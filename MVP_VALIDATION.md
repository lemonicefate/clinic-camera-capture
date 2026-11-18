# MVP v0.6.3 diagnostics and validation — 2026-10-28

## v0.6.3 JPG default and clinic field-test updates

**Code change:** New installs and missing/invalid Format settings default to `jpg`; existing saved `png`/`bmp` preferences remain explicit and are not overwritten. JPG is lossy. Synthetic smoke tests cover JPEG SOI/EOI signature, decoding with original dimensions, on-disk SHA-256, and cross-format JPG→PNG patient sequence. PNG/BMP retain exact RGB round-trip checks. Existing date folders, 01–99 numbering, and no-overwrite behavior are preserved.

**User-reported clinic acceptance (2026-10-28, not rerun by this agent):** actual PD011M1 read-only lookup compatibility **PASS**; F9 photo shortcut **PASS**; user-selected storage path **PASS**. No patient identifiers, production DBF, actual photos or clinical paths were accessed or recorded here. Exact tested path kind (local vs. UNC), focus variations, concurrent HIS DBF changes and long-term stability were not specified and remain outside this reported evidence.

**Still pending:** v0.6.3 JPEG capture on the physical USB card, USB camera A→B→A switching and recovery on real hardware, MP4 playback, special UNC network permissions, and broader UI/backup acceptance. Historical notes below reflect what was or was not verified at the time of those earlier releases; the user-reported passes above supersede their stale unverified status for the three named scenarios.

## v0.6.2 two USB cameras: select second camera while previewing first

**Reproduction boundary:** Development host has no connected PnP Camera-class devices. The clinic Windows 10 host with two USB cameras is not available as a connected node. Therefore we did not claim physical dual-camera reproduction and never touched HIS or camera settings on the production computer.

**Diagnosing-bugs Phase 1 red command (actual source-state contract):**
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\CameraSwitch.Regression.ps1`

Against original v0.6.1 code, the command exited **1** and printed:

```text
FAIL: Selector disabled whenever a camera is connected
FAIL: Selection change does not request any active camera switch
```

The focused contract script inspects exactly the two UI event paths responsible for this symptom; this is **code-level reproduction**, not real USB input/output.

**Ranked hypotheses & falsifiable predictions after red:** (1) `UpdateButtons` explicitly disables `CameraPicker` whenever `_camera != null` (confirmed from the assignment); (2) `CameraPicker_SelectionChanged` only saves preferences and never invokes `StartCameraAsync`/reinitialization (confirmed from handler); (3) even if selection is enabled, `MediaCapture` exclusive ownership may prevent switching unless old preview/capture is disposed (existing `CloseCamera` detaches preview and disposes, **not yet verified against actual hardware**); (4) ambiguous enumeration indices could address the wrong camera (unlikely because IDs are used, but field confirmation pending).

**Fix:** Keep selection enabled whenever not busy/recording; on a genuine user selection change while a camera is connected, compare IDs and call `SwitchCameraAsync`. Reuse serialized `StartCameraAsync` to release the old preview/camera then initialize new ID, updating persisted CameraId only on success. If initialization fails, restore the old selection and attempt to restart the previous camera. Do not switch mid-recording or camera operation. Preserve the existing media capture and photo/DBF boundaries.

**After fix:** the same source-state red command returns **PASS**. Debug build, synthetic capture/DBF test suite, and self-contained desktop launch should be checked independently. **Full hardware acceptance still requires USB A → B → A and a failed-switch recovery test on the clinic PC.** Use non-patient synthetic objects and fake patient references.

## Historical diagnostic notes


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
- Historical v0.4 rejected JPEG; v0.6.3 now supports `.jpg` (not the `.jpeg` alias). BMP BI_RGB uncompressed header is still validated.
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
- The earlier automated F9 foreground-input probe found no reliable WinUI UI Automation feedback via the outer window; subsequently, **the user reported successful clinic F9 testing on 2026-10-28**. Other focus combinations have not been separately confirmed by this agent.
- The original user-reported v0.1 preview+photo worked on Windows 10 with USB2.0 PC CAMERA, 640×480; this remains the only confirmed physical-card evidence.
- v0.5 adds **optional read-only PD011M1.dbf patient lookup**. Synthetic VFP DBF with CP950 Chinese names verified: NUM forward lookup (including padding), ID reverse lookup (case-insensitive), empty ID displayed as 0000+NUM, deleted rows excluded, and non-exclusive reading while a simulated writer holds the file. No CDX or real HIS file was accessed. Physical HIS DBF format compatibility is **not yet verified**. Windows x64 testing requires a new self-contained Release build; not a clinical release.

## UI and camera **not yet conclusively tested**

- The actual **shortcut problem** happens on the clinic capture workstation, not reproducibly on the no-physical-camera development PC. Framework-based `KeyboardAccelerator` replaces the weak root `KeyDown` binding, but exact on-device F8/F9/F10/F12 use with focused patient field **needs field verification**. Do not describe this as fully proven fixed until tested.
- WinUI FileOpenPicker for PD011M1, both LostFocus query flows, gallery left/right arrow keys, folder picker, and actual camera/recording still need interactive physical validation. No real patient details should be included in bug screenshots or logs. WinUI FolderPicker, path selection UI (especially UNC permissions and WinRT video APIs), gallery prev/next buttons, and top-right close while full-screen need physical interactive field verification. Explicitly selecting a folder under HIS does not register a corresponding HIS photo index.
- USB capture-card MP4 recording, abrupt disconnection, backup, permissions, disk exhaustion and shared drive behavior remain untested.
- The v0.5 app allows a user to configure **PD011M1.dbf for read-only NUM/ID/NAME lookup**, using FileAccess.Read, FileShare.ReadWrite|Delete and a transient in-memory index. It does **not** read/write CDX/FPT or CH013M1. No real patient data were read during automated implementation/testing. **The user reported actual clinic PD011M1 compatibility PASS on 2026-10-28.** Concurrent mutation timing, ambiguous identifiers and long-term stability remain outside the provided evidence.
- v0.6: CaptureStore uses <root>/<six-digit-number>_<yyyyMMdd>/, based on Windows local shutter date (or recording start date). Automated synthetic-image tests verify same date & MRN folder reuse across simulated app instances, 01–99 numbering, next-day folder rollover and independent _01 sequence, other-patient separation, and preservation of previous day's images. Prior plain patient-number subfolders and root-level files are **not migrated**.
- Per-patient numbering is **per patient+date directory**, up to 99; the program refuses after full and does not delete or overwrite. Clinic camera and MP4 storage tests on the device still pending.
