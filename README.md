# Clinic Camera Capture — Windows Native Camera MVP v0.6.1

**C# + XAML / WinUI 3 / .NET 10 / Windows MediaCapture**

非臨床測試版：新增 `PD011M1.dbf` **選配、唯讀**患者查詢，但不修改 HIS 的 DBF/CDX、也不自動建立 `CH013M1` 影像索引。**開發及測試只用合成 DBF／假號碼與非病患物件，絕不將病患資料提交至公開 GitHub。**

## 功能

- 640×480 擷取卡來源可放大在 1920×1080 全螢幕顯示（維持 4:3、左右黑邊；畫質不會變成 HD）。
- 右側快門拍照／錄影按鈕；右側上方**「× 關閉」**可直接結束全螢幕 App。F11 切換全螢幕，Esc 退出全螢幕／照片檢視。
- 上方新增病歷號、身分證字號（可輸入）與姓名（唯讀）三欄。病歷號 1–6 位數字按 **Enter 或離開欄位** 時補零六碼；若已設定 PD011M1.dbf，透過 **NUM** 唯讀帶入 NAME／ID；ID 空白時顯示 `0000` + 六位病歷號。身分證號按 **Enter 或離開欄位** 時，以 **ID** 反查 NUM／NAME。**v0.6.1 已修正成功查詢後延遲 TextChanged 清空三欄的問題，且重新編輯識別欄位仍會取消舊患者確認。** 查不到患者或 DBF 無法讀取時禁止拍照，避免誤歸檔。
- PNG（無損壓縮）及 BMP（未壓縮）照片保留影格 RGB 像素；自動建立 **`六碼病歷號_yyyyMMdd`** 資料夾，例如 `000090_20261023`。同一病歷號、同一天沿用資料夾，照片依 `_01`～`_99` 編號且跨 PNG/BMP 共用；既有照片不覆蓋，99 個序號用滿則提示錯誤。隔日建立新的日期資料夾，該日可從 `_01` 重新開始。錄影 MP4 亦儲存在以錄影**開始日期**命名的相同子資料夾。
- 右下角「本次照片」縮圖；點擊後可用「上一張／下一張」按鈕，或鍵盤 **←／→** 瀏覽這次開啟 App 以來、目前輸入病歷號的全部照片。切換病歷號時不混用相簿，且不掃描歷史資料。
- 左上設定可選相機、模式、照片格式、F8/F9/F10/F12 拍照快捷鍵及**照片/錄影的任意儲存路徑**（本機、網路共享、映射磁碟等）。快捷鍵改用 WinUI `KeyboardAccelerator`（在程式前景、相機連線且無設定/大圖覆蓋時使用）；錄影中禁止拍照。
- MP4 錄影維持原本的有損影像錄製流程，不錄製麥克風音訊；**尚未在診間 v0.5 實測**。

## 儲存路徑與隱私

預設位置：`%LOCALAPPDATA%\ClinicCameraCapture-MVP\captures\`。可於設定按「瀏覽資料夾」或輸入任意可解析路徑，再按「套用儲存路徑」。**不再限制儲存位置**：本機磁碟、根目錄、UNC 網路共享、映射磁碟、相對路徑、junction/symlink、甚至 `D:\WM003` 都允許；仍須通過 Windows 目錄建立及寫入權限檢查。相對路徑會解析為絕對路徑後儲存；網路路徑必須實際可連線。

偏好設定：`%LOCALAPPDATA%\ClinicCameraCapture-MVP\settings.json`（記住 OutputFolder 及 PatientDbfPath，**不存**病歷號、姓名或身分證）。程式把 OutputFolder 視為**根目錄**；每次拍照按當下 Windows 本地日期解析子資料夾，錄影則按開始錄影日期。**本次批次相簿依病歷號顯示，可能包含切換路徑前或跨日拍攝的照片**，但不掃描歷史資料。

### 不可忽略的限制

- 每個**病歷號＋日期**資料夾最多 99 個兩位數照片序號；當日重啟 App 會接續已有編號，隔日可從 `_01` 開始。**舊版直接放在根目錄或純病歷號資料夾內的影像不會被搬動或重新編號**；有需要請另行規劃受控遷移。
- **PNG 無損不等於來源無損**：如果設備原本使用有損的 MJPEG 或類比訊號品質差，無法還原。
- 患者查詢以 FileAccess.Read 和 FileShare.ReadWrite|Delete 開啟 PD011M1，將 NUM／ID 建成 RAM 索引；不使用或改寫 CDX，並於 DBF 的大小／修改時間改變時重建索引。**尚未用實際診間 PD011M1 驗證欄位結構與即時更新狀況。**
- 設定記憶 DBF 檔案路徑，不保存患者姓名／身分證／病歷號。若 ID 為空，僅顯示 0000+NUM 替代值，不會把替代值回寫 DBF，也不視為實際 ID 查詢鍵。
- 允許將輸出路徑設在 HIS 影像目錄，但只寫入影像／影片檔；另可**唯讀**查詢使用者設定的 `PD011M1.dbf`（NUM／ID／NAME），不讀寫 `CDX`，也**不會新增 `CH013M1`**，因此照片不一定能在 HIS 查到。正式使用需先完成個資權限、備份與稽核驗收。
- WinUI 快捷鍵在有鍵盤焦點的子控制項裡仍需實機驗證；程式不註冊全域熱鍵。
- MP4 僅檢查檔案存在及長度，正式影片可播放／意外中斷恢復需人工確認。

## 建置與測試（開發機）

```powershell
& "D:\tools\dotnet10\dotnet.exe" build "D:\Dev\clinic-camera-capture\ClinicCameraCapture.csproj" -c Debug -p:Platform=x64
& "D:\tools\dotnet10\dotnet.exe" run --project "D:\Dev\clinic-camera-capture\tests\CaptureStore.Smoke\CaptureStore.Smoke.csproj" -c Debug
```

開發機可雙擊 `Start-MVP.cmd`。診間 Win10 要使用完整的 self-contained x64 發佈資料夾，不可只複製 EXE。

## 驗證說明

已自動測試：RGB 無損回讀、BMP BI_RGB、存檔失敗保護、流水號補零、01–99、防覆蓋、相簿分組及任意路徑正規化。新增**完全合成的 VFP DBF** 測試，通過 CP950 中文姓名、NUM／ID 雙向查詢、ID 空白替代、刪除紀錄忽略，以及與另一個程序共享開啟的唯讀測試。**所有 DBF 測試只用合成資料，未讀取正式 HIS。**

**已用合成資料驗證：** v0.6.1 的病歷號＋日期資料夾、無損存檔及唯讀 DBF 測試。另使用 Windows UI Automation 搭配合成 PD011M1.dbf，**驗證病歷號或身分證按 Enter／失焦後三欄皆保留**，以及修改識別欄位不會反過來清空正在輸入的內容；四種組合皆通過。測試腳本 `tests/PatientLookup.UiRepro.ps1` 會備份並還原開發機的本機設定。**未診間實測：** 真實 HIS 資料欄位、Windows 10 擷取卡、MP4、UNC 網路共享和日期跨午夜的操作情境。

詳細資料：[SPEC.md](SPEC.md) · [ARCHITECTURE.md](ARCHITECTURE.md) · [DESIGN.md](DESIGN.md) · [MVP_VALIDATION.md](MVP_VALIDATION.md)。
