# Clinic Camera Capture — SPEC.md

**版本：** MVP v0.3，2026-10-22

**平台：** Windows 10 x64（診間為 Build 19045）、Windows 11

**技術：** C#、XAML、WinUI 3、.NET 10、Windows MediaCapture

**產品狀態：** 非臨床測試程式；不可直接取代 HIS 正式影像保存流程。

## 1. 問題與目標

舊的 Visual FoxPro／VideoCapX 擷取工具會發生相機辨識不穩定，且使用 JPEG 有損儲存。新工具以 Windows 原生攝影架構直接操作 UVC 攝影機／USB 擷取卡，提供大畫面預覽、無損照片、影片、快捷鍵與簡化介面。保留影像像素完整性與失敗可見性，**目前與正式 HIS 完全隔離**。

已觀察的診間硬體：

- `USB2.0 PC CAMERA`：VID_18EC&PID_5850（可能為 USB 影像擷取裝置）。
- `HD Camera`：VID_1BCF&PID_2087。
- Microsoft `usbvideo.inf` 驅動正常；診間曾測試 v0.1，預覽／拍照成功，使用者回報最大解析度 640×480。
- **不代表 v0.3 的錄影、UI、快捷鍵或相機重連已通過實機驗證。**

## 2. MVP v0.3 功能需求

### FR-01 設備探索、預覽

以 `DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)` 取得設備，依 DeviceId 記憶相機選擇，未設定時優先選 `USB2.0 PC CAMERA`。使用 `MediaCapture`＋`MediaFrameSource` 預覽、可選擇驅動宣告的 Subtype／FPS／解析度；連線錯誤時提示並可重試。停止時釋放相機。

### FR-02 全螢幕介面

預設可全螢幕顯示，大小以螢幕實際顯示解析度為準（例如 1920×1080）。640×480 來源以 `Stretch=Uniform` 放大，保留完整 4:3 內容；16:9 螢幕允許兩側黑邊，**不提升原始畫質、不拉伸或裁切**。F11 切換、Esc 關閉大圖／設定或退出全螢幕。

### FR-03 病歷參考號輸入

上方保留六位 **ASCII 數字**輸入框；範例假號碼 `000123`，保留前導零。未輸入正確時，禁止開始拍照／錄影。錄影時禁止修改。本版病歷號**只用於本機測試檔名前綴**，不查詢、不驗證 HIS 患者、不寫入設定檔；任何真人病歷號不可用來測試。

### FR-04 無損照片保存

`MediaCapture.PrepareLowLagPhotoCaptureAsync` 取出未壓縮 BGRA8 軟體影格：

- PNG：無損壓縮；BMP：BI_RGB 未壓縮；**不會偷偷轉成 JPEG**。
- 解碼比對 RGB 像素／尺寸後，先寫同目錄唯一 `.tmp`，讀回驗證 bytes，最後 rename 到唯一正式檔名，不覆寫既存檔。
- 檔名：`{sixDigitRef}_yyyyMMdd_HHmmss_fff_{random8}.png|bmp`。
- 成功必須以檔案實際保存為準；如果縮圖讀取失敗，要顯示警告但仍保留圖片。

### FR-05 MP4 錄影

設定抽屜選「錄影」，右側唯一主按鈕開始／停止；REC 指示燈。使用 `MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto)` 和 `StartRecordToStorageFileAsync`／`StopRecordAsync`。**MP4 視訊為有損壓縮**；將 `Audio=null`，避免錄到診間對話。完整播放、裝置相容性、非正常停止恢復**尚待診間實測**。

### FR-06 可設定拍照快捷鍵

可選 F8／F9／F10／F12，預設 F9；**僅在程式前景**、沒有設定／照片遮罩且非錄影中啟用。錄影模式但未錄影時，快捷鍵仍指定為「拍照」。不能冒充 Windows 全域熱鍵。

### FR-07 照片預覽

右下角保留本次成功儲存的最後一張**照片**縮圖，點擊顯示全螢幕大圖；Esc 或「返回相機」關閉。本版**不提供**全部照片列表、刪除、修改或影片播放器。

### FR-08 設定

左上角齒輪進入設定抽屜：選擇相機、重新搜尋、開始／停止、照片格式、操作模式、快捷鍵、全螢幕、裝置支援格式診斷和開啟本機測試目錄。設定 JSON 僅存相機 DeviceId、Format、Shortcut、Mode、FullScreen；不存病歷號或影像。

## 3. 安全／資料界線

- 測試圖片／影片只存 `%LOCALAPPDATA%\ClinicCameraCapture-MVP\captures\`。
- 偏好設定只存 `%LOCALAPPDATA%\ClinicCameraCapture-MVP\settings.json`。
- **不得讀寫** `D:\WM003`、`PD011M1`、`CH013M1`、DBF/CDX/FPT/MEM 或 HIS 正式圖片目錄。
- **不得以真正病患／真病歷號**做 MVP 測試；只使用假號碼與非病患測試物。
- GitHub 公開 repo 不得放患者資料、圖片／影片、擷取卡現場影像、憑證或 build 結果。
- 正式整合前需先有權限、備份、稽核、影像儲存權責、HIS 圖片相容性和 DBF/CDX 一致性方案；在隔離測試複本驗證，禁止用正式 HIS 直接測試寫入。

## 4. 驗收狀態（區分證據）

| 驗證項目 | 現況 |
|---|---|
| v0.3 C#／XAML 編譯、開發機視窗啟動 | 已通過 |
| PNG／BMP 合成影格 RGB round-trip／磁碟驗證 | 已通過 |
| 儲存失敗、拒絕 JPEG、六位數命名／防覆寫 | 已通過 |
| 診間 v0.1 預覽及拍照 640×480 | 使用者回報成功 |
| **診間 v0.3** 全螢幕、熱鍵、縮圖 | **待測** |
| 診間 640×480 裝置錄 MP4、正常停止與播放器讀取 | **待測** |
| 真實 HIS 查看 PNG/BMP 及 CH013M1 整合 | **未實作／未授權** |
| 正式臨床部署與資安／資料保存驗收 | **未完成** |

## 5. 非功能性需求

優先保證資料完整性而非速度；相機錯誤應可恢復且不能影響 HIS。全螢幕縮放應保留 4:3 比例。以 Windows 原生相機堆疊運作，不依賴 VideoCapX；應使用可重複的合成影格測試。不得聲稱 PNG 能恢復來源已被 MJPEG 或類比訊號鏈損失的資訊。

## 6. 下一階段（不屬於已完成 MVP）

1. 診間實測 v0.3 錄影、重啟／拔插、鍵盤焦點、縮圖及全螢幕。
2. 可靠的 MP4 解碼驗證、停止／意外中斷復原、儲存空間不足處理。
3. 如需正式患者資料，設計受限權限、病歷識別確認、影像生命週期與正式儲存稽核。
4. 在隔離 DBF 鏡像中確認 HIS 是否支援 PNG/BMP；如只支援 JPG，定義無損原檔與衍生 JPG 的對照策略，以及錯誤後補索引機制。

**範圍界線：** 本專案 v0.3 只是擷取 UI／本機測試影像 MVP，尚非可上線的臨床影像管理系統。
