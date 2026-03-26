using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace ScreenshotScraper.Extraction.HandHistory;

public sealed class PreHeroScreenshotParser : IPreHeroScreenshotParser
{
    private const int HeroSeatIndex = 1;
    private const string GenericHeroName = "GenericHeroName";

    private readonly IOcrEngine _ocrEngine;
    private readonly ITableHeaderExtractor _tableHeaderExtractor;
    private readonly ISeatSnapshotExtractor _seatSnapshotExtractor;
    private readonly ICardExtractor _cardExtractor;
    private readonly ITableVisionDetector _tableVisionDetector;
    private readonly IPreHeroActionInferencer _preHeroActionInferencer;

    public PreHeroScreenshotParser(
        IOcrEngine ocrEngine,
        ITableHeaderExtractor tableHeaderExtractor,
        ISeatSnapshotExtractor seatSnapshotExtractor,
        ICardExtractor cardExtractor,
        ITableVisionDetector tableVisionDetector,
        IPreHeroActionInferencer preHeroActionInferencer)
    {
        _ocrEngine = ocrEngine;
        _tableHeaderExtractor = tableHeaderExtractor;
        _seatSnapshotExtractor = seatSnapshotExtractor;
        _cardExtractor = cardExtractor;
        _tableVisionDetector = tableVisionDetector;
        _preHeroActionInferencer = preHeroActionInferencer;
    }

    public async Task<PartialHandHistorySnapshot> ParseAsync(CapturedImage image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rawText = await _ocrEngine.ReadTextAsync(image, cancellationToken).ConfigureAwait(false);
        var header = _tableHeaderExtractor.Extract(image, rawText);
        var basePlayers = _seatSnapshotExtractor.Extract(image, rawText).ToList();
        var heroCardRegionImage = CropHeroCardRegion(image);
        var heroCardRegionText = await ReadHeroCardRegionTextAsync(heroCardRegionImage, cancellationToken).ConfigureAwait(false);
        var heroCards = _cardExtractor.ExtractHeroCards(heroCardRegionImage, heroCardRegionText);
        var heroSeat = DetectHeroSeat(basePlayers, heroCards);
        var tableDetection = _tableVisionDetector.Detect(image, basePlayers);
        var seatLocalPlayers = await ExtractSeatPlayersFromRoisAsync(image, tableDetection, cancellationToken).ConfigureAwait(false);
        var mergedPlayers = MergeSeatPlayers(basePlayers, seatLocalPlayers);
        var dealerSeatField = BuildDealerSeatField(tableDetection);
        var dealerSeat = int.TryParse(dealerSeatField.ParsedValue, out var parsedDealerSeat) ? parsedDealerSeat : (int?)null;

        var players = ApplyDealerAndHeroCards(mergedPlayers, dealerSeat, heroSeat, heroCards, tableDetection);
        var hero = players.FirstOrDefault(player => player.IsHero);
        var heroNameField = BuildHeroNameField(hero, heroSeat.HasValue);
        var heroPositionField = BuildHeroPositionField();
        var actions = _preHeroActionInferencer.Infer(players);

        return new PartialHandHistorySnapshot
        {
            GameCode = header.GameCode,
            StartDate = header.StartDate,
            Players = players,
            Round0Actions = actions.Round0Actions.ToList(),
            Round1PocketCards = BuildPocketCards(players, heroCards),
            Round1ObservedActions = actions.Round1Actions.ToList(),
            GameCodeField = header.GameCodeField,
            HeroNameField = heroNameField,
            DealerSeatField = dealerSeatField,
            HeroPositionField = heroPositionField
        };
    }


