using OpenCvSharp;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;

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
        var seatLocalResult = await ExtractSeatPlayersFromRoisAsync(image, tableDetection, cancellationToken).ConfigureAwait(false);
        var mergedPlayers = MergeSeatPlayers(basePlayers, seatLocalResult.Players);
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
            HeroPositionField = heroPositionField,
            SeatLocalOcrDiagnostics = seatLocalResult.Diagnostics
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

    private async Task<SeatLocalExtractionResult> ExtractSeatPlayersFromRoisAsync(CapturedImage image, TableDetectionResult detection, CancellationToken cancellationToken)
    {
        if (image.ImageBytes.Length == 0)
        {
            return new SeatLocalExtractionResult([], "No screenshot bytes available for seat-local OCR.");
        }

        var occupiedSeats = detection.OccupiedSeats
            .Where(seat => seat is >= 1 and <= 6)
            .Distinct()
            .ToHashSet();

        var seatRois = new SixMaxTableVisionLayout().GetSeatRois(image.Width, image.Height);
        var players = new List<SnapshotPlayer>();
        var diagnostics = new List<string>();
        var debugDirectory = EnsureSeatLocalDebugDirectory(image.CapturedAtUtc);

        foreach (var seat in seatRois)
        {
            var isOccupied = occupiedSeats.Count == 0 || occupiedSeats.Contains(seat.Seat);
            var seatFullBounds = BuildSeatBounds(seat);

            var nameRead = await ReadSeatRoiTextAsync(image, seat.NameRoi, SeatLocalOcrPreprocessor.PreprocessNameRoi, cancellationToken).ConfigureAwait(false);
            var stackRead = await ReadSeatRoiTextAsync(image, seat.StackRoi, SeatLocalOcrPreprocessor.PreprocessNumericRoi, cancellationToken).ConfigureAwait(false);
            var betRead = await ReadSeatRoiTextAsync(image, seat.BetRoi, SeatLocalOcrPreprocessor.PreprocessNumericRoi, cancellationToken).ConfigureAwait(false);

            var name = SeatLocalTextParser.ParseName(nameRead.OcrText);
            var chips = SeatLocalTextParser.ParseNumber(stackRead.OcrText);
            var bet = SeatLocalTextParser.ParseNumber(betRead.OcrText);

            var nameRejection = IsReliableNonHeroName(name) || seat.Seat == HeroSeatIndex ? string.Empty : "name rejected by reliability filter";
            var stackRejection = chips.Length == 0 && !string.IsNullOrWhiteSpace(stackRead.OcrText) ? "numeric parse failed" : string.Empty;
            var betRejection = bet.Length == 0 && !string.IsNullOrWhiteSpace(betRead.OcrText) ? "numeric parse failed" : string.Empty;

            SaveSeatLocalDebugArtifacts(image, debugDirectory, seat, seatFullBounds, nameRead, stackRead, betRead);

            diagnostics.Add($"Seat {seat.Seat}: occupied={isOccupied}; full={FormatRect(seatFullBounds)}; name={FormatRect(seat.NameRoi)} raw='{Sanitize(nameRead.OcrText)}' parsed='{name}' reject='{nameRejection}'; stack={FormatRect(seat.StackRoi)} raw='{Sanitize(stackRead.OcrText)}' parsed='{chips}' reject='{stackRejection}'; bet={FormatRect(seat.BetRoi)} raw='{Sanitize(betRead.OcrText)}' parsed='{bet}' reject='{betRejection}'");

            Debug.WriteLine($"[SeatLocalOCR] seat={seat.Seat}; occupied={isOccupied}; full={FormatRect(seatFullBounds)}; nameRoi={FormatRect(seat.NameRoi)}; stackRoi={FormatRect(seat.StackRoi)}; betRoi={FormatRect(seat.BetRoi)}; nameRaw='{Sanitize(nameRead.OcrText)}'; stackRaw='{Sanitize(stackRead.OcrText)}'; betRaw='{Sanitize(betRead.OcrText)}'; parsedName='{name}'; parsedStack='{chips}'; parsedBet='{bet}'; nameReject='{nameRejection}'; stackReject='{stackRejection}'; betReject='{betRejection}'");

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

        var diagnosticsText = string.Join(Environment.NewLine, diagnostics);
        File.WriteAllText(Path.Combine(debugDirectory, "seat_ocr_summary.txt"), diagnosticsText);
        return new SeatLocalExtractionResult(players, diagnosticsText);
    }

    private async Task<SeatRoiReadResult> ReadSeatRoiTextAsync(
        CapturedImage image,
        Rectangle roi,
        Func<byte[], byte[]> preprocess,
        CancellationToken cancellationToken)
    {
        var cropped = CropRegion(image, roi);
        if (cropped.ImageBytes.Length == 0)
        {
            return new SeatRoiReadResult(cropped, new CapturedImage(), string.Empty);
        }

        var preprocessedBytes = preprocess(cropped.ImageBytes);
        var preprocessed = preprocessedBytes.Length == 0
            ? new CapturedImage()
            : new CapturedImage
            {
                ImageBytes = preprocessedBytes,
                Width = cropped.Width,
                Height = cropped.Height,
                CapturedAtUtc = image.CapturedAtUtc,
                WindowTitle = image.WindowTitle
            };

        var ocrInput = preprocessed.ImageBytes.Length > 0 ? preprocessed : cropped;
        var text = await _ocrEngine.ReadTextAsync(ocrInput, cancellationToken).ConfigureAwait(false);
        return new SeatRoiReadResult(cropped, preprocessed, text);
    }

    private static Rectangle BuildSeatBounds(SeatVisionRoi seat)
    {
        var left = new[] { seat.NameRoi.Left, seat.StackRoi.Left, seat.BetRoi.Left, seat.OccupancyRoi.Left }.Min();
        var top = new[] { seat.NameRoi.Top, seat.StackRoi.Top, seat.BetRoi.Top, seat.OccupancyRoi.Top }.Min();
        var right = new[] { seat.NameRoi.Right, seat.StackRoi.Right, seat.BetRoi.Right, seat.OccupancyRoi.Right }.Max();
        var bottom = new[] { seat.NameRoi.Bottom, seat.StackRoi.Bottom, seat.BetRoi.Bottom, seat.OccupancyRoi.Bottom }.Max();
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static void SaveSeatLocalDebugArtifacts(CapturedImage source, string debugDirectory, SeatVisionRoi seat, Rectangle fullBounds, SeatRoiReadResult nameRead, SeatRoiReadResult stackRead, SeatRoiReadResult betRead)
    {
        var fullCrop = CropRegion(source, fullBounds);
        File.WriteAllBytes(Path.Combine(debugDirectory, $"seat_{seat.Seat}_full.png"), fullCrop.ImageBytes);
        File.WriteAllBytes(Path.Combine(debugDirectory, $"seat_{seat.Seat}_name_raw.png"), nameRead.Raw.ImageBytes);
        File.WriteAllBytes(Path.Combine(debugDirectory, $"seat_{seat.Seat}_name_preprocessed.png"), nameRead.Preprocessed.ImageBytes.Length == 0 ? nameRead.Raw.ImageBytes : nameRead.Preprocessed.ImageBytes);
        File.WriteAllBytes(Path.Combine(debugDirectory, $"seat_{seat.Seat}_stack_raw.png"), stackRead.Raw.ImageBytes);
        File.WriteAllBytes(Path.Combine(debugDirectory, $"seat_{seat.Seat}_stack_preprocessed.png"), stackRead.Preprocessed.ImageBytes.Length == 0 ? stackRead.Raw.ImageBytes : stackRead.Preprocessed.ImageBytes);
        File.WriteAllBytes(Path.Combine(debugDirectory, $"seat_{seat.Seat}_bet_raw.png"), betRead.Raw.ImageBytes);
        File.WriteAllBytes(Path.Combine(debugDirectory, $"seat_{seat.Seat}_bet_preprocessed.png"), betRead.Preprocessed.ImageBytes.Length == 0 ? betRead.Raw.ImageBytes : betRead.Preprocessed.ImageBytes);
    }

    private static string EnsureSeatLocalDebugDirectory(DateTime capturedAt)
    {
        var timestamp = (capturedAt == default ? DateTime.UtcNow : capturedAt).ToString("yyyyMMdd_HHmmssfff");
        var path = Path.Combine("debug", "output", timestamp);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FormatRect(Rectangle rect) => $"({rect.Left},{rect.Top},{rect.Width},{rect.Height})";

    private static string Sanitize(string? value) => (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

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

        using var source = Cv2.ImDecode(image.ImageBytes, ImreadModes.Color);
        if (source.Empty())
        {
            return new CapturedImage();
        }

        var bounded = new OpenCvSharp.Rect(region.X, region.Y, region.Width, region.Height);
        bounded = bounded.Intersect(new OpenCvSharp.Rect(0, 0, source.Width, source.Height));
        if (bounded.Width <= 0 || bounded.Height <= 0)
        {
            return new CapturedImage();
        }

        using var cropped = new Mat(source, bounded);
        Cv2.ImEncode(".png", cropped, out var encoded);

        return new CapturedImage
        {
            ImageBytes = encoded,
            Width = bounded.Width,
            Height = bounded.Height,
            CapturedAtUtc = image.CapturedAtUtc,
            WindowTitle = image.WindowTitle,
            WindowLeft = image.WindowLeft + bounded.X,
            WindowTop = image.WindowTop + bounded.Y
        };
    }

    private sealed record SeatRoiReadResult(CapturedImage Raw, CapturedImage Preprocessed, string OcrText);

    private sealed record SeatLocalExtractionResult(IReadOnlyList<SnapshotPlayer> Players, string Diagnostics);
}
