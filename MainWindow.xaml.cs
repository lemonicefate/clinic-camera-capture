using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace ClinicCameraCapture;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Active { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Active = this;
        AppWindow.Resize(new SizeInt32(1600, 900)); // FullScreen uses actual monitor size.
        AppWindow.SetIcon("Assets/AppIcon.ico");
        RootFrame.Navigate(typeof(MainPage));
        Closed += (_, _) => Active = null;
    }

    public bool IsFullScreen => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    public void SetFullScreen(bool enabled)
    {
        AppWindow.SetPresenter(enabled
            ? AppWindowPresenterKind.FullScreen
            : AppWindowPresenterKind.Overlapped);
        if (!enabled) AppWindow.Resize(new SizeInt32(1600, 900));
    }
}
