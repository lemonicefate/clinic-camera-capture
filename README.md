# 診所原生影像擷取 — WinUI 3 MVP v0.3

**語言：C# + XAML；框架：WinUI 3 / .NET 10；相機：Windows MediaCapture。**

本版是診間擷取卡的**非臨床測試版**。禁止以真實病患測試；不讀寫 `D:\WM003`、`PD011M1`、`CH013M1` 或 HIS 正式影像資料夾。

## 相機式 UI（v0.3）

- 啟動時全螢幕覆蓋**實際顯示器解析度**（1920×1080 顯示器即滿版 1920×1080），而不是變更 Windows 的顯示器解析度。
- 即時相機預覽使用 `Stretch=Uniform`，保留來源完整的 4:3 畫面；16:9 螢幕兩側可能有黑邊，不會裁切內視鏡畫面或憑空增加像素。
- 左上角齒輪開啟設定抽屜：相機切換、連線/停止、拍照/錄影模式、PNG/BMP 格式、拍照快捷鍵、啟動全螢幕、診斷支援格式。
- 上方六位數病歷號輸入框：必須是 6 個 ASCII 數字，可保留前導零；若未輸入完整則主按鈕停用。此值**不會記錄到設定檔或 HIS**，僅供本機測試檔案命名。
- 右側只有一個主要按鈕：拍照模式直接擷取無損 PNG/BMP；錄影模式開始/停止 MP4（僅影像，刻意不錄麥克風）。
- 拍照快捷鍵：可在設定選擇 F8 / F9 / F10 / F12，**僅限應用程式在前景時**。即使目前切到錄影模式，快捷鍵仍指定為拍照；錄影中不可拍照。
- F11 切換全螢幕；Esc 關閉大圖/設定或離開全螢幕；Alt+F4 可關閉程式。
- 右下角為上一次成功拍攝的**照片**縮圖，點擊開啟遮罩全畫面預覽；錄影不會覆蓋照片縮圖。
- 設定用 `%LOCALAPPDATA%\ClinicCameraCapture-MVP\settings.json` 保留，不存病歷號。

## 啟動 / 編譯

開發機：直接雙擊 `D:\Dev\clinic-camera-capture\Start-MVP.cmd`。

```powershell
$env:DOTNET_ROOT = 'D:\tools\dotnet10'
& 'D:\tools\dotnet10\dotnet.exe' build 'D:\Dev\clinic-camera-capture\ClinicCameraCapture.csproj' -c Debug -p:Platform=x64
& 'D:\tools\dotnet10\dotnet.exe' run --project 'D:\Dev\clinic-camera-capture\tests\CaptureStore.Smoke\CaptureStore.Smoke.csproj' -c Debug
```

診間 Windows 10 電腦**不用裝 Visual Studio**：使用另一份 self-contained 測試資料夾，需複製完整發佈資料夾；不能只複製 EXE。

## 本機存檔位置與資料限制

檔案僅儲存於：`%LOCALAPPDATA%\ClinicCameraCapture-MVP\captures\`

照片名稱：`六位病歷號_yyyyMMdd_HHmmss_fff_GUID.png` 或 `.bmp`。
錄影名稱：`六位病歷號_yyyyMMdd_HHmmss_fff_GUID.mp4`。

照片經無損編碼後，檢查解碼 RGB 像素一致、磁碟寫入內容與 SHA256，再用同磁碟 rename 提交；不覆寫現有照片。PNG 會無損壓縮，BMP BI_RGB 不壓縮像素。**來源只有 640×480 時，放大至全螢幕仍只包含 640×480 的細節**。如 UVC 來源先經 MJPEG 壓縮，無損存檔不能復原先前資訊。

錄影使用 Windows MP4（有損視訊壓縮），不錄麥克風；`StartRecordToStorageFileAsync` / `StopRecordAsync` 已編譯，但診間 USB 擷取卡是否支援實際錄影需現場驗證。錄影產生失敗或停止時若有部分檔案，應人工檢查，不視為成功錄影。本版不做影片回放。

## 測試檢查

在非看診時段，用**非病患測試物**：

1. 連接 `USB2.0 PC CAMERA`（預計為 VID_18EC&PID_5850），確認自動預覽、全畫面 4:3、設定開閉。
2. 輸入假病歷號 `000123`，試 F9 快捷鍵、右側拍照按鈕、PNG/BMP 存檔與右下縮圖放大。
3. 設定中選 F10，再驗證快捷鍵變更後會記住；F11 / Esc 驗證視窗大小切換。
4. 以非病患測試物錄製短 MP4 並停止；核對檔案大小、能否用本機播放器打開；若失敗記下畫面錯誤。
5. 關閉重開確認設定持續；拔插 USB、裝置忙碌等需待非看診時間測試。
6. 切換病歷號後再拍攝，確認檔名前綴變更，不與前張覆寫。

## 安全與下一輪

**這不是已核可的臨床影像管理系統**。病歷號出現在實際檔名時就屬於識別資訊，因此目前只能以假號碼／無病患影像測試。等到檔案權限、存檔保護、正式 HIS 查看 PNG/BMP 與 CH013M1 索引相容性等完成前，不應拿正式病患影像替換原系統。

完整驗證狀態在 `MVP_VALIDATION.md`。

## 專案規格及設計文件

- [SPEC.md](SPEC.md)：v0.3 功能需求、驗收條件與未完成的臨床整合。
- [ARCHITECTURE.md](ARCHITECTURE.md)：C#/WinUI3/MediaCapture 及影像儲存架構。
- [DESIGN.md](DESIGN.md)：相機式全螢幕 UI、操作／設定／快捷鍵規格。
- [MVP_VALIDATION.md](MVP_VALIDATION.md)：哪些項目已測試，哪些仍待診間實測。

> 原使用者指定拼法 `ARCHETECTURE.md` 亦保留為導向 [ARCHITECTURE.md](ARCHITECTURE.md) 的索引。
