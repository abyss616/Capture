# OCR runtime requirements

ScreenshotScraper uses **PaddleOCR** behind `IOcrEngine` for seat-local name/stack/bet recognition.

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

### 4) Configure ScreenshotScraper backend

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

- raw and preprocessed seat ROI PNGs
- `seat_ocr_summary.txt`
- Paddle worker JSON responses (for debug/troubleshooting)

Each seat OCR diagnostic line includes backend, ROI type, variant used (`raw` / `preprocessed`), raw text, confidence, and elapsed time.

## Manual validation checklist

1. Load the known problematic screenshot in the WPF app.
2. Confirm Paddle environment variables are set (`PADDLE_PYTHON`, `PADDLE_WORKER_SCRIPT`, etc.).
3. Generate XML.
4. Open `debug/output/<timestamp>/seat_ocr_summary.txt`.
5. Verify visible seat-name ROI text (e.g., `jkl102`) is non-empty in raw OCR output and parsed output.
6. Confirm dealer detection/seat ordering remains unchanged.