    private static ExtractedField BuildDealerSeatField(TableDetectionResult tableDetection)
    {
        var diagnostics = string.Join(
            "; ",
            tableDetection.PerSeatDiagnostics.OrderBy(pair => pair.Key).Select(pair =>
                $"S{pair.Key}:dealer={pair.Value.DealerScore:0.000},occupied={pair.Value.IsOccupied},occScore={pair.Value.OccupancyScore:0.000}"));

        if (tableDetection.DealerSeat.HasValue)
        {
            return new ExtractedField
            {
                Name = "DealerSeat",
                RawText = diagnostics,
                ParsedValue = tableDetection.DealerSeat.Value.ToString(),
                IsValid = true,
                Confidence = tableDetection.DealerConfidence,
                Reason = $"OpenCV template match selected seat {tableDetection.DealerSeat.Value}. {diagnostics}"
            };
        }

        return new ExtractedField
        {
            Name = "DealerSeat",
            RawText = diagnostics,
            ParsedValue = null,
            IsValid = false,
            Error = "Dealer button was not confidently detected.",
            Confidence = tableDetection.DealerConfidence,
            Reason = $"OpenCV dealer detection did not pass threshold. {diagnostics}"
        };
    }

    private static ExtractedField BuildHeroNameField(SnapshotPlayer? hero, bool heroSeatDetected)
    {
        var usedFallback = heroSeatDetected && string.Equals(hero?.Name, GenericHeroName, StringComparison.Ordinal);

        return new ExtractedField
        {
            Name = "HeroName",
            RawText = hero?.Name,
            ParsedValue = hero?.Name,
            IsValid = heroSeatDetected,
            Error = heroSeatDetected ? null : "Hero seat was not confidently detected.",
            Confidence = heroSeatDetected ? (usedFallback ? 0.7 : 1.0) : 0,
            Reason = !heroSeatDetected
                ? "Hero seat could not be reliably identified from visible hero cards."
                : usedFallback
                    ? "Hero seat was identified from visible hero cards (seat 1), but hero name OCR was missing/invalid; fallback GenericHeroName was used."
                    : "Hero seat was identified from visible hero cards (seat 1), and hero name was read from the seat region."
        };
    }

    private static ExtractedField BuildHeroPositionField()
    {
        return new ExtractedField
        {
            Name = "HeroPosition",
            RawText = null,
            ParsedValue = null,
            IsValid = false,
            Error = "Hero position assignment is deferred.",
            Confidence = 0,
            Reason = "Position assignment (BTN/SB/BB/UTG/HJ/CO) is intentionally deferred until occupied-seat and dealer-snapshot validation is complete."
        };
    }

    private static List<SnapshotPocketCards> BuildPocketCards(IReadOnlyList<SnapshotPlayer> players, string heroCards)
    {
        return players
            .Select(player => new SnapshotPocketCards
            {
                Player = player.Name,
                Cards = player.IsHero && !string.IsNullOrWhiteSpace(heroCards)
                    ? heroCards
                    : "X X"
            })
            .ToList();
    }

