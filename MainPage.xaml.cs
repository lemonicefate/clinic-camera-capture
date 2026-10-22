using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using Windows.UI;

namespace ClinicCameraCapture;

/// <summary>
/// Camera-first WinUI 3 UI. All files stay under the MVP-only local test folder.
/// This code does not open HIS DBF files or write an HIS photo index.
/// </summary>
public sealed partial class MainPage : Page
{
    private sealed record CameraChoice(string Name, string Id);
    private sealed record FormatChoice(MediaFrameFormat Format, string Label);

    private readonly SemaphoreSlim _cameraGate = new(1, 1);
    private CameraPreferences _preferences = CameraPreferences.Load();
    private CaptureStore CurrentStore => new(_preferences.OutputFolder);
    private readonly PhotoBatch _batch = new();
    private readonly KeyboardAccelerator _photoAccelerator = new();
    private MediaCapture? _camera;
    private MediaFrameSource? _frameSource;
    private List<FormatChoice> _supportedFormats = new();
    private bool _loadingUi = true;
    private bool _busy;
    private bool _recording;
    private string? _recordingPath;
    private int _activePhotoIndex;

    public MainPage()
    {
        InitializeComponent();
        SelectTag(ModePicker, _preferences.Mode);
        SelectTag(FormatPicker, _preferences.Format);
        SelectTag(ShortcutPicker, _preferences.Shortcut);
        FullScreenCheckBox.IsChecked = _preferences.FullScreen;
        OutputFolderBox.Text = _preferences.OutputFolder;
        _photoAccelerator.Key = Enum.Parse<VirtualKey>(_preferences.Shortcut);
        _photoAccelerator.Invoked += PhotoAccelerator_Invoked;
        CameraRoot.KeyboardAccelerators.Add(_photoAccelerator);
        _loadingUi = false;
        CameraRoot.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(HandleKeyboard), true);
        UpdateButtons();
        UpdateModeDisplay();

