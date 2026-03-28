import argparse
import base64
import json
import os
import sys
import time
from io import BytesIO
from typing import Any

os.environ["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"

import numpy as np
from PIL import Image
from paddleocr import PaddleOCR


def eprint(*args: Any) -> None:
    print(*args, file=sys.stderr, flush=True)


def json_dumps_line(obj: dict[str, Any]) -> str:
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def ok_result(text: str, confidence: float | None, lines: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "ok": True,
        "text": text,
        "confidence": confidence,
        "lines": lines,
    }


def error_result(message: str) -> dict[str, Any]:
    return {
        "ok": False,
        "error": message,
        "text": "",
        "confidence": None,
        "lines": [],
    }


def _result_to_dict(res: Any) -> dict[str, Any]:
    if isinstance(res, dict):
        return res

    json_attr = getattr(res, "json", None)
    if isinstance(json_attr, dict):
        return json_attr
    if isinstance(json_attr, str):
        try:
            return json.loads(json_attr)
        except Exception:
            pass

    res_attr = getattr(res, "res", None)
    if isinstance(res_attr, dict):
        return {"res": res_attr}

    return {}


def _extract_recognition_payload(result_dict: dict[str, Any]) -> dict[str, Any]:
    if isinstance(result_dict.get("res"), dict):
        return result_dict["res"]

    return result_dict


def recognize_bytes(engine: PaddleOCR, image_bytes: bytes) -> dict[str, Any]:
    image = Image.open(BytesIO(image_bytes)).convert("RGB")
    image_np = np.array(image)

    results = engine.predict(image_np)

    lines: list[dict[str, Any]] = []
    texts: list[str] = []
    confidences: list[float] = []

    for res in results or []:
        result_dict = _result_to_dict(res)
        data = _extract_recognition_payload(result_dict)

        rec_texts = data.get("rec_texts", []) or []
        rec_scores = data.get("rec_scores", []) or []
        rec_polys = data.get("rec_polys", []) or []

        if not rec_texts and data.get("rec_text"):
            rec_texts = [data.get("rec_text")]
            rec_scores = [data.get("rec_score")] if data.get("rec_score") is not None else []

        for i, text in enumerate(rec_texts):
            clean = str(text).strip()
            if not clean:
                continue

            conf = None
            if i < len(rec_scores):
                try:
                    conf = float(rec_scores[i])
                    confidences.append(conf)
                except Exception:
                    conf = None

            bbox = None
            if i < len(rec_polys):
                try:
                    poly = rec_polys[i]
                    bbox = poly.tolist() if hasattr(poly, "tolist") else poly
                except Exception:
                    bbox = None

            lines.append(
                {
                    "text": clean,
                    "confidence": conf,
                    "bbox": bbox,
                }
            )
            texts.append(clean)

    avg_confidence = None
    if confidences:
        avg_confidence = sum(confidences) / len(confidences)

    return ok_result(" ".join(texts).strip(), avg_confidence, lines)


def recognize_file(engine: PaddleOCR, image_path: str) -> dict[str, Any]:
    with open(image_path, "rb") as f:
        return recognize_bytes(engine, f.read())


def parse_request(payload: dict[str, Any]) -> bytes:
    if payload.get("imageBase64"):
        try:
            return base64.b64decode(payload["imageBase64"])
        except Exception as ex:
            raise ValueError(f"Invalid imageBase64: {ex}") from ex

    if payload.get("imagePath"):
        image_path = str(payload["imagePath"])
        with open(image_path, "rb") as f:
            return f.read()

    raise ValueError("Request must contain either 'imageBase64' or 'imagePath'.")


def handle_request(engine: PaddleOCR, payload: dict[str, Any]) -> dict[str, Any]:
    image_bytes = parse_request(payload)
    return recognize_bytes(engine, image_bytes)


def make_engine(args: argparse.Namespace) -> PaddleOCR:
    kwargs: dict[str, Any] = {
        "lang": args.lang,
        "use_doc_orientation_classify": args.use_doc_orientation_classify,
        "use_doc_unwarping": args.use_doc_unwarping,
        "use_textline_orientation": args.use_textline_orientation,
        "enable_hpi": args.enable_hpi,
        "use_tensorrt": args.use_tensorrt,
        "precision": args.precision,
    }

    try:
        return PaddleOCR(**kwargs)
    except TypeError as ex:
        unsupported = []
        for key in ["enable_hpi", "use_tensorrt", "precision"]:
            if key in kwargs:
                unsupported.append(key)
                kwargs.pop(key)
        eprint(f"[PaddleOCR] fallback constructor due to unsupported args ({', '.join(unsupported)}): {ex}")
        return PaddleOCR(**kwargs)


def run_single_json_stdin(args: argparse.Namespace) -> int:
    try:
        eprint("[PaddleOCR] worker process start")
        eprint("[PaddleOCR] before PaddleOCR construction")
        engine = make_engine(args)
        eprint("[PaddleOCR] after PaddleOCR construction")

        raw = sys.stdin.read()
        if not raw.strip():
            print(json_dumps_line(error_result("No JSON received on stdin.")), flush=True)
            return 1

        payload = json.loads(raw)
        if not isinstance(payload, dict):
            print(json_dumps_line(error_result("stdin JSON must be an object.")), flush=True)
            return 1

        request_start = time.perf_counter()
        result = handle_request(engine, payload)
        request_elapsed_ms = int((time.perf_counter() - request_start) * 1000)
        eprint(f"[PaddleOCR] request completed ({request_elapsed_ms} ms)")
        print(json_dumps_line(result), flush=True)
        return 0

    except Exception as ex:
        print(json_dumps_line(error_result(str(ex))), flush=True)
        return 1


def run_stdio_loop(args: argparse.Namespace) -> int:
    try:
        eprint("[PaddleOCR] worker process start")
        eprint("[PaddleOCR] before PaddleOCR construction")
        engine = make_engine(args)
        eprint("[PaddleOCR] after PaddleOCR construction")
    except Exception as ex:
        print(json_dumps_line(error_result(f"Failed to initialize PaddleOCR: {ex}")), flush=True)
        return 1

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        request_start = time.perf_counter()
        is_warmup = False

        try:
            payload = json.loads(line)
            if not isinstance(payload, dict):
                eprint("[PaddleOCR] request received")
                response = error_result("Request JSON must be an object.")
            else:
                is_warmup = str(payload.get("roiType", "")).strip().lower() == "warmup"
                if is_warmup:
                    eprint("[PaddleOCR] warmup request start")
                response = handle_request(engine, payload)
        except Exception as ex:
            eprint("[PaddleOCR] request received")
            response = error_result(str(ex))

        request_elapsed_ms = int((time.perf_counter() - request_start) * 1000)
        if is_warmup:
            eprint(f"[PaddleOCR] warmup request end ({request_elapsed_ms} ms)")

        print(json_dumps_line(response), flush=True)

    return 0


def run_image_cli(args: argparse.Namespace) -> int:
    try:
        eprint("[PaddleOCR] worker process start")
        eprint("[PaddleOCR] before PaddleOCR construction")
        engine = make_engine(args)
        eprint("[PaddleOCR] after PaddleOCR construction")
        result = recognize_file(engine, args.image)
        print(json_dumps_line(result), flush=True)
        return 0
    except Exception as ex:
        print(json_dumps_line(error_result(str(ex))), flush=True)
        return 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="PaddleOCR worker")
    parser.add_argument("--image", help="Path to image file")
    parser.add_argument("--stdin-json", action="store_true", help="Read a single JSON request from stdin")
    parser.add_argument("--stdio", action="store_true", help="Run as line-delimited JSON stdio worker")
    parser.add_argument("--lang", default="en", help="OCR language")

    parser.add_argument(
        "--use-doc-orientation-classify",
        action="store_true",
        help="Enable document orientation classification",
    )
    parser.add_argument(
        "--use-doc-unwarping",
        action="store_true",
        help="Enable document unwarping",
    )
    parser.add_argument(
        "--use-textline-orientation",
        action="store_true",
        help="Enable text line orientation classification",
    )
    parser.add_argument("--enable-hpi", action="store_true", help="Enable high-performance inference path when available")
    parser.add_argument("--use-tensorrt", action="store_true", help="Enable TensorRT execution when available")
    parser.add_argument("--precision", default="fp32", help="Inference precision: fp32/fp16")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    if args.stdio:
        return run_stdio_loop(args)

    if args.stdin_json:
        return run_single_json_stdin(args)

    if args.image:
        return run_image_cli(args)

    print(json_dumps_line(error_result("Provide --image, --stdin-json, or --stdio.")), flush=True)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
