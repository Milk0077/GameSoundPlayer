using System.Configuration;
using System.Data;
using System.Windows;
using GameSoundboard.App.ViewModels;
using GameSoundboard.Audio;
using GameSoundboard.Audio.Capture;
using GameSoundboard.Audio.Playback;
using GameSoundboard.Core.Logging;

namespace GameSoundboard.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private MainViewModel? _mainViewModel;
    private AppLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _logger = new AppLogger();
        _logger.Info("程序启动；正在检测 Voicemeeter 播放设备。");
        var output = new WasapiRenderSink();
        _mainViewModel = new MainViewModel(new WasapiDeviceCatalog(), new AudioEngine(output), output, _logger);
        var window = new MainWindow(_mainViewModel);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        _logger?.Dispose();
        base.OnExit(e);
    }
}

