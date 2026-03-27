# OCR runtime requirements

ScreenshotScraper uses a single OCR backend behind `IOcrEngine`:

- **PaddleOCR**

PaddleOCR is used for both full-table extraction and tiny poker UI scene-text ROIs.

## PaddleOCR setup (3.x)

### 1) Install Python

Use Python **3.10 - 3.12** on Windows.

### 2) Install dependencies

From repository root:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install paddleocr==3.* paddlepaddle pillow
```

> If `paddlepaddle` GPU/CPU wheel selection differs on your machine, follow Paddle's install guide and then install `paddleocr==3.*`.

### 3) Model download

On first run, PaddleOCR downloads required models to the local Paddle cache directory automatically.

### 4) Configure ScreenshotScraper OCR

The WPF app reads environment variables at startup:

```powershell
$env:PADDLE_PYTHON = ".\.venv\Scripts\python.exe"
$env:PADDLE_WORKER_SCRIPT = "tools\paddle_ocr_worker.py"
$env:PADDLE_LANGUAGE = "en"
$env:PADDLE_TIMEOUT_MS = "15000"
$env:PADDLE_KEEP_WARM = "true"
```

Then run the app normally. Seat-local OCR requests flow through the Python sidecar.

## Diagnostics

Per run, seat-local debug artifacts are persisted under:

`debug/output/<timestamp>/`

Including:

- seat full ROI + raw field ROIs
- exact OCR input PNGs for **every tried variant** (`*_ocr_input.png`)
- `seat_ocr_summary.txt`
- `seat_ocr_debug.json` (structured per-seat/per-field/per-variant metadata)
- Paddle worker JSON responses (for debug/troubleshooting)

Each seat OCR diagnostic line includes seat, field ROI, backend, tried variant names, selected variant, raw OCR text, confidence, parse result, and rejection reason.

### Tuning seat-local preprocessing variants

Seat-local preprocessing is intentionally source-preserving first, with thresholding only as fallback.

Defaults are configured in `SeatLocalOcrPreprocessingSettings`:

- name upscales: `2x`, `3x`, `4x`
- numeric upscales: `2x`, `3x`, `4x`
- source-preserving enhancement: light contrast (`alpha`/`beta`) + mild sharpen
- grayscale-normalized variants
- optional threshold fallback variants

To tune:

1. Update values in `SeatLocalOcrPreprocessingSettings`.
2. Re-run with the same screenshot.
3. Compare `seat_ocr_debug.json` and variant PNGs to confirm which exact OCR-input image won.
4. Prefer variants that preserve white/light text edges and outlines over aggressively binarized variants.

## Manual validation checklist

1. Load the known problematic screenshot in the WPF app.
2. Generate XML.
3. Open `debug/output/<timestamp>/seat_ocr_summary.txt`.
4. Open `debug/output/<timestamp>/seat_ocr_debug.json` and confirm the selected variant image path matches the expected `*_ocr_input.png`.
5. Verify visible seat-name ROI text (e.g., `jkl102`) is non-empty in selected OCR output and parsed output.
5. Confirm dealer detection/seat ordering remains unchanged.
