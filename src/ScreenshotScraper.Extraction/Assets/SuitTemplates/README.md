# Suit templates

- `raw/`: source glyph captures from the poker client (`s`, `c`, `h`, `d`). This repo intentionally keeps `raw/` empty except for `.gitkeep` so binary files do not block PR flow.
- `normalized/`: runtime-ready 64x64 templates generated using the same preprocessing pipeline as live suit ROIs.

## Refresh workflow
1. Add `raw/s.*`, `raw/c.*`, `raw/h.*`, `raw/d.*` with your captured suit glyph images.
2. Run the extractor once (or any flow that invokes `OcrHeroCardExtractor`).
3. The extractor regenerates `normalized/*.png` automatically and writes debug artifacts under:
   - `%TEMP%/ScreenshotScraper/debug/<timestamp>/suit_template_generation/`

Generated debug artifacts include `*_raw.png`, `*_trimmed.png`, and `*_normalized.png` per suit.
