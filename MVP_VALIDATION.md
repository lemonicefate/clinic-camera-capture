# MVP v0.4 diagnostics and validation — 2026-10-22

## Method (mattpocock/skills: diagnosing-bugs)

1. **Reproduction feedback loop:** existing synthetic camera-store test, extended with a failing assertion: `CaptureIdentity.IsValid("90")` returned false. Initial run was **FAIL: accept short numeric patient reference**.
2. **Minimization:** 1 input (`90`) and 1 assertion demonstrated that the previous exact-six-digit validator prevents the user's desired left-padding. Existing tests did not exercise the requirement.
3. **Falsifiable findings:** `CaptureIdentity` imposed exact six digits; `CaptureStore` generated timestamp+GUID names, not sequence; `MainPage` retained only `_lastPhotoPath`; key capture was a bubbling `CameraRoot.KeyDown` handler that could be bypassed by focus handling.
4. **Fix:** normalize 1–6 ASCII digits to 6, assign 01–99 per folder and patient across formats, keep per-patient run-only `PhotoBatch`, use WinUI `KeyboardAccelerator` instead of relying on bubbling `KeyDown` to invoke photo capture. Add close button and validated user-selected local output folder.
5. **Regression:** all synthetic/format/numbering/path/batch tests pass. UI is compiled, not fully automated at real camera level.

## Regressions that have actually been run

- `dotnet run --project tests/CaptureStore.Smoke/CaptureStore.Smoke.csproj -c Debug`
- PNG/BMP RGB round-trip matches source and disk SHA-256 verified.
- JPEG is refused, BMP BI_RGB uncompressed header validated.
- Simulated write failure does not overwrite an existing image.
- Enter `90` or `000090`: standardized to six digits; first PNG is `000090_01.png`, next BMP is `000090_02.bmp`.
- Cross-format occupied number `000090_03.jpg` causes next PNG to use `000090_04.png`.
- All 01–99 taken means a new attempt fails clearly without overwriting `_01`.
- In-run `PhotoBatch` tracks two photographs for `000090` and does not mix in `000091`.
- Output folder validation allows safe local paths and rejects direct `D:\WM003\IMAGES`.

## Build and execution

- WinUI C# / XAML Debug build on development PC completed with **0 warnings, 0 errors**.
- Direct launching the framework-dependent Debug EXE failed due to unregistered Windows App SDK COM class (Event Viewer: `0x80040154` in `DeploymentInitializeOptions` auto-initializer); **this is a development host runtime dependency**, not evidence that the photo code crashed. Do not use the raw Debug EXE for clinic distribution.
- The self-contained Windows x64 Release publish **was launched on the development PC and remained responsive with a real window handle**.
- An automated F9 foreground-input probe found no reliable WinUI UI Automation feedback via the outer window; **keyboard shortcut trigger is not independently verified**. Only the code binding and successful application startup are verified.
- The original user-reported v0.1 preview+photo worked on Windows 10 with USB2.0 PC CAMERA, 640×480; this remains the only confirmed physical-card evidence.
- New v0.4 source will be packaged as Win10 self-contained x64 test build separately. Deployment is not a clinical release.

## UI and camera **not yet conclusively tested**

- The actual **shortcut problem** happens on the clinic capture workstation, not reproducibly on the no-physical-camera development PC. Framework-based `KeyboardAccelerator` replaces the weak root `KeyDown` binding, but exact on-device F8/F9/F10/F12 use with focused patient field **needs field verification**. Do not describe this as fully proven fixed until tested.
- WinUI FolderPicker, path selection UI, gallery prev/next buttons, and top-right close while full-screen need physical interactive field verification.
- USB capture-card MP4 recording, abrupt disconnection, backup, permissions, disk exhaustion and shared drive behavior remain untested.
- The MVP intentionally doesn't open HIS DBF or write `CH013M1`. Patient IDs can be identifying information; use **synthetic IDs only**.
- Per-patient numbering is **per selected storage directory**, up to 99; the program refuses after full and does not delete or overwrite.