    private static List<SnapshotPlayer> ApplyDealerAndHeroCards(IReadOnlyList<SnapshotPlayer> players, int? dealerSeat, int? heroSeat, string heroCards, TableDetectionResult tableDetection)
    {
        var playersBySeat = players.ToDictionary(player => player.Seat);
        var occupiedSeats = tableDetection.OccupiedSeats
            .Where(seat => seat is >= 1 and <= 6)
            .Distinct()
            .OrderBy(seat => seat)
            .ToList();

        if (occupiedSeats.Count == 0)
        {
            occupiedSeats = players
                .Where(player => !string.IsNullOrWhiteSpace(player.Name) || !string.IsNullOrWhiteSpace(player.Chips) || !string.IsNullOrWhiteSpace(player.Bet))
                .Select(player => player.Seat)
                .Distinct()
                .OrderBy(seat => seat)
                .ToList();
        }

        if (heroSeat.HasValue && !occupiedSeats.Contains(heroSeat.Value))
        {
            occupiedSeats.Add(heroSeat.Value);
        }

        occupiedSeats = BuildDealerRelativeSeatOrder(occupiedSeats, dealerSeat);

        var dealerIsOnOccupiedSeat = dealerSeat.HasValue && occupiedSeats.Contains(dealerSeat.Value);

        return occupiedSeats
            .Select(seat =>
            {
                playersBySeat.TryGetValue(seat, out var extracted);
                var isHero = heroSeat.HasValue && seat == heroSeat.Value;

                var resolvedName = ResolvePlayerName(extracted, seat, isHero);
                tableDetection.PerSeatDiagnostics.TryGetValue(seat, out var diagnostics);
                var failure = resolvedName.EndsWith("_Unknown", StringComparison.Ordinal) ? "Name OCR missing or rejected." : string.Empty;

                Debug.WriteLine($"[SeatMap] seat={seat}; occupied={(diagnostics?.IsOccupied ?? false)}; occScore={(diagnostics?.OccupancyScore ?? 0):0.000}; dealerScore={(diagnostics?.DealerScore ?? 0):0.000}; rawName='{extracted?.Name ?? string.Empty}'; parsedName='{resolvedName}'; stack='{extracted?.Chips ?? string.Empty}'; bet='{extracted?.Bet ?? string.Empty}'; failure='{failure}'");

                return new SnapshotPlayer
                {
                    Seat = seat,
                    Name = resolvedName,
                    Chips = extracted?.Chips ?? string.Empty,
                    Dealer = dealerIsOnOccupiedSeat && seat == dealerSeat,
                    Bet = extracted?.Bet ?? string.Empty,
                    Win = extracted?.Win ?? string.Empty,
                    Muck = extracted?.Muck ?? string.Empty,
                    Cashout = extracted?.Cashout ?? string.Empty,
                    CashoutFee = extracted?.CashoutFee ?? string.Empty,
                    RakeAmount = extracted?.RakeAmount ?? string.Empty,
                    Position = string.Empty,
                    IsHero = isHero,
                    AppearsFolded = extracted?.AppearsFolded ?? false,
                    HasVisibleCards = isHero && !string.IsNullOrWhiteSpace(heroCards)
                };
            })
            .ToList();
    }

    private static string ResolvePlayerName(SnapshotPlayer? player, int seat, bool isHero)
    {
        if (isHero)
        {
            var heroName = player?.Name;
            return IsReliableHeroName(heroName) ? heroName! : GenericHeroName;
        }

        if (IsReliableNonHeroName(player?.Name))
        {
            return player!.Name;
        }

        return $"Seat{seat}_Unknown";
    }

    private static IReadOnlyList<SnapshotPlayer> MergeSeatPlayers(IReadOnlyList<SnapshotPlayer> basePlayers, IReadOnlyList<SnapshotPlayer> seatLocalPlayers)
    {
        var bySeat = new Dictionary<int, SnapshotPlayer>();
        foreach (var player in basePlayers)
        {
            bySeat[player.Seat] = player;
        }

        foreach (var local in seatLocalPlayers)
        {
            if (!bySeat.TryGetValue(local.Seat, out var existing))
            {
                bySeat[local.Seat] = local;
                continue;
            }

            bySeat[local.Seat] = new SnapshotPlayer
            {
                Seat = existing.Seat,
                IsHero = existing.IsHero,
                Dealer = existing.Dealer || local.Dealer,
                Name = IsReliableNonHeroName(existing.Name) || IsReliableHeroName(existing.Name) ? existing.Name : local.Name,
                Chips = !string.IsNullOrWhiteSpace(existing.Chips) ? existing.Chips : local.Chips,
                Bet = !string.IsNullOrWhiteSpace(existing.Bet) ? existing.Bet : local.Bet,
                Win = existing.Win ?? local.Win,
                Muck = existing.Muck ?? local.Muck,
                Cashout = existing.Cashout ?? local.Cashout,
                CashoutFee = existing.CashoutFee ?? local.CashoutFee,
                RakeAmount = existing.RakeAmount ?? local.RakeAmount,
                Position = existing.Position ?? local.Position,
                AppearsFolded = existing.AppearsFolded || local.AppearsFolded,
                HasVisibleCards = existing.HasVisibleCards || local.HasVisibleCards
            };
        }

        return bySeat.Values.OrderBy(player => player.Seat).ToList();
    }