        Loaded += async (_, _) =>
        {
            MainWindow.Active?.SetFullScreen(_preferences.FullScreen);
            await RefreshCamerasAsync();
            if (CameraPicker.SelectedItem is CameraChoice)
                await StartCameraAsync();
            PatientNumberBox.Focus(FocusState.Programmatic);
        };
        Unloaded += (_, _) => _ = StopCameraAsync(false);
    }

    private static string SelectedTag(ComboBox box, string defaultValue) =>
        (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? defaultValue;

    private static void SelectTag(ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
            if (item.Tag?.ToString() == tag) { box.SelectedItem = item; return; }
    }

    private void SavePreferences()
    {
        try { _preferences.Save(); }
        catch (Exception ex) { SetStatus("設定儲存失敗", ex.Message, InfoBarSeverity.Warning); }
    }

    private void SetStatus(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        ResultBar.Title = title;
        ResultBar.Message = message;
        ResultBar.Severity = severity;
        ResultBar.IsOpen = true;
    }

    private bool HasValidPatient => CaptureIdentity.IsValid(PatientNumberBox.Text);

    private void UpdateButtons()
    {
        CameraPicker.IsEnabled = !_busy && !_recording && _camera is null;
        RefreshButton.IsEnabled = !_busy && !_recording;
        StartButton.IsEnabled = !_busy && !_recording && _camera is null
            && CameraPicker.SelectedItem is CameraChoice;
        StopButton.IsEnabled = !_busy && !_recording && _camera is not null;
        ShutterButton.IsEnabled = !_busy && _camera is not null && (_recording || HasValidPatient);
        PatientNumberBox.IsEnabled = !_recording;
        ModePicker.IsEnabled = !_busy && !_recording;
        FormatPicker.IsEnabled = !_busy && !_recording;
        FrameFormatPicker.IsEnabled = !_busy && !_recording && _frameSource is not null;
        ApplyFormatButton.IsEnabled = !_busy && !_recording && _frameSource is not null
            && FrameFormatPicker.SelectedItem is FormatChoice;
        CopyFormatsButton.IsEnabled = !_busy && _supportedFormats.Count > 0;
        ThumbnailButton.IsEnabled = _batch.For(PatientNumberBox.Text).Count > 0;
        PatientHintText.Text = HasValidPatient ? "✓ " + CaptureIdentity.Require(PatientNumberBox.Text) : "輸入 1–6 位數字";
        PatientHintText.Foreground = new SolidColorBrush(HasValidPatient ? Colors.LightGreen : Colors.Wheat);
    }

    private void UpdateModeDisplay()
    {
        if (_recording)
        {
            ShutterButton.Content = "■";
            ShutterButton.Background = new SolidColorBrush(Colors.Red);
            ShutterLabel.Text = "停止錄影";
            ModeLabel.Text = "錄影模式";
            RecordingIndicator.Visibility = Visibility.Visible;
        }
        else if (_preferences.Mode == "video")
        {
            ShutterButton.Content = "●";
            ShutterButton.Background = new SolidColorBrush(Colors.IndianRed);
            ShutterLabel.Text = "開始錄影";
            ModeLabel.Text = "錄影模式";
            RecordingIndicator.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShutterButton.Content = "●";
            ShutterButton.Background = new SolidColorBrush(Colors.White);
            ShutterLabel.Text = "拍照";
            ModeLabel.Text = "拍照模式";
            RecordingIndicator.Visibility = Visibility.Collapsed;
        }
        ShutterButton.IsEnabled = !_busy && _camera is not null && (_recording || HasValidPatient);
        ToolTipService.SetToolTip(ShutterButton, _preferences.Mode == "photo"
            ? "拍照快捷鍵：" + _preferences.Shortcut : "錄影：按鈕開始／停止");
    }

    private async void PatientNumberBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateButtons();
        try { await RefreshBatchThumbnailAsync(); }
        catch (Exception ex) { SetStatus("讀取縮圖失敗", ex.Message, InfoBarSeverity.Warning); }
    }

    private void PatientNumberBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (HasValidPatient)
            PatientNumberBox.Text = CaptureIdentity.Require(PatientNumberBox.Text);
    }

    private async void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        await StopCameraAsync(false);
        MainWindow.Active?.Close();
    }

    private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            if (MainWindow.Active is null) throw new InvalidOperationException("找不到程式視窗。");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(MainWindow.Active));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) OutputFolderBox.Text = folder.Path;
        }
        catch (Exception ex) { SetStatus("選擇資料夾失敗", ex.Message, InfoBarSeverity.Error); }
    }

    private void ApplyOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recording || _busy)
        {
            SetStatus("目前不能變更路徑", "請先結束錄影或拍攝。", InfoBarSeverity.Warning);
            return;
        }
        try
        {
            var verified = OutputFolderPolicy.Normalize(OutputFolderBox.Text);
            Directory.CreateDirectory(verified);
            // Probe only the explicit user-selected folder. Remove the probe
            // immediately; no HIS DBF/index files are opened or modified.
            var probe = Path.Combine(verified, "." + Guid.NewGuid().ToString("N") + ".tmp");
            using (File.Create(probe)) { }
            File.Delete(probe);
            _preferences.OutputFolder = verified;
            OutputFolderBox.Text = verified;
            SavePreferences();
            SetStatus("儲存路徑已更新", verified + "。只儲存影像/影片檔，不會自動更新 HIS 病歷索引。", InfoBarSeverity.Success);
        }
        catch (Exception ex) { SetStatus("無法使用這個儲存路徑", ex.Message, InfoBarSeverity.Error); }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsLayer.Visibility = Visibility.Visible;

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsLayer.Visibility = Visibility.Collapsed;

    private void CameraPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CameraPicker.SelectedItem is CameraChoice choice && !_loadingUi)
        {
            _preferences.CameraId = choice.Id;
            SavePreferences();
        }
        UpdateButtons();
    }

    private void ModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        _preferences.Mode = SelectedTag(ModePicker, "photo");
        SavePreferences();
        UpdateModeDisplay();
    }

    private void FormatPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        _preferences.Format = SelectedTag(FormatPicker, "png");
        SavePreferences();
    }

    private void ShortcutPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        _preferences.Shortcut = SelectedTag(ShortcutPicker, "F9");
        _photoAccelerator.Key = Enum.Parse<VirtualKey>(_preferences.Shortcut);
        SavePreferences();
        UpdateModeDisplay();
    }

    private void FullScreenCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        _preferences.FullScreen = FullScreenCheckBox.IsChecked == true;
        SavePreferences();
        MainWindow.Active?.SetFullScreen(_preferences.FullScreen);
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        var win = MainWindow.Active;
        if (win is null) return;
        win.SetFullScreen(!win.IsFullScreen);
        _preferences.FullScreen = win.IsFullScreen;
        FullScreenCheckBox.IsChecked = _preferences.FullScreen;
        SavePreferences();
    }

    private void HandleKeyboard(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            if (PhotoOverlay.Visibility == Visibility.Visible)
                ClosePhoto();
            else if (SettingsLayer.Visibility == Visibility.Visible)
                SettingsLayer.Visibility = Visibility.Collapsed;
            else if (MainWindow.Active?.IsFullScreen == true)
                MainWindow.Active.SetFullScreen(false);
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.F11)
        {
            FullScreenButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (SettingsLayer.Visibility == Visibility.Visible || PhotoOverlay.Visibility == Visibility.Visible
            || _recording || _busy || !HasValidPatient || _camera is null)
            return;

        // Photo shortcut is handled by a WinUI KeyboardAccelerator, which
        // participates in focused TextBox / control accelerator routing.
    }

    private void PhotoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (SettingsLayer.Visibility == Visibility.Visible || PhotoOverlay.Visibility == Visibility.Visible
            || _recording || _busy) return;
        if (!HasValidPatient)
        {
            SetStatus("快捷鍵已觸發", "請先輸入 1 至 6 位數字病歷號。", InfoBarSeverity.Warning);
            return;
        }
        if (_camera is null)
        {
            SetStatus("快捷鍵已觸發", "相機尚未連線；請檢查設定。", InfoBarSeverity.Warning);
            return;
        }
        _ = TakePhotoAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshCamerasAsync();

    private async Task RefreshCamerasAsync()
    {
        if (_recording) return;
        await _cameraGate.WaitAsync();
        _busy = true;
        UpdateButtons();
        try
        {
            CloseCamera();
            CameraPicker.ItemsSource = null;
            var discovered = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var choices = discovered.Select(d => new CameraChoice(d.Name, d.Id)).ToList();
            CameraPicker.ItemsSource = choices;
            if (choices.Count == 0)
            {
                EmptyPreviewText.Text = "找不到相機。請打開左上角設定並檢查 USB 擷取卡。";
                SetStatus("找不到相機", "檢查攝影機裝置與 Windows 相機權限。", InfoBarSeverity.Warning);
                return;
            }

            // Prefer the previously selected stable ID, not the OS enumeration index.
            var preferred = choices.FindIndex(c => c.Id == _preferences.CameraId);
            if (preferred < 0)
                preferred = choices.FindIndex(c => c.Name.Contains("USB2.0 PC CAMERA", StringComparison.OrdinalIgnoreCase));
            CameraPicker.SelectedIndex = preferred >= 0 ? preferred : 0;
            EmptyPreviewText.Text = "正在連線到影像裝置…";
        }
        catch (Exception ex)
        {
            SetStatus("搜尋攝影機失敗", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
            _cameraGate.Release();
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e) => await StartCameraAsync();

    private async Task StartCameraAsync()
    {
        if (_recording) return;
        await _cameraGate.WaitAsync();
        _busy = true;
        UpdateButtons();
        try
        {
            if (CameraPicker.SelectedItem is not CameraChoice choice)
                throw new InvalidOperationException("尚未選擇攝影機。");

            CloseCamera();
            _camera = new MediaCapture();
            _camera.Failed += Camera_Failed;
            await _camera.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = choice.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl
            });
            _frameSource = _camera.FrameSources.Values.FirstOrDefault(s =>
                    s.Info.SourceKind == MediaFrameSourceKind.Color
                    && (s.Info.MediaStreamType == MediaStreamType.VideoPreview
                        || s.Info.MediaStreamType == MediaStreamType.VideoRecord))
                ?? _camera.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                ?? throw new InvalidOperationException("裝置沒有彩色影像串流。");

            _supportedFormats = _frameSource.SupportedFormats
                .Where(f => f.VideoFormat is not null && f.VideoFormat.Width > 0 && f.VideoFormat.Height > 0)
                .OrderByDescending(f => (long)f.VideoFormat.Width * f.VideoFormat.Height)
                .ThenBy(f => f.Subtype, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FormatChoice(f, DescribeFormat(f)))
                .ToList();
            FrameFormatPicker.ItemsSource = _supportedFormats;
            var current = _frameSource.CurrentFormat;
            FrameFormatPicker.SelectedItem = _supportedFormats.FirstOrDefault(c =>
                c.Format.Subtype == current.Subtype
                && c.Format.VideoFormat.Width == current.VideoFormat.Width
                && c.Format.VideoFormat.Height == current.VideoFormat.Height);
            DeviceDetailText.Text = choice.Name + "｜" + DescribeFormat(current);
            FrameFormatNote.Text = $"Windows 回報 {_supportedFormats.Count} 種格式。預覽尺寸與實際拍照尺寸可能不同。";
            PreviewElement.Source = MediaSource.CreateFromMediaFrameSource(_frameSource);
            EmptyPreviewText.Visibility = Visibility.Collapsed;
            _preferences.CameraId = choice.Id;
            SavePreferences();
            SetStatus("攝影機已連線", DeviceDetailText.Text);
            SettingsLayer.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            CloseCamera();
            EmptyPreviewText.Text = "開啟左上角設定，重新選擇影像裝置。";
            SetStatus("攝影機初始化失敗", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
            UpdateModeDisplay();
            _cameraGate.Release();
        }
    }

    private void Camera_Failed(MediaCapture sender, MediaCaptureFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await StopCameraAsync(false);
            SetStatus("攝影機中斷", $"{args.Code:X}: {args.Message}", InfoBarSeverity.Error);
        });
    }

    private static string DescribeFormat(MediaFrameFormat f)
    {
        var fps = f.FrameRate.Denominator == 0
            ? "?"
            : ((double)f.FrameRate.Numerator / f.FrameRate.Denominator).ToString("0.##");
        return $"{f.VideoFormat.Width}×{f.VideoFormat.Height} / {f.Subtype} / {fps}fps";
    }

    private void FrameFormatPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private async void ApplyFormatButton_Click(object sender, RoutedEventArgs e)
    {
        await _cameraGate.WaitAsync();
        _busy = true;
        UpdateButtons();
        try
        {
            if (_frameSource is null || FrameFormatPicker.SelectedItem is not FormatChoice selection)
                throw new InvalidOperationException("請先連線並選擇格式。");
            var previous = _frameSource.CurrentFormat;
            PreviewElement.Source = null;
            try { await _frameSource.SetFormatAsync(selection.Format); }
            catch
            {
                try { await _frameSource.SetFormatAsync(previous); } catch { }
                throw;
            }
            finally
            {
                PreviewElement.Source = MediaSource.CreateFromMediaFrameSource(_frameSource);
            }
            DeviceDetailText.Text = (CameraPicker.SelectedItem as CameraChoice)?.Name
                + "｜" + DescribeFormat(_frameSource.CurrentFormat);
            SetStatus("格式已套用", "拍照存檔解析度仍須另行確認：" + DescribeFormat(_frameSource.CurrentFormat));
        }
        catch (Exception ex) { SetStatus("無法切換格式", ex.Message, InfoBarSeverity.Error); }
        finally { _busy = false; UpdateButtons(); _cameraGate.Release(); }
    }

    private void CopyFormatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_supportedFormats.Count == 0) return;
        var text = (CameraPicker.SelectedItem as CameraChoice)?.Name + Environment.NewLine
            + string.Join(Environment.NewLine, _supportedFormats.Select(f => f.Label));
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        SetStatus("格式已複製", "可以直接貼到 ChatGPT；不包含影像或病歷資料。");
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e) => await StopCameraAsync();

    private async Task StopCameraAsync(bool showStatus = true)
    {
        await _cameraGate.WaitAsync();
        _busy = true;
        UpdateButtons();
        try
        {
            if (_recording) await StopRecordingCoreAsync();
            if (showStatus) SetStatus("相機已停止", "已釋放相機，不會修改 HIS。");
        }
        catch (Exception ex) { SetStatus("關閉相機失敗", ex.Message, InfoBarSeverity.Error); }
        finally
        {
            CloseCamera();
            _recording = false;
            _busy = false;
            UpdateButtons();
            UpdateModeDisplay();
            _cameraGate.Release();
        }
    }

    private void CloseCamera()
    {
        PreviewElement.Source = null;
        EmptyPreviewText.Visibility = Visibility.Visible;
        _frameSource = null;
        _supportedFormats.Clear();
        FrameFormatPicker.ItemsSource = null;
        FrameFormatNote.Text = "開始預覽後可查看裝置的支援格式。";
        if (_camera is not null)
        {
            _camera.Failed -= Camera_Failed;
            _camera.Dispose();
            _camera = null;
        }
        DeviceDetailText.Text = "尚未連線";
    }

    private async void ShutterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_preferences.Mode == "video") await ToggleRecordingAsync();
        else await TakePhotoAsync();
    }

    private async Task TakePhotoAsync()
    {
        await _cameraGate.WaitAsync();
        _busy = true;
        UpdateButtons();
        try
        {
            if (_recording) throw new InvalidOperationException("錄影進行中，請先停止錄影。");
            if (_camera is null) throw new InvalidOperationException("請先連線相機。");
            var patientNumber = CaptureIdentity.Require(PatientNumberBox.Text);
            var session = await _camera.PrepareLowLagPhotoCaptureAsync(
                ImageEncodingProperties.CreateUncompressed(MediaPixelFormat.Bgra8));
            try
            {
                var photo = await session.CaptureAsync();
                using var bitmap = photo.Frame.SoftwareBitmap
                    ?? throw new InvalidOperationException("無法取得未壓縮影像像素。");
                var saved = await CurrentStore.SaveAsync(bitmap, _preferences.Format, patientNumber);
                _batch.Add(patientNumber, saved.FilePath);
                try
                {
                    await RefreshBatchThumbnailAsync();
                    SetStatus("照片已通過驗證",
                        $"{saved.Width}×{saved.Height}、{saved.Bytes:N0} bytes。檔案僅在本機測試目錄，尚未登錄 HIS。",
                        InfoBarSeverity.Success);
                }
                catch (Exception displayError)
                {
                    SetStatus("照片已儲存，但縮圖載入失敗",
                        $"檔案已保留在本機測試資料夾；縮圖錯誤：{displayError.Message}",
                        InfoBarSeverity.Warning);
                }
            }
            finally { await session.FinishAsync(); }
        }
        catch (Exception ex) { SetStatus("拍照或儲存失敗", ex.Message, InfoBarSeverity.Error); }
        finally { _busy = false; UpdateButtons(); UpdateModeDisplay(); _cameraGate.Release(); }
    }

    private static async Task<BitmapImage> LoadPhotoImageAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private async Task RefreshBatchThumbnailAsync()
    {
        var photos = _batch.For(PatientNumberBox.Text);
        ThumbnailButton.IsEnabled = photos.Count > 0;
        if (photos.Count == 0)
        {
            ThumbnailImage.Source = null;
            ThumbnailPlaceholder.Visibility = Visibility.Visible;
            return;
        }
        string refAtStart = CaptureIdentity.Require(PatientNumberBox.Text);
        var image = await LoadPhotoImageAsync(photos[^1]);
        if (CaptureIdentity.IsValid(PatientNumberBox.Text) &&
            CaptureIdentity.Require(PatientNumberBox.Text) == refAtStart)
        {
            ThumbnailImage.Source = image;
            ThumbnailPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ShowPhotoAtAsync(int index)
    {
        var photos = _batch.For(PatientNumberBox.Text);
        if (index < 0 || index >= photos.Count) return;
        var path = photos[index];
        FullPhotoImage.Source = await LoadPhotoImageAsync(path);
        _activePhotoIndex = index;
        PhotoTitle.Text = $"{CaptureIdentity.Require(PatientNumberBox.Text)}｜第 {index + 1} / {photos.Count} 張｜{Path.GetFileName(path)}";
        PrevPhotoButton.IsEnabled = index > 0;
        NextPhotoButton.IsEnabled = index < photos.Count - 1;
    }

    private async void ThumbnailButton_Click(object sender, RoutedEventArgs e)
    {
        var photos = _batch.For(PatientNumberBox.Text);
        if (photos.Count == 0) return;
        try
        {
            await ShowPhotoAtAsync(photos.Count - 1);
            PhotoOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { SetStatus("無法開啟照片", ex.Message, InfoBarSeverity.Error); }
    }

    private async void PrevPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        try { await ShowPhotoAtAsync(_activePhotoIndex - 1); }
        catch (Exception ex) { SetStatus("無法開啟上一張", ex.Message, InfoBarSeverity.Error); }
    }

    private async void NextPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        try { await ShowPhotoAtAsync(_activePhotoIndex + 1); }
        catch (Exception ex) { SetStatus("無法開啟下一張", ex.Message, InfoBarSeverity.Error); }
    }

    private void ClosePhotoButton_Click(object sender, RoutedEventArgs e) => ClosePhoto();
    private void ClosePhoto() => PhotoOverlay.Visibility = Visibility.Collapsed;

    private async Task ToggleRecordingAsync()
    {
        await _cameraGate.WaitAsync();
        _busy = true;
        UpdateButtons();
        try
        {
            if (_recording) await StopRecordingCoreAsync();
            else
            {
                if (_camera is null) throw new InvalidOperationException("請先連線攝影機。");
                var patientNumber = CaptureIdentity.Require(PatientNumberBox.Text);
                Directory.CreateDirectory(_preferences.OutputFolder);
                var folder = await StorageFolder.GetFolderFromPathAsync(_preferences.OutputFolder);
                var filename = CaptureIdentity.VideoName(patientNumber);
                var file = await folder.CreateFileAsync(filename, CreationCollisionOption.FailIfExists);
                _recordingPath = file.Path;
                try
                {
                    var encoding = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
                    encoding.Audio = null; // No microphone or patient conversation in this MVP.
                    await _camera.StartRecordToStorageFileAsync(encoding, file);
                    _recording = true;
                    SetStatus("錄影中", "按右側紅色按鈕結束；只儲存在獨立本機目錄。");
                }
                catch
                {
                    _recordingPath = null;
                    // Remove only the empty file we just created; preserve any partial video.
                    if (File.Exists(file.Path) && new FileInfo(file.Path).Length == 0)
                        File.Delete(file.Path);
                    throw;
                }
            }
        }
        catch (Exception ex) { SetStatus("錄影失敗", ex.Message, InfoBarSeverity.Error); }
        finally { _busy = false; UpdateButtons(); UpdateModeDisplay(); _cameraGate.Release(); }
    }

    private async Task StopRecordingCoreAsync()
    {
        if (!_recording || _camera is null) return;
        try
        {
            await _camera.StopRecordAsync();
            _recording = false;
            if (_recordingPath is null || !File.Exists(_recordingPath)
                || new FileInfo(_recordingPath).Length == 0)
                throw new IOException("錄影停止，但未產生有效影像檔案。");
            SetStatus("錄影已停止", "MP4 已存至本機測試資料夾；沒有寫入 HIS。",
                InfoBarSeverity.Success);
        }
        finally
        {
            _recording = false;
            _recordingPath = null;
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_preferences.OutputFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _preferences.OutputFolder + "\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) { SetStatus("無法開啟測試資料夾", ex.Message, InfoBarSeverity.Error); }
    }
}
