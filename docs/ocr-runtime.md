# OCR runtime requirements

The production WPF application now uses the built-in **Windows OCR** runtime (`Windows.Media.Ocr`) through `WindowsOcrEngine`.

## Clean machine setup

1. Run the application on **Windows 10 or Windows 11**.
2. Install at least one OCR-capable language pack:
   * Open **Settings**.
   * Go to **Time & language** → **Language & region**.
   * Add or edit a language and make sure its **OCR** feature is installed.
3. Restart the application after the language pack finishes installing.

## What happens when OCR is not ready

If the Windows OCR recognizer cannot be created, the application surfaces a clear runtime error instead of returning placeholder text. Typical causes are:

* running a non-Windows build
* missing Windows OCR language packs
* invalid or empty screenshot bytes

## Notes

* OCR output is still parsed by the existing poker-specific extraction pipeline.
* Accuracy depends on screenshot readability, table theme, font rendering, and overlap from animations or avatars.
