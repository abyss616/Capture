#!/usr/bin/env python3
"""
Local PaddleOCR worker used by ScreenshotScraper.
Stdio mode receives one JSON request per line and returns one JSON response per line.

Protocol contract:
- stdout: JSON responses only (exactly one line per request)
- stderr: diagnostics, tracebacks, and runtime logs
"""

import argparse
import base64
import json
import sys
import time
import traceback
from io import BytesIO

from PIL import Image
from paddleocr import PaddleOCR


def log_stderr(message: str) -> None:
    sys.stderr.write(message + "\n")
    sys.stderr.flush()


def build_engine(lang: str) -> PaddleOCR:
    return PaddleOCR(
        use_doc_orientation_classify=False,
        use_doc_unwarping=False,
        use_textline_orientation=False,
        lang=lang,
    )


def recognize(engine: PaddleOCR, image_bytes: bytes) -> dict:
    image = Image.open(BytesIO(image_bytes)).convert("RGB")

    # PaddleOCR 3.x docs show predict(...) as the main API.
    results = engine.predict(image)

    lines = []
    texts = []
    confidences = []

    for res in results or []:
        data = res.get("res", {}) if isinstance(res, dict) else {}
        rec_texts = data.get("rec_texts", []) or []
        rec_scores = data.get("rec_scores", []) or []

        for i, text in enumerate(rec_texts):
            clean = (text or "").strip()
            if not clean:
                continue

            conf = None
            if i < len(rec_scores):
                try:
                    conf = float(rec_scores[i])
                except Exception:
                    conf = None

            texts.append(clean)
            if conf is not None:
                confidences.append(conf)
            lines.append({"text": clean, "confidence": conf})

    confidence = None
    if confidences:
        confidence = sum(confidences) / len(confidences)

    return {
        "text": " ".join(texts).strip(),
        "confidence": confidence,
        "lines": lines,
    }


def handle_request(engine: PaddleOCR, request_line: str) -> dict:
    started = time.perf_counter()
    payload = json.loads(request_line)

    image_base64 = payload.get("image_base64", "")
    if not image_base64:
        return {
            "ok": True,
            "text": "",
            "confidence": None,
            "lines": [],
            "elapsed_ms": 0,
        }

    image_bytes = base64.b64decode(image_base64)
    data = recognize(engine, image_bytes)
    data["ok"] = True
    data["elapsed_ms"] = int((time.perf_counter() - started) * 1000)
    return data


def run_stdio(engine: PaddleOCR) -> None:
    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            response = handle_request(engine, line)
        except Exception as exc:
            log_stderr(f"request failure: {exc}")
            traceback.print_exc(file=sys.stderr)
            response = {"ok": False, "error": str(exc)}

        sys.stdout.write(json.dumps(response, ensure_ascii=False) + "\n")
        sys.stdout.flush()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--stdio", action="store_true", help="Run line-delimited JSON over stdin/stdout")
    parser.add_argument("--lang", default="en", help="PaddleOCR language model tag")
    args = parser.parse_args()

    log_stderr(f"paddle_ocr_worker starting (lang={args.lang}, stdio={args.stdio})")
    engine = build_engine(args.lang)
    log_stderr("paddle_ocr_worker initialized successfully")

    if args.stdio:
        run_stdio(engine)
    else:
        parser.error("Only --stdio mode is currently supported.")


if __name__ == "__main__":
    main()