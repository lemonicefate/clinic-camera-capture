# Clinic Camera Capture — SPEC.md

**版本：** MVP v0.4，2026-10-22

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

## 2. MVP v0.4 功能需求

### FR-01 設備探索、預覽

以 `DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)` 取得設備，依 DeviceId 記憶相機選擇，未設定時優先選 `USB2.0 PC CAMERA`。使用 `MediaCapture`＋`MediaFrameSource` 預覽、可選擇驅動宣告的 Subtype／FPS／解析度；連線錯誤時提示並可重試。停止時釋放相機。

### FR-02 全螢幕介面

預設可全螢幕顯示，大小以螢幕實際解析度為準（例如 1920×1080）；新增右側「× 關閉」按鈕供全螢幕直接結束 App。640×480 來源以 `Stretch=Uniform` 放大，保留 4:3、不裁切或假稱提升畫質。F11 切換、Esc 關閉大圖／設定或退出全螢幕。

### FR-03 病歷參考號輸入

上方可輸入 1～6 位 **ASCII 數字**；保存時左補 0 成六位（`90` → `000090`）。不可包含符號、字母或超過六位。未輸入有效號碼時禁止拍照／錄影，錄影中不可修改。本版號碼**只用於本機測試檔名**，不查詢、不驗證 HIS 病人、不寫入設定檔。

### FR-04 無損照片保存

`MediaCapture.PrepareLowLagPhotoCaptureAsync` 取出未壓縮 BGRA8 軟體影格：

- PNG：無損壓縮；BMP：BI_RGB 未壓縮；**不會偷偷轉成 JPEG**。
- 解碼比對 RGB 像素／尺寸後，先寫同目錄唯一 `.tmp`，讀回驗證 bytes，最後 rename 到唯一正式檔名，不覆寫既存檔。
- 照片檔名：`{sixDigitRef}_{01..99}.png|bmp`，例如 `000090_01.png`、`000090_12.bmp`；在所選資料夾內跨 PNG/BMP 共用流水號，不重用現有序號；01～99 全滿時拒絕存檔且不覆寫。MP4 維持獨立的時間戳檔名。
- 成功必須以檔案實際保存為準；如果縮圖讀取失敗，要顯示警告但仍保留圖片。

### FR-05 MP4 錄影

設定抽屜選「錄影」，右側唯一主按鈕開始／停止；REC 指示燈。使用 `MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto)` 和 `StartRecordToStorageFileAsync`／`StopRecordAsync`。**MP4 視訊為有損壓縮**；將 `Audio=null`，避免錄到診間對話。完整播放、裝置相容性、非正常停止恢復**尚待診間實測**。

### FR-06 可設定拍照快捷鍵

可選 F8／F9／F10／F12，預設 F9；改為 WinUI `KeyboardAccelerator`，避免舊版單靠 Grid `KeyDown` 的焦點路由問題。**僅程式前景**且沒有設定／照片遮罩、非錄影中時使用；錄影模式未開始錄影時亦可拍照。實際診間焦點驗證仍待測。不是全域熱鍵。

### FR-07 照片預覽

右下角顯示目前病歷號本次最後一張成功儲存的照片縮圖；點擊開啟同病歷號**本次 App 執行期間所拍的所有照片**，可用上一張／下一張切換，顯示張數及檔名。切換病歷號後分別檢視，不包含歷史執行批次、影片、刪除或修改。

### FR-08 設定

左上齒輪設定抽屜包含相機、拍照／錄影、格式、快捷鍵、全螢幕、來源格式診斷，及**自訂照片/影片儲存資料夾**（手動路徑與資料夾選取）。選擇後須按「套用」，建立資料夾並測試寫入權限；只能本機完整路徑，禁止正式 WM003、UNC share、junction/symlink。設定 JSON 包含 CameraId、Format、Shortcut、Mode、FullScreen、OutputFolder；不存病歷號。

## 3. 安全／資料界線

- 測試圖片／影片預設 `%LOCALAPPDATA%\ClinicCameraCapture-MVP\captures\`，可改為經 `OutputFolderPolicy` 驗證的本機路徑；不得指向正式 HIS 資料樹。
- 偏好設定只存 `%LOCALAPPDATA%\ClinicCameraCapture-MVP\settings.json`。
- **不得讀寫** `D:\WM003`、`PD011M1`、`CH013M1`、DBF/CDX/FPT/MEM 或 HIS 正式圖片目錄。
- **不得以真正病患／真病歷號**做 MVP 測試；只使用假號碼與非病患測試物。
- GitHub 公開 repo 不得放患者資料、圖片／影片、擷取卡現場影像、憑證或 build 結果。
- 正式整合前需先有權限、備份、稽核、影像儲存權責、HIS 圖片相容性和 DBF/CDX 一致性方案；在隔離測試複本驗證，禁止用正式 HIS 直接測試寫入。

## 4. 驗收狀態（區分證據）

| 驗證項目 | 現況 |
|---|---|
| v0.4 C#／XAML 編譯、合成照片及新命名/批次測試 | 已通過；相機實機需另測 |
| PNG／BMP 合成影格 RGB round-trip／磁碟驗證 | 已通過 |
| 儲存失敗、拒絕 JPEG、六位數命名／防覆寫 | 已通過 |
| 診間 v0.1 預覽及拍照 640×480 | 使用者回報成功 |
| **診間 v0.4** 全螢幕退出、加速鍵、相簿及自訂路徑 | **待實機驗證** |
| 診間 640×480 裝置錄 MP4、正常停止與播放器讀取 | **待測** |
| 真實 HIS 查看 PNG/BMP 及 CH013M1 整合 | **未實作／未授權** |
| 正式臨床部署與資安／資料保存驗收 | **未完成** |

## 5. 非功能性需求

優先保證資料完整性而非速度；相機錯誤應可恢復且不能影響 HIS。全螢幕縮放應保留 4:3 比例。以 Windows 原生相機堆疊運作，不依賴 VideoCapX；應使用可重複的合成影格測試。不得聲稱 PNG 能恢復來源已被 MJPEG 或類比訊號鏈損失的資訊。

## 6. 下一階段（不屬於已完成 MVP）

1. 診間實測 v0.4 錄影、重啟／拔插、輸入框焦點時的 F9、關閉按鈕、照片翻頁及自訂儲存路徑。
2. 可靠的 MP4 解碼驗證、停止／意外中斷復原、儲存空間不足處理。
3. 如需正式患者資料，設計受限權限、病歷識別確認、影像生命週期與正式儲存稽核。
4. 在隔離 DBF 鏡像中確認 HIS 是否支援 PNG/BMP；如只支援 JPG，定義無損原檔與衍生 JPG 的對照策略，以及錯誤後補索引機制。

**範圍界線：** 本專案 v0.4 只是擷取 UI／本機測試影像 MVP，尚非可上線的臨床影像管理系統。
