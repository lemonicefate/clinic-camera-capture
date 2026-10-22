# Agent rules: Clinic Camera Capture MVP

- This repository contains an isolated non-clinical camera proof-of-concept, NOT the deployed HIS capture application.
- Never read, write, delete, migrate, reindex, pack, update, or lock production `D:\WM003\` or its DBF/CDX/FPT/IMAGES files as part of implementation or testing. The app may allow a user-chosen path anywhere, but agents and automated tests must never exercise that capability against production HIS; photo file storage is not HIS indexing.
- Use only generated synthetic frames and local disposable test directories in automated tests.
- Do not ingest, copy, upload, log, or commit patient data or actual patient photos.
- The capture API must remain Windows MediaCapture/Media Foundation native; legacy VideoCapX OCX cannot be a required component.
- Photo saves must not write JPEG by default. PNG must round-trip without RGB pixel changes; BMP must be uncompressed BI_RGB.
- Never report successful save until the output file and RGB pixels have been validated. A failed capture must not create a success record.
- Keep camera selection based on device identity rather than unstable list index. Release camera handles on errors/disconnection.
- No HIS integration until separately authorized and validated with an isolated non-production DBF mirror.
- Before deploying to a live capture workstation, complete hardware validation with synthetic objects, permission failure tests, restart behavior, and Windows Camera comparison.
- Build: `D:\tools\dotnet10\dotnet.exe build ClinicCameraCapture.csproj -c Debug -p:Platform=x64`.
- Tests: `D:\tools\dotnet10\dotnet.exe run --project tests\CaptureStore.Smoke\CaptureStore.Smoke.csproj -c Debug`.
