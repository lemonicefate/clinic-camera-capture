# Clinic Camera Capture — v0.3 validation (2026-10-22)

## Confirmed / Automated
- Development workstation SDK: .NET 10; WinUI 3, C# + XAML.
- WinUI camera-style page and C# handlers compiled on development PC with 0 errors and 0 warnings.
- Process launched on development PC and was observed running, responding, and holding a native app window.
- Synthetic 80x48 RGBA/BGRA8 PNG/BMP round-trip pixel integrity passes; BMP BI_RGB is uncompressed, checksum verified, JPEG rejected.
- Failure-injection output folder test: failed save does not overwrite previous photo.
- New six-digit local reference tests: fake 000123 preserves leading zeros, nonnumeric/path traversal rejected, successive saves generate collision-safe distinct files.
- No WM003 source files or HIS records were modified by these tests.

## User-Reported Earlier Hardware Success (v0.1)
- On clinic imaging workstation, Windows 10 build 19045, webcam and capture devices are enumerated:
  - USB2.0 PC CAMERA (VID_18EC&PID_5850)
  - HD Camera (VID_1BCF&PID_2087)
- User verified original v0.1 Windows MediaCapture preview and photo capture works, at 640x480. This does not validate v0.3 video recording.

## v0.3 Changes
- Full-screen presentation on actual monitor size; preserves source 4:3 with Stretch=Uniform (black side margins on 16:9).
- Six-digit patient-reference entry; no implicit HIS lookup or indexing, used only for a local test filename.
- Single right-hand shutter/record button with mode selected in left settings drawer.
- F8/F9/F10/F12 configurable **foreground** still photo shortcut, default F9.
- Local JSON settings (camera ID, default mode, format, hotkey, full screen) without patient reference.
- Thumbnail and full-canvas view of most recent successfully saved still photograph.
- MP4 record control wired via MediaCapture, with microphone disabled.

## Still NOT Verified / NOT Clinical Release
- No physical camera on development PC; all v0.3 hardware behavior pending clinic field test.
- MP4 StartRecordToStorageFileAsync on VID_18EC&PID_5850, preview+record simultaneity, MP4 playback, reconnect, data-loss and storage access tests pending.
- Actual Windows 10 v0.3 UI interaction, fullscreen display and keyboard shortcuts pending clinic PC field test.
- No video preview or player inside app, only photo thumbnail; no global hotkey.
- No PD011M1/CH013M1 access or HIS images integration; no real clinical images may be used in this isolated MVP.
- No production HIPAA / local personal-data confidentiality controls are established.
