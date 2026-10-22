# Clinic Camera Capture — Windows Native Camera MVP v0.4

**C# + XAML / WinUI 3 / .NET 10 / Windows MediaCapture**

非臨床測試版；原 HIS (WM003/DBF) 完全不連線。**僅用虛構病歷號、非病患測試物；不要上傳任何真人照片或病歷資料至公開 GitHub。**

## 功能

- 640×480 擷取卡來源可放大在 1920×1080 全螢幕顯示（維持 4:3、左右黑邊；畫質不會變成 HD）。
- 右側快門拍照／錄影按鈕；右側上方**「× 關閉」**可直接結束全螢幕 App。F11 切換全螢幕，Esc 退出全螢幕／照片檢視。
- 上方輸入 **1～6 位數字**；儲存時左補零為六位，例如 `90` → `000090`，不得輸入英文字或符號。
- PNG（無損壓縮）及 BMP（未壓縮）照片保留所取得影格的 RGB 像素；每個病歷號在同一資料夾內使用 `000090_01.png`、`000090_02.bmp` 等 **01～99** 流水號。已存在的序號會跳過，找不到未使用的 01～99 時**拒絕拍照存檔，不覆寫舊照片**。同一序號在 PNG 和 BMP 間不可重複。
- 右下角「本次照片」縮圖；點進去可以用「上一張」「下一張」瀏覽**這次開啟 App 以來、目前輸入病歷號**拍的所有照片。切換病歷號後各看各的照片。**不掃描舊病歷照片或以前執行過的 App 資料**。
- 左上設定可選相機、模式、照片格式、F8/F9/F10/F12 拍照快捷鍵及**照片/錄影的本機儲存路徑**。快捷鍵改用 WinUI `KeyboardAccelerator`（在程式前景、相機連線且無設定/大圖覆蓋時使用）；錄影中禁止拍照。
- MP4 錄影按鈕保持原本的有損錄影流程，不錄製麥克風音訊；**尚未在診間 v0.4 實測**。

## 儲存路徑與隱私

預設位置：`%LOCALAPPDATA%\ClinicCameraCapture-MVP\captures\`。可於設定按「瀏覽資料夾」或輸入**本機絕對路徑**，再按「套用儲存路徑」。MVP 拒絕 UNC 網路共享、junction/symlink 和正式 `D:\WM003` 路徑；無寫入權限時不會套用。

偏好設定：`%LOCALAPPDATA%\ClinicCameraCapture-MVP\settings.json`（包含 OutputFolder，但不記錄病歷號）。更換儲存資料夾後，新照片從目標資料夾尚未使用的序號開始；**本次批次相簿依病歷號顯示，可能包含切換路徑前已拍攝的照片**，但不掃描歷史資料。

### 不可忽略的限制

- 每一資料夾每一病歷號**最多 99 個兩位數序號**；同一號碼日後再次就診時，舊序號存在就不可覆蓋。需採不同的保存資料夾或另行設計正式病患/就診識別機制。
- **PNG 無損不等於來源無損**：如果設備原本使用有損的 MJPEG 或類比訊號品質差，無法還原。
- 不得以本版寫入正式 HIS；沒有 `PD011M1` 查詢、`CH013M1` 寫入、臨床資料 ACL / 備份 / 稽核。
- WinUI 快捷鍵在有鍵盤焦點的子控制項裡仍需實機驗證；程式不註冊全域熱鍵。
- MP4 僅檢查檔案存在及長度，正式影片可播放／意外中斷恢復需人工確認。

## 建置與測試（開發機）

```powershell
& "D:\tools\dotnet10\dotnet.exe" build "D:\Dev\clinic-camera-capture\ClinicCameraCapture.csproj" -c Debug -p:Platform=x64
& "D:\tools\dotnet10\dotnet.exe" run --project "D:\Dev\clinic-camera-capture\tests\CaptureStore.Smoke\CaptureStore.Smoke.csproj" -c Debug
```

開發機可雙擊 `Start-MVP.cmd`。診間 Win10 要使用完整的 self-contained x64 發佈資料夾，不可只複製 EXE。

## 驗證說明

已自動測試：RGB 無損回讀、BMP BI_RGB、存檔失敗保護、流水號補零、01–99、跨 PNG/BMP 占用、防覆蓋、同病歷號批次分組與儲存路徑 HIS 保護。

**未實機驗證：** v0.4 全螢幕關閉、WinUI 快捷鍵在診間攝影卡與不同焦點下的運作、路徑 Picker、批次照片 UI、錄影及 USB 裝置異常。

詳細資料：[SPEC.md](SPEC.md) · [ARCHITECTURE.md](ARCHITECTURE.md) · [DESIGN.md](DESIGN.md) · [MVP_VALIDATION.md](MVP_VALIDATION.md)。
