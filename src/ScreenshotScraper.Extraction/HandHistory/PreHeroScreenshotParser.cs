using OpenCvSharp;
using ScreenshotScraper.Core.Interfaces;
using ScreenshotScraper.Core.Interfaces.HandHistory;
using ScreenshotScraper.Core.Models;
using ScreenshotScraper.Core.Models.HandHistory;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;

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

        var rawText = (await _ocrEngine.ReadAsync(image, new OcrRequest("full_table", "raw"), cancellationToken).ConfigureAwait(false)).Text;
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
            SeatLocalOcrDiagnostics = seatLocalResult.Diagnostics,
            SeatLocalOcrDebugArtifacts = seatLocalResult.DebugArtifacts
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
            return new SeatLocalExtractionResult([], "No screenshot bytes available for seat-local OCR.", []);
        }

        var occupiedSeats = detection.OccupiedSeats
            .Where(seat => seat is >= 1 and <= 6)
            .Distinct()
            .ToHashSet();

        var seatRois = new SixMaxTableVisionLayout().GetSeatRois(image.Width, image.Height);
        var players = new List<SnapshotPlayer>();
        var diagnostics = new List<string>();
        var debugArtifacts = new List<SeatDebugArtifact>();
        var debugDirectory = EnsureSeatLocalDebugDirectory(image.CapturedAtUtc);

        foreach (var seat in seatRois)
        {
            var isOccupied = occupiedSeats.Count == 0 || occupiedSeats.Contains(seat.Seat);
            var seatFullBounds = BuildSeatBounds(seat);

            var nameRead = await ReadSeatRoiTextAsync(image, debugDirectory, seat.Seat, "name", seat.NameRoi, bytes => SeatLocalOcrPreprocessor.BuildVariantsForName(bytes), cancellationToken).ConfigureAwait(false);
            var stackRead = await ReadSeatRoiTextAsync(image, debugDirectory, seat.Seat, "stack", seat.StackRoi, bytes => SeatLocalOcrPreprocessor.BuildVariantsForNumeric(bytes), cancellationToken).ConfigureAwait(false);
            var betRead = await ReadSeatRoiTextAsync(image, debugDirectory, seat.Seat, "bet", seat.BetRoi, bytes => SeatLocalOcrPreprocessor.BuildVariantsForNumeric(bytes), cancellationToken).ConfigureAwait(false);

            var name = SeatLocalTextParser.ParseName(nameRead.OcrText);
            if (seat.Seat == HeroSeatIndex && ContainsTimeBankText(nameRead.OcrText))
            {
                name = "Seat1_Unknown";
            }
            var chips = SeatLocalTextParser.ParseNumber(stackRead.OcrText);
            var bet = SeatLocalTextParser.ParseNumber(betRead.OcrText);

            var nameRejection = IsReliableNonHeroName(name) || seat.Seat == HeroSeatIndex ? string.Empty : "name rejected by reliability filter";
            var stackRejection = chips.Length == 0 && !string.IsNullOrWhiteSpace(stackRead.OcrText) ? "numeric parse failed" : string.Empty;
            var betRejection = bet.Length == 0 && !string.IsNullOrWhiteSpace(betRead.OcrText) ? "numeric parse failed" : string.Empty;

            diagnostics.Add($"Seat {seat.Seat}: occupied={isOccupied}; full={FormatRect(seatFullBounds)}; name={FormatRect(seat.NameRoi)} variant={nameRead.VariantUsed} backend={nameRead.OcrResult.Backend} conf={FormatConfidence(nameRead.OcrResult.Confidence)} raw='{Sanitize(nameRead.OcrText)}' parsed='{name}' reject='{nameRejection}'; stack={FormatRect(seat.StackRoi)} variant={stackRead.VariantUsed} backend={stackRead.OcrResult.Backend} conf={FormatConfidence(stackRead.OcrResult.Confidence)} raw='{Sanitize(stackRead.OcrText)}' parsed='{chips}' reject='{stackRejection}'; bet={FormatRect(seat.BetRoi)} variant={betRead.VariantUsed} backend={betRead.OcrResult.Backend} conf={FormatConfidence(betRead.OcrResult.Confidence)} raw='{Sanitize(betRead.OcrText)}' parsed='{bet}' reject='{betRejection}'");
            diagnostics.Add($"  name variants: {FormatVariantDiagnostics(nameRead.Attempts)}");
            diagnostics.Add($"  stack variants: {FormatVariantDiagnostics(stackRead.Attempts)}");
            diagnostics.Add($"  bet variants: {FormatVariantDiagnostics(betRead.Attempts)}");

            Debug.WriteLine($"[SeatLocalOCR] seat={seat.Seat}; occupied={isOccupied}; full={FormatRect(seatFullBounds)}; nameRoi={FormatRect(seat.NameRoi)}; stackRoi={FormatRect(seat.StackRoi)}; betRoi={FormatRect(seat.BetRoi)}; nameVariant={nameRead.VariantUsed}; stackVariant={stackRead.VariantUsed}; betVariant={betRead.VariantUsed}; nameBackend={nameRead.OcrResult.Backend}; stackBackend={stackRead.OcrResult.Backend}; betBackend={betRead.OcrResult.Backend}; nameConf={FormatConfidence(nameRead.OcrResult.Confidence)}; stackConf={FormatConfidence(stackRead.OcrResult.Confidence)}; betConf={FormatConfidence(betRead.OcrResult.Confidence)}; nameElapsedMs={nameRead.OcrResult.ElapsedMilliseconds ?? 0}; stackElapsedMs={stackRead.OcrResult.ElapsedMilliseconds ?? 0}; betElapsedMs={betRead.OcrResult.ElapsedMilliseconds ?? 0}; nameRaw='{Sanitize(nameRead.OcrText)}'; stackRaw='{Sanitize(stackRead.OcrText)}'; betRaw='{Sanitize(betRead.OcrText)}'; parsedName='{name}'; parsedStack='{chips}'; parsedBet='{bet}'; nameReject='{nameRejection}'; stackReject='{stackRejection}'; betReject='{betRejection}'");

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

            var seatDebugArtifact = SaveSeatLocalDebugArtifacts(
                image,
                debugDirectory,
                seat,
                seatFullBounds,
                nameRead,
                name,
                nameRejection,
                stackRead,
                chips,
                stackRejection,
                betRead,
                bet,
                betRejection);
            debugArtifacts.Add(seatDebugArtifact);
        }

        var diagnosticsText = string.Join(Environment.NewLine, diagnostics);
        File.WriteAllText(Path.Combine(debugDirectory, "seat_ocr_summary.txt"), diagnosticsText);
        File.WriteAllText(
            Path.Combine(debugDirectory, "seat_ocr_debug.json"),
            JsonSerializer.Serialize(debugArtifacts, new JsonSerializerOptions { WriteIndented = true }));
        return new SeatLocalExtractionResult(players, diagnosticsText, debugArtifacts);
    }

    private async Task<SeatRoiReadResult> ReadSeatRoiTextAsync(
        CapturedImage image,
        string debugDirectory,
        int seat,
        string roiType,
        Rectangle roi,
        Func<byte[], IReadOnlyList<SeatLocalOcrVariantImage>> buildVariants,
        CancellationToken cancellationToken)
    {
        var cropped = CropRegion(image, roi);
        if (cropped.ImageBytes.Length == 0)
        {
            return new SeatRoiReadResult(cropped, "raw", new OcrResult(string.Empty, "none"), string.Empty, []);
        }

        var variantInputs = buildVariants(cropped.ImageBytes);
        if (variantInputs.Count == 0)
        {
            variantInputs = [new SeatLocalOcrVariantImage("raw", cropped.ImageBytes, cropped.Width, cropped.Height)];
        }

        var attempts = new List<SeatOcrAttempt>(variantInputs.Count);
        foreach (var variantInput in variantInputs)
        {
            var attemptImage = new CapturedImage
            {
                ImageBytes = variantInput.ImageBytes,
                Width = variantInput.Width,
                Height = variantInput.Height,
                CapturedAtUtc = image.CapturedAtUtc,
                WindowTitle = image.WindowTitle
            };

            var request = new OcrRequest(roiType, variantInput.VariantName, PreferRecognitionOnly: true);
            var result = await _ocrEngine.ReadAsync(attemptImage, request, cancellationToken).ConfigureAwait(false);
            attempts.Add(new SeatOcrAttempt(variantInput, result));
        }

        var selectedAttempt = attempts
            .OrderByDescending(attempt => ScoreAttempt(roiType, attempt.OcrResult))
            .ThenByDescending(attempt => attempt.OcrResult.Confidence ?? 0)
            .ThenByDescending(attempt => Sanitize(attempt.OcrResult.Text).Length)
            .FirstOrDefault() ?? new SeatOcrAttempt(new SeatLocalOcrVariantImage("raw", cropped.ImageBytes, cropped.Width, cropped.Height), new OcrResult(string.Empty, "none"));

        var attemptSummaries = attempts
            .Select(attempt => new SeatOcrAttemptSummary(
                attempt.Variant.VariantName,
                attempt.Variant.ImageBytes,
                attempt.OcrResult,
                attempt.Variant.VariantName == selectedAttempt.Variant.VariantName,
                attempt.Variant.VariantName == selectedAttempt.Variant.VariantName ? null : BuildAttemptRejectionReason(roiType, attempt, selectedAttempt)))
            .ToList();

        if (selectedAttempt.OcrResult.RawPayload is { Length: > 0 })
        {
            var outputPath = Path.Combine(debugDirectory, $"seat_{seat}_{roiType}_{selectedAttempt.Variant.VariantName}_ocr.json");
            File.WriteAllText(outputPath, selectedAttempt.OcrResult.RawPayload);
        }

        return new SeatRoiReadResult(cropped, selectedAttempt.Variant.VariantName, selectedAttempt.OcrResult, selectedAttempt.OcrResult.Text, attemptSummaries);
    }

    private static Rectangle BuildSeatBounds(SeatVisionRoi seat)
    {
        var left = new[] { seat.NameRoi.Left, seat.StackRoi.Left, seat.BetRoi.Left, seat.OccupancyRoi.Left }.Min();
        var top = new[] { seat.NameRoi.Top, seat.StackRoi.Top, seat.BetRoi.Top, seat.OccupancyRoi.Top }.Min();
        var right = new[] { seat.NameRoi.Right, seat.StackRoi.Right, seat.BetRoi.Right, seat.OccupancyRoi.Right }.Max();
        var bottom = new[] { seat.NameRoi.Bottom, seat.StackRoi.Bottom, seat.BetRoi.Bottom, seat.OccupancyRoi.Bottom }.Max();
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static SeatDebugArtifact SaveSeatLocalDebugArtifacts(
        CapturedImage source,
        string debugDirectory,
        SeatVisionRoi seat,
        Rectangle fullBounds,
        SeatRoiReadResult nameRead,
        string parsedName,
        string nameRejection,
        SeatRoiReadResult stackRead,
        string parsedStack,
        string stackRejection,
        SeatRoiReadResult betRead,
        string parsedBet,
        string betRejection)
    {
        var fullCrop = CropRegion(source, fullBounds);
        var fullPath = Path.Combine(debugDirectory, $"seat_{seat.Seat}_full.png");
        File.WriteAllBytes(fullPath, fullCrop.ImageBytes);

        var nameArtifact = SaveFieldArtifacts(debugDirectory, seat.Seat, "name", seat.NameRoi, parsedName, nameRejection, nameRead);
        var stackArtifact = SaveFieldArtifacts(debugDirectory, seat.Seat, "stack", seat.StackRoi, parsedStack, stackRejection, stackRead);
        var betArtifact = SaveFieldArtifacts(debugDirectory, seat.Seat, "bet", seat.BetRoi, parsedBet, betRejection, betRead);

        return new SeatDebugArtifact
        {
            SeatNumber = seat.Seat,
            SeatFullRect = fullBounds,
            SeatFullImagePath = fullPath,
            Fields = [nameArtifact, stackArtifact, betArtifact]
        };
    }

    private static SeatFieldOcrDebugResult SaveFieldArtifacts(string debugDirectory, int seat, string fieldType, Rectangle roi, string parsedValue, string parseRejectionReason, SeatRoiReadResult fieldRead)
    {
        var rawPath = Path.Combine(debugDirectory, $"seat_{seat}_{fieldType}_raw.png");
        File.WriteAllBytes(rawPath, fieldRead.Raw.ImageBytes);

        var variantArtifacts = new List<SeatOcrVariantDebugArtifact>(fieldRead.Attempts.Count);
        foreach (var attempt in fieldRead.Attempts)
        {
            var variantPath = Path.Combine(debugDirectory, $"seat_{seat}_{fieldType}_{attempt.VariantName}_ocr_input.png");
            File.WriteAllBytes(variantPath, attempt.ImageBytes);
            variantArtifacts.Add(new SeatOcrVariantDebugArtifact
            {
                SeatNumber = seat,
                FieldType = fieldType,
                RawRoiRect = roi,
                VariantName = attempt.VariantName,
                OcrInputImagePath = variantPath,
                OcrBackend = attempt.OcrResult.Backend,
                OcrRawText = attempt.OcrResult.Text,
                Confidence = attempt.OcrResult.Confidence,
                Selected = attempt.Selected,
                ParsedValue = parsedValue,
                RejectionReason = attempt.RejectionReason
            });
        }

        var selected = variantArtifacts.FirstOrDefault(item => item.Selected) ?? variantArtifacts.FirstOrDefault();
        return new SeatFieldOcrDebugResult
        {
            SeatNumber = seat,
            FieldType = fieldType,
            RawRoiRect = roi,
            RawRoiImagePath = rawPath,
            SelectedVariantName = selected?.VariantName ?? "raw",
            SelectedOcrInputImagePath = selected?.OcrInputImagePath ?? rawPath,
            ParsedValue = parsedValue,
            ParseRejectionReason = parseRejectionReason,
            Variants = variantArtifacts
        };
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

    private static bool ContainsTimeBankText(string? text)
        => !string.IsNullOrWhiteSpace(text) && text.Contains("Time Bank", StringComparison.OrdinalIgnoreCase);
    private static string FormatConfidence(double? confidence) => confidence.HasValue ? confidence.Value.ToString("0.000") : "n/a";
    private static string FormatVariantDiagnostics(IReadOnlyList<SeatOcrAttemptSummary> attempts)
        => string.Join(", ", attempts.Select(attempt => $"{attempt.VariantName}[selected={attempt.Selected},backend={attempt.OcrResult.Backend},conf={FormatConfidence(attempt.OcrResult.Confidence)},raw='{Sanitize(attempt.OcrResult.Text)}',reject='{attempt.RejectionReason ?? string.Empty}']"));

    private static double ScoreAttempt(string roiType, OcrResult result)
    {
        var text = Sanitize(result.Text);
        if (text.Length == 0)
        {
            return -1;
        }

        var confidenceBonus = (result.Confidence ?? 0d) * 5d;
        return roiType switch
        {
            "name" => text.Any(char.IsLetter) ? 5d + confidenceBonus + text.Length : 0.5d + confidenceBonus,
            _ => text.Any(char.IsDigit) ? 4d + confidenceBonus + text.Count(char.IsDigit) : 0.5d + confidenceBonus
        };
    }

    private static string BuildAttemptRejectionReason(string roiType, SeatOcrAttempt attempt, SeatOcrAttempt selected)
    {
        var attemptScore = ScoreAttempt(roiType, attempt.OcrResult);
        var selectedScore = ScoreAttempt(roiType, selected.OcrResult);
        return $"score={attemptScore:0.000} below selected={selectedScore:0.000}";
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

        return (await _ocrEngine.ReadAsync(heroCardRegionImage, new OcrRequest("hero_cards", "raw"), cancellationToken).ConfigureAwait(false)).Text;
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

    private sealed record SeatRoiReadResult(CapturedImage Raw, string VariantUsed, OcrResult OcrResult, string OcrText, IReadOnlyList<SeatOcrAttemptSummary> Attempts);
    private sealed record SeatOcrAttempt(SeatLocalOcrVariantImage Variant, OcrResult OcrResult);
    private sealed record SeatOcrAttemptSummary(string VariantName, byte[] ImageBytes, OcrResult OcrResult, bool Selected, string? RejectionReason);

    private sealed record SeatLocalExtractionResult(IReadOnlyList<SnapshotPlayer> Players, string Diagnostics, IReadOnlyList<SeatDebugArtifact> DebugArtifacts);
}