    private static List<int> BuildDealerRelativeSeatOrder(IReadOnlyCollection<int> occupiedSeats, int? dealerSeat)
    {
        if (!dealerSeat.HasValue || occupiedSeats.Count == 0)
        {
            return occupiedSeats.OrderBy(seat => seat).ToList();
        }

        return occupiedSeats
             .OrderBy(seat => (seat - dealerSeat.Value + 6) % 6)
            .ThenBy(seat => seat)
            .ToList();
    }

    private static bool IsReliableNonHeroName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        var compact = new string(trimmed.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length < 3)
        {
            return false;
        }

        if (compact.All(char.IsDigit))
        {
            return compact.Length >= 5;
        }

        return true;
    }

    private static bool IsReliableHeroName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        return trimmed.Any(char.IsLetter);
    }

    private async Task<IReadOnlyList<SnapshotPlayer>> ExtractSeatPlayersFromRoisAsync(CapturedImage image, TableDetectionResult detection, CancellationToken cancellationToken)
    {
        if (image.ImageBytes.Length == 0)
        {
            return [];
        }

        var occupiedSeats = detection.OccupiedSeats
            .Where(seat => seat is >= 1 and <= 6)
            .Distinct()
            .ToHashSet();

        var seatRois = new SixMaxTableVisionLayout().GetSeatRois(image.Width, image.Height);
        var players = new List<SnapshotPlayer>();

        foreach (var seat in seatRois)
        {
            if (occupiedSeats.Count > 0 && !occupiedSeats.Contains(seat.Seat))
            {
                continue;
            }

            var nameText = await ReadSeatRoiTextAsync(image, seat.NameRoi, cancellationToken).ConfigureAwait(false);
            var stackText = await ReadSeatRoiTextAsync(image, seat.StackRoi, cancellationToken).ConfigureAwait(false);
            var betText = await ReadSeatRoiTextAsync(image, seat.BetRoi, cancellationToken).ConfigureAwait(false);
            var name = ExtractNameCandidate(nameText);
            var chips = ExtractNumberWithOptionalBb(stackText);
            var bet = ExtractNumberWithOptionalBb(betText);

            Debug.WriteLine($"[SeatLocalOCR] seat={seat.Seat}; nameRaw='{nameText}'; stackRaw='{stackText}'; betRaw='{betText}'; parsedName='{name}'; parsedStack='{chips}'; parsedBet='{bet}'");

            players.Add(new SnapshotPlayer
            {
                Seat = seat.Seat,
                Name = name,
                Chips = chips,
                Bet = bet,
                IsHero = seat.Seat == HeroSeatIndex,
                Dealer = false,
                Win = string.Empty,
                Muck = string.Empty,
                Cashout = string.Empty,
                CashoutFee = string.Empty,
                RakeAmount = string.Empty,
                Position = string.Empty,
                AppearsFolded = false,
                HasVisibleCards = false
            });
        }

        return players;
    }

    private async Task<string> ReadSeatRoiTextAsync(CapturedImage image, Rectangle roi, CancellationToken cancellationToken)
    {
        var cropped = CropRegion(image, roi);
        if (cropped.ImageBytes.Length == 0)
        {
            return string.Empty;
        }

        return await _ocrEngine.ReadTextAsync(cropped, cancellationToken).ConfigureAwait(false);
    }

    private static string ExtractNameCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Replace("\r", " ").Replace("\n", " ");
        foreach (Match match in Regex.Matches(cleaned, @"[A-Za-z0-9_]{3,}"))
        {
            var token = match.Value.Trim();
            if (token.Equals("TIME", StringComparison.OrdinalIgnoreCase)
                || token.Equals("BANK", StringComparison.OrdinalIgnoreCase)
                || token.Equals("POT", StringComparison.OrdinalIgnoreCase)
                || token.Equals("MAX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsReliableNonHeroName(token))
            {
                return token;
            }
        }

        return string.Empty;
    }

    private static string ExtractNumberWithOptionalBb(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = Regex.Match(value, @"(?<num>\d+(?:[\.,]\d+)?)\s*(?:BB)?", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["num"].Value.Replace(',', '.') : string.Empty;
    }

    private static int? DetectHeroSeat(IReadOnlyList<SnapshotPlayer> players, string heroCards)
    {
        if (string.IsNullOrWhiteSpace(heroCards))
        {
            return null;
        }

        return players.Any(player => player.Seat == HeroSeatIndex)
            ? HeroSeatIndex
            : null;
    }

    private async Task<string> ReadHeroCardRegionTextAsync(CapturedImage heroCardRegionImage, CancellationToken cancellationToken)
    {
        if (heroCardRegionImage.ImageBytes.Length == 0)
        {
            return string.Empty;
        }

        return await _ocrEngine.ReadTextAsync(heroCardRegionImage, cancellationToken).ConfigureAwait(false);
    }

    private static CapturedImage CropHeroCardRegion(CapturedImage image)
    {
        if (image.ImageBytes.Length == 0)
        {
            return new CapturedImage();
        }

        try
        {
            using var sourceStream = new MemoryStream(image.ImageBytes);
            using var sourceBitmap = new Bitmap(sourceStream);

            var width = sourceBitmap.Width;
            var height = sourceBitmap.Height;
            if (width == 0 || height == 0)
            {
                return new CapturedImage();
            }

            var crop = new Rectangle(
                x: (int)(width * 0.36),
                y: (int)(height * 0.58),
                width: Math.Max(1, (int)(width * 0.28)),
                height: Math.Max(1, (int)(height * 0.30)));
            crop.Intersect(new Rectangle(0, 0, width, height));
            if (crop.Width == 0 || crop.Height == 0)
            {
                return new CapturedImage();
            }

            using var croppedBitmap = sourceBitmap.Clone(crop, sourceBitmap.PixelFormat);
            using var targetStream = new MemoryStream();
            croppedBitmap.Save(targetStream, ImageFormat.Png);

            return new CapturedImage
            {
                ImageBytes = targetStream.ToArray(),
                Width = croppedBitmap.Width,
                Height = croppedBitmap.Height,
                CapturedAtUtc = image.CapturedAtUtc,
                SourceDescription = $"{image.SourceDescription}|HeroCardsRegion",
                WindowTitle = image.WindowTitle,
                ProcessName = image.ProcessName,
                WindowLeft = image.WindowLeft + crop.Left,
                WindowTop = image.WindowTop + crop.Top,
                WindowWidth = crop.Width,
                WindowHeight = crop.Height,
                IsVisible = image.IsVisible,
                IsForegroundWindow = image.IsForegroundWindow,
                WindowHandle = image.WindowHandle,
                CaptureMethod = image.CaptureMethod,
                MonitorDeviceName = image.MonitorDeviceName
            };
        }
        catch
        {
            return new CapturedImage();
        }
    }

    private static CapturedImage CropRegion(CapturedImage image, Rectangle region)
    {
        if (image.ImageBytes.Length == 0 || region.Width <= 0 || region.Height <= 0)
        {
            return new CapturedImage();
        }

        try
        {
            using var sourceStream = new MemoryStream(image.ImageBytes);
            using var sourceBitmap = new Bitmap(sourceStream);
            var boundedRegion = Rectangle.Intersect(new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height), region);
            if (boundedRegion.Width <= 0 || boundedRegion.Height <= 0)
            {
                return new CapturedImage();
            }

            using var croppedBitmap = sourceBitmap.Clone(boundedRegion, sourceBitmap.PixelFormat);
            using var outputStream = new MemoryStream();
            croppedBitmap.Save(outputStream, ImageFormat.Png);

            return new CapturedImage
            {
                ImageBytes = outputStream.ToArray(),
                Width = boundedRegion.Width,
                Height = boundedRegion.Height,
                CapturedAtUtc = image.CapturedAtUtc,
                WindowTitle = image.WindowTitle,
                WindowLeft = image.WindowLeft + boundedRegion.X,
                WindowTop = image.WindowTop + boundedRegion.Y
            };
        }
        catch
        {
            return new CapturedImage();
        }
    }
}
