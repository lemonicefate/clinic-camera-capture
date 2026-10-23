# MVP v0.6.1 diagnostics and validation — 2026-10-23

## v0.6.1 regression: patient fields disappear after successful lookup

Using mattpocock/skills `diagnosing-bugs`, constructed a **real WinUI UI Automation** reproduction (not a mock) against the unmodified v0.6 self-contained executable and a disposable synthetic PD011M1.dbf. Both forward NUM and reverse ID lookup initially returned **ReportedSuccess=True**, but **NumberEmpty=True, IdEmpty=True, NameEmpty=True** with test **FAIL**. This is the exact user-reported symptom.

After creating that failing feedback loop, investigated these falsifiable hypotheses in order: (1) delayed, programmatic `TextChanged` notifications racing a short-lived `_updatingPatientFields` guard; (2) `LostFocus` re-entrancy; (3) asynchronous lookup response ordering. A temporary state-only trace (never patient values) showed the lookup **did fill all fields**, then queued `TextChanged` arrived **after** the guard was lowered, and the two field-clearing handlers cascaded. Diagnosis (1) confirmed. All temporary debug instrumentation was removed before release.

Fix: programmatically assigned TextBox values are recognized via a pending expected-value marker; a fully resolved pair of identifiers is also recognized. True user edits still clear the opposite field and name, without recursively erasing the currently edited value. Enter explicitly commits either input field; LostFocus continues to work. Lookup remains read-only and generation-checked.

### Repeatable regression command (Windows development desktop)

```powershell
$exe = "D:\tools\rea-analysis\image-capture-his\win10-camera-mvp-v061\ClinicCameraCapture.exe"
foreach ($field in @("forward","reverse")) {
    foreach ($trigger in @("focus","enter")) {
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\PatientLookup.UiRepro.ps1 -Exe $exe -Direction $field -Trigger $trigger
        if ($LASTEXITCODE -ne 0) { throw "WinUI lookup regression: $field / $trigger" }
    }
}
```

The fixture contains **only synthetic, non-patient identifiers**, runs no camera capture, and temporarily backs up/restores the user's own local settings file. No real clinic DBF/CDX is accessed. Asserted: lookup succeeds, all three visible fields remain correct, and modifying the input invalidates the old patient while preserving what was typed.

**After fix:** all four forward/reverse × Enter/LostFocus scenarios passed on the development machine. The actual clinic Windows 10 hardware and real DBF interactions still need a separate read-only field acceptance test without exposing patient values to logs or Git.

## Historical diagnostic notes (previous versions)

## Method (mattpocock/skills: diagnosing-bugs)

1. **Reproduction feedback loop:** existing synthetic camera-store test, extended with a failing assertion: `CaptureIdentity.IsValid("90")` returned false. Initial run was **FAIL: accept short numeric patient reference**.
2. **Minimization:** 1 input (`90`) and 1 assertion demonstrated that the previous exact-six-digit validator prevents the user's desired left-padding. Existing tests did not exercise the requirement.
3. **Falsifiable findings:** `CaptureIdentity` imposed exact six digits; `CaptureStore` generated timestamp+GUID names, not sequence; `MainPage` retained only `_lastPhotoPath`; key capture was a bubbling `CameraRoot.KeyDown` handler that could be bypassed by focus handling.
4. **Fix:** normalize 1–6 ASCII digits to 6, assign 01–99 per folder and patient across formats, keep per-patient run-only `PhotoBatch`, use WinUI `KeyboardAccelerator` instead of relying on bubbling `KeyDown` to invoke photo capture. Add close button and user-selected output folder (v0.4.1 removes all hardcoded directory-location exclusions).
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
- v0.4.1 output folder normalization accepts valid local, UNC, drive-root, relative and HIS-looking paths, without performing any file access at those locations. No application-level location denylist remains; OS write access still required.

## Build and execution

- WinUI C# / XAML Debug build on development PC completed with **0 warnings, 0 errors**.
- Direct launching the framework-dependent Debug EXE failed due to unregistered Windows App SDK COM class (Event Viewer: `0x80040154` in `DeploymentInitializeOptions` auto-initializer); **this is a development host runtime dependency**, not evidence that the photo code crashed. Do not use the raw Debug EXE for clinic distribution.
- The self-contained Windows x64 Release publish **was launched on the development PC and remained responsive with a real window handle**.
- An automated F9 foreground-input probe found no reliable WinUI UI Automation feedback via the outer window; **keyboard shortcut trigger is not independently verified**. Only the code binding and successful application startup are verified.
- The original user-reported v0.1 preview+photo worked on Windows 10 with USB2.0 PC CAMERA, 640×480; this remains the only confirmed physical-card evidence.
- v0.5 adds **optional read-only PD011M1.dbf patient lookup**. Synthetic VFP DBF with CP950 Chinese names verified: NUM forward lookup (including padding), ID reverse lookup (case-insensitive), empty ID displayed as 0000+NUM, deleted rows excluded, and non-exclusive reading while a simulated writer holds the file. No CDX or real HIS file was accessed. Physical HIS DBF format compatibility is **not yet verified**. Windows x64 testing requires a new self-contained Release build; not a clinical release.

## UI and camera **not yet conclusively tested**

- The actual **shortcut problem** happens on the clinic capture workstation, not reproducibly on the no-physical-camera development PC. Framework-based `KeyboardAccelerator` replaces the weak root `KeyDown` binding, but exact on-device F8/F9/F10/F12 use with focused patient field **needs field verification**. Do not describe this as fully proven fixed until tested.
- WinUI FileOpenPicker for PD011M1, both LostFocus query flows, gallery left/right arrow keys, folder picker, and actual camera/recording still need interactive physical validation. No real patient details should be included in bug screenshots or logs. WinUI FolderPicker, path selection UI (especially UNC permissions and WinRT video APIs), gallery prev/next buttons, and top-right close while full-screen need physical interactive field verification. Explicitly selecting a folder under HIS does not register a corresponding HIS photo index.
- USB capture-card MP4 recording, abrupt disconnection, backup, permissions, disk exhaustion and shared drive behavior remain untested.
- The v0.5 app allows a user to configure **PD011M1.dbf for read-only NUM/ID/NAME lookup**, using FileAccess.Read, FileShare.ReadWrite|Delete and a transient in-memory index. It does **not** read/write CDX/FPT or CH013M1. No real patient data were read during implementation/testing. Actual DBF field types, CP950 strings, file mutation timing, patient identity ambiguity, and UI focus behavior require clinic validation, initially on an isolated DBF copy.
- v0.6: CaptureStore uses <root>/<six-digit-number>_<yyyyMMdd>/, based on Windows local shutter date (or recording start date). Automated synthetic-image tests verify same date & MRN folder reuse across simulated app instances, 01–99 numbering, next-day folder rollover and independent _01 sequence, other-patient separation, and preservation of previous day's images. Prior plain patient-number subfolders and root-level files are **not migrated**.
- Per-patient numbering is **per patient+date directory**, up to 99; the program refuses after full and does not delete or overwrite. Clinic camera and MP4 storage tests on the device still pending.
