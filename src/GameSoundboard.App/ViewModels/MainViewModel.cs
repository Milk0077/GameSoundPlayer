using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using GameSoundboard.Core.Models;
using GameSoundboard.Core.Settings;
using GameSoundboard.Core.Logging;
using GameSoundboard.Audio.Playback;

namespace GameSoundboard.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAudioDeviceCatalog _catalog;
    private readonly IAudioEngine _engine;
    private readonly AppLogger _logger;
    private readonly WasapiRenderSink _output;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly SemaphoreSlim _recordingGate = new(1, 1);
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _meterTimer;
    private IReadOnlyList<AudioInputDevice> _inputDevices = [];
    private IReadOnlyList<AudioRenderDevice> _outputDevices = [];
    private string? _selectedDeviceId;
    private string? _selectedOutputDeviceId;
    private string _inputStatus = "正在扫描输入设备…";
    private string _deviceSummary = "";
    private bool _isRefreshing;
    private bool _updatingDevices;
    private bool _refreshQueued;
    private bool _disposed;
    private long _selectionVersion;
    private long _outputSelectionVersion;
    private double _micMeterPercent;
    private string _micPeakText = "-60.0 dB";
    private string _engineStatus = "Mixer: Starting";
    private string _outputStatus = "Voicemeeter 输出：未选择";
    private string _soundboardStatus = "支持 WAV、MP3、FLAC、OGG、M4A；音效最长 30 秒";
    private string _musicStatus = "请选择音乐文件";
    private string _musicTime = "00:00 / 00:00";
    private double _musicPositionPercent;
    private int _selectedMusicIndex = -1;
    private long _lastUnderruns;
    private long _lastOverruns;
    private DateTime _lastDiagnosticLog = DateTime.MinValue;
    private DateTime _lastDiagnosticUi = DateTime.MinValue;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private string _diagnosticsText = "正在获取诊断信息…";
    private string _recordingStatus = "本地录制已停止";

    public MainViewModel(IAudioDeviceCatalog catalog, IAudioEngine engine, WasapiRenderSink output, AppLogger logger)
    {
        _catalog = catalog;
        _engine = engine;
        _logger = logger;
        _output = output;
        MixerBuses =
        [
            new MixerBusViewModel(engine, AudioBus.Microphone, "Mic"),
            new MixerBusViewModel(engine, AudioBus.Soundboard, "Soundboard"),
            new MixerBusViewModel(engine, AudioBus.Music, "Music"),
            new MixerBusViewModel(engine, AudioBus.Master, "Master")
        ];
        _catalog.DevicesChanged += OnDevicesChanged;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += OnMeterTimerTick;
        _meterTimer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<SoundEffectInfo> Sounds { get; } = [];
    public IReadOnlyList<HotkeySettings> SavedHotkeys { get; private set; } = [];
    public int SavedMusicLoopMode { get; private set; }
    public int SavedBufferMilliseconds { get; private set; } = 10;
    public bool SavedMicMonitorEnabled { get; private set; }
    public double SavedWindowWidth { get; private set; } = 1120;
    public double SavedWindowHeight { get; private set; } = 650;
    public IReadOnlyList<MixerBusViewModel> MixerBuses { get; }
    public ObservableCollection<MusicTrackInfo> MusicTracks { get; } = [];

    public int SelectedMusicIndex
    {
        get => _selectedMusicIndex;
        set { _selectedMusicIndex = value; OnPropertyChanged(); }
    }

    public string MusicStatus
    {
        get => _musicStatus;
        private set { _musicStatus = value; OnPropertyChanged(); }
    }

    public string MusicTime
    {
        get => _musicTime;
        private set { _musicTime = value; OnPropertyChanged(); }
    }

    public string DiagnosticsText
    {
        get => _diagnosticsText;
        private set { _diagnosticsText = value; OnPropertyChanged(); }
    }

    public string RecordingStatus
    {
        get => _recordingStatus;
        private set { _recordingStatus = value; OnPropertyChanged(); }
    }

    public async Task StartLocalRecordingAsync(string path)
    {
        await _recordingGate.WaitAsync();
        try
        {
            await Task.Run(() => _engine.StartLocalRecording(path));
            RecordingStatus = $"正在录制：{System.IO.Path.GetFileName(path)}";
            _logger.Info($"开始本地混音 WAV 录制：{path}");
        }
        catch (Exception exception)
        {
            RecordingStatus = $"录制失败：{exception.Message}";
            _logger.Error(RecordingStatus);
        }
        finally { _recordingGate.Release(); }
    }

    public async Task StopLocalRecordingAsync()
    {
        await _recordingGate.WaitAsync();
        try
        {
            await Task.Run(_engine.StopLocalRecording);
            RecordingStatus = "本地录制已停止";
            _logger.Info("本地混音 WAV 录制已停止。");
        }
        catch (Exception exception)
        {
            RecordingStatus = $"停止录制失败：{exception.Message}";
            _logger.Error(RecordingStatus);
        }
        finally { _recordingGate.Release(); }
    }

    public double MusicPositionPercent
    {
        get => _musicPositionPercent;
        private set { _musicPositionPercent = value; OnPropertyChanged(); }
    }

    public void AddMusic(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                _engine.AddMusic(path);
                MusicTracks.Add(_engine.GetMusicTracks()[^1]);
                if (SelectedMusicIndex < 0) SelectedMusicIndex = 0;
                MusicStatus = $"已添加 {System.IO.Path.GetFileName(path)}";
            }
            catch (Exception exception) { MusicStatus = $"添加失败：{exception.Message}"; }
        }
    }

    public void PlayMusic()
    {
        _engine.PlayMusic();
    }

    public void PlaySelectedMusic()
    {
        if (SelectedMusicIndex >= 0) _engine.PlayMusicAt(SelectedMusicIndex);
    }

    public void RemoveSelectedMusic()
    {
        var index = SelectedMusicIndex;
        if (index < 0 || index >= MusicTracks.Count) return;
        _engine.RemoveMusic(index);
        MusicTracks.RemoveAt(index);
        SelectedMusicIndex = MusicTracks.Count == 0 ? -1 : Math.Min(index, MusicTracks.Count - 1);
    }

    public void PauseMusic() => _engine.PauseMusic();
    public void StopMusic() => _engine.StopMusic();
    public void NextMusic() => _engine.NextMusic();
    public void PreviousMusic() => _engine.PreviousMusic();
    public void SetMusicLoopMode(MusicLoopMode mode) => _engine.SetMusicLoopMode(mode);

    public void SeekMusic(double percent)
    {
        var info = _engine.GetMusicPlayback();
        if (info.Duration > TimeSpan.Zero)
            _engine.SeekMusic(TimeSpan.FromTicks((long)(info.Duration.Ticks * Math.Clamp(percent, 0, 100) / 100)));
    }

    public string SoundboardStatus
    {
        get => _soundboardStatus;
        private set { _soundboardStatus = value; OnPropertyChanged(); }
    }

    public async Task AddSoundsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                var info = await _engine.AddSoundAsync(path);
                Sounds.Add(info);
                _logger.Info($"音效已加载：{info.Name}");
                SoundboardStatus = $"已加载 {info.Name}，点击播放即可触发。";
            }
            catch (Exception exception)
            {
                SoundboardStatus = $"添加 {System.IO.Path.GetFileName(path)} 失败：{exception.Message}";
                _logger.Error(SoundboardStatus);
            }
        }
    }

    public void PlaySound(Guid id)
    {
        if (!_engine.PlaySound(id)) SoundboardStatus = "音效触发队列已满，请稍后再试。";
    }

    public void StopSound(Guid id) => _engine.StopSound(id);
    public void StopAllSounds() => _engine.StopAllSounds();

    public void SetSoundVolume(Guid id, double volume) =>
        _engine.SetSoundVolume(id, (float)volume);

    public void RemoveSound(Guid id)
    {
        if (!_engine.RemoveSound(id)) return;
        var item = Sounds.FirstOrDefault(sound => sound.Id == id);
        if (item is not null) Sounds.Remove(item);
    }

    public void RenameSound(Guid id, string name)
    {
        if (!_engine.RenameSound(id, name)) return;
        var old = Sounds.FirstOrDefault(sound => sound.Id == id);
        if (old is null) return;
        var index = Sounds.IndexOf(old);
        Sounds[index] = old with { Name = name.Trim() };
    }

    public void SetSoundHotkey(Guid id, string hotkey)
    {
        var old = Sounds.FirstOrDefault(sound => sound.Id == id);
        if (old is null) return;
        Sounds[Sounds.IndexOf(old)] = old with { Hotkey = hotkey };
    }

    public void ShowSoundboardStatus(string message) => SoundboardStatus = message;

    public IReadOnlyList<AudioInputDevice> InputDevices
    {
        get => _inputDevices;
        private set { _inputDevices = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<AudioRenderDevice> OutputDevices
    {
        get => _outputDevices;
        private set { _outputDevices = value; OnPropertyChanged(); }
    }

    public string? SelectedOutputDeviceId
    {
        get => _selectedOutputDeviceId;
        set
        {
            if (_updatingDevices && value is null) return;
            if (_selectedOutputDeviceId == value) return;
            _selectedOutputDeviceId = value;
            OnPropertyChanged();
            _ = ApplySelectedOutputAsync(value, ++_outputSelectionVersion);
        }
    }

    private async Task ApplySelectedOutputAsync(string? deviceId, long version)
    {
        await _outputGate.WaitAsync();
        try
        {
            if (_disposed || version != _outputSelectionVersion) return;
            await _output.SelectDeviceAsync(deviceId);
            if (!_disposed && deviceId == _selectedOutputDeviceId)
            {
                OutputStatus = deviceId is null ? "Voicemeeter 输出：未选择" : "Voicemeeter 输出：已连接";
                _logger.Info($"混音播放设备已连接：{OutputDevices.FirstOrDefault(device => device.Id == deviceId)?.Name ?? "未选择"}");
            }
        }
        catch (Exception exception)
        {
            if (_disposed || deviceId != _selectedOutputDeviceId) return;
            OutputStatus = $"输出设备失败：{exception.Message}";
            _logger.Error(OutputStatus);
        }
        finally { _outputGate.Release(); }
    }

    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set
        {
            if (_updatingDevices && value is null) return;
            if (_selectedDeviceId == value) return;
            _selectedDeviceId = value;
            OnPropertyChanged();
            UpdateStatus();
            _ = ApplySelectedMicrophoneAsync();
        }
    }

    public double MicMeterPercent
    {
        get => _micMeterPercent;
        private set { _micMeterPercent = value; OnPropertyChanged(); }
    }

    public string MicPeakText
    {
        get => _micPeakText;
        private set { _micPeakText = value; OnPropertyChanged(); }
    }

    public string EngineStatus
    {
        get => _engineStatus;
        private set { _engineStatus = value; OnPropertyChanged(); }
    }

    public string OutputStatus
    {
        get => _outputStatus;
        private set { _outputStatus = value; OnPropertyChanged(); }
    }

    public async Task InitializeAsync()
    {
        try
        {
            var settings = _settingsStore.Load();
            _logger.Info("配置已读取，正在初始化音频引擎。");
            _selectedDeviceId = settings.MicrophoneDeviceId;
            _selectedOutputDeviceId = settings.OutputDeviceId;
            SavedHotkeys = settings.Hotkeys;
            SavedMusicLoopMode = Math.Clamp(settings.MusicLoopMode, 0, 2);
            SavedBufferMilliseconds = settings.AudioBufferMilliseconds is 5 or 10 or 20 or 40
                ? settings.AudioBufferMilliseconds : 10;
            SavedMicMonitorEnabled = settings.MicMonitorEnabled;
            SavedWindowWidth = settings.WindowWidth;
            SavedWindowHeight = settings.WindowHeight;
            foreach (var saved in settings.Buses)
            {
                var bus = MixerBuses.FirstOrDefault(item => (int)item.Bus == saved.Bus);
                if (bus is null) continue;
                bus.Volume = Math.Clamp(saved.Volume, 0, 2);
                bus.IsMuted = saved.Muted;
            }
            await _engine.SetBufferMillisecondsAsync(SavedBufferMilliseconds);
            await _engine.StartAsync();
            if (SavedMicMonitorEnabled) await _engine.SetMicMonitorAsync(true);
            _logger.Info("音频混音线程已启动。");
            foreach (var saved in settings.Sounds)
            {
                try
                {
                    var sound = await _engine.RestoreSoundAsync(saved.FilePath, saved.Id, saved.Name, saved.Volume);
                    Sounds.Add(sound);
                }
                catch (Exception exception)
                {
                    _logger.Warn($"已跳过音效 {saved.FilePath}：{exception.Message}");
                }
            }
            foreach (var path in settings.MusicPaths)
            {
                try { _engine.AddMusic(path); }
                catch (Exception exception)
                {
                    _logger.Warn($"已跳过音乐 {path}：{exception.Message}");
                }
            }
            foreach (var track in _engine.GetMusicTracks()) MusicTracks.Add(track);
            if (MusicTracks.Count > 0) SelectedMusicIndex = 0;
            _engine.SetMusicLoopMode((MusicLoopMode)SavedMusicLoopMode);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            InputStatus = $"音频引擎初始化失败：{exception.Message}";
            _logger.Error(InputStatus);
        }
    }

    public void SaveSettings(IReadOnlyList<HotkeySettings> hotkeys, double width, double height, int loopMode)
    {
        var settings = new AppSettings
        {
            MicrophoneDeviceId = SelectedDeviceId,
            OutputDeviceId = SelectedOutputDeviceId,
            Sounds = Sounds.Select(sound => new SoundSettings(sound.Id, sound.FilePath, sound.Name,
                _engine.GetSounds().FirstOrDefault(current => current.Id == sound.Id)?.Volume ?? sound.Volume)).ToList(),
            MusicPaths = MusicTracks.Select(track => track.FilePath).ToList(),
            Hotkeys = hotkeys.ToList(),
            Buses = MixerBuses.Select(bus => new BusSettings((int)bus.Bus, (float)bus.Volume, bus.IsMuted)).ToList(),
            MusicLoopMode = Math.Clamp(loopMode, 0, 2),
            AudioBufferMilliseconds = _engine.BufferMilliseconds,
            MicMonitorEnabled = _engine.MicMonitorEnabled,
            WindowWidth = width,
            WindowHeight = height
        };
        _settingsStore.Save(settings);
    }

    public async Task SetBufferMillisecondsAsync(int milliseconds)
    {
        try
        {
            await _engine.SetBufferMillisecondsAsync(milliseconds);
            _logger.Info($"请求音频缓冲周期已设置为 {milliseconds} ms。");
        }
        catch (Exception exception)
        {
            _logger.Error($"音频缓冲周期设置失败：{exception.Message}");
            InputStatus = $"缓冲设置失败：{exception.Message}";
        }
    }

    public async Task SetMicMonitorAsync(bool enabled)
    {
        try
        {
            await _engine.SetMicMonitorAsync(enabled);
            _logger.Info($"麦克风监听已{(enabled ? "开启" : "关闭")}。");
        }
        catch (Exception exception)
        {
            _logger.Error($"麦克风监听切换失败：{exception.Message}");
            InputStatus = $"监听不可用：{exception.Message}";
        }
    }

    public string InputStatus
    {
        get => _inputStatus;
        private set { _inputStatus = value; OnPropertyChanged(); }
    }

    public string DeviceSummary
    {
        get => _deviceSummary;
        private set { _deviceSummary = value; OnPropertyChanged(); }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set { _isRefreshing = value; OnPropertyChanged(); }
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        if (IsRefreshing)
        {
            _refreshQueued = true;
            return;
        }

        IsRefreshing = true;
        try
        {
            var devices = await Task.Run(_catalog.GetInputDevices);
            var outputDevices = await Task.Run(WasapiRenderSink.GetDevices);
            if (_disposed) return;

            if (_selectedDeviceId is null || devices.Any(device => device.Id == _selectedDeviceId && IsVirtualInput(device.Name)))
            {
                _selectedDeviceId = devices.FirstOrDefault(device => device.IsDefault && device.Availability == AudioDeviceAvailability.Active && !IsVirtualInput(device.Name))?.Id
                    ?? devices.FirstOrDefault(device => device.Availability == AudioDeviceAvailability.Active && !IsVirtualInput(device.Name))?.Id
                    ?? devices.FirstOrDefault(device => device.Availability == AudioDeviceAvailability.Active)?.Id;
            }

            if (_selectedDeviceId is not null && devices.All(device => device.Id != _selectedDeviceId))
            {
                devices = [.. devices, new AudioInputDevice(_selectedDeviceId, "已选择的麦克风", AudioDeviceAvailability.Disconnected, false)];
            }

            _updatingDevices = true;
            try
            {
                InputDevices = devices;
                OnPropertyChanged(nameof(SelectedDeviceId));
                OutputDevices = outputDevices;
                if (_selectedOutputDeviceId is null)
                    _selectedOutputDeviceId = outputDevices.FirstOrDefault(device =>
                        device.Name.Contains("Voicemeeter Input", StringComparison.OrdinalIgnoreCase))?.Id;
                if (_selectedOutputDeviceId is not null && outputDevices.All(device => device.Id != _selectedOutputDeviceId))
                    _selectedOutputDeviceId = null;
                OnPropertyChanged(nameof(SelectedOutputDeviceId));
            }
            finally
            {
                _updatingDevices = false;
            }

            DeviceSummary = $"检测到 {devices.Count(device => device.Availability == AudioDeviceAvailability.Active)} 个可用输入设备";
            _logger.Info($"音频设备刷新：{DeviceSummary}");
            _logger.Info($"混音播放设备选择：{outputDevices.FirstOrDefault(device => device.Id == _selectedOutputDeviceId)?.Name ?? "未选择"}");
            UpdateStatus();
            _ = ApplySelectedMicrophoneAsync();
            _ = ApplySelectedOutputAsync(_selectedOutputDeviceId, ++_outputSelectionVersion);
        }
        catch (Exception exception)
        {
            if (!_disposed)
            {
                InputStatus = $"设备枚举失败：{exception.Message}";
                DeviceSummary = "Windows 音频设备暂不可用";
                _logger.Error(InputStatus);
            }
        }
        finally
        {
            IsRefreshing = false;
            if (_refreshQueued && !_disposed)
            {
                _refreshQueued = false;
                _refreshTimer.Stop();
                _refreshTimer.Start();
            }
        }
    }

    private static bool IsVirtualInput(string name) =>
        name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Virtual Desktop", StringComparison.OrdinalIgnoreCase);

    private void UpdateStatus()
    {
        if (_selectedDeviceId is null)
        {
            InputStatus = _inputDevices.Count == 0 ? "没有检测到麦克风" : "请选择麦克风";
            return;
        }

        var selected = _inputDevices.FirstOrDefault(device => device.Id == _selectedDeviceId);
        InputStatus = selected?.Availability switch
        {
            AudioDeviceAvailability.Active => "Microphone Connected",
            AudioDeviceAvailability.Disabled => "Microphone Disabled",
            AudioDeviceAvailability.Unplugged or AudioDeviceAvailability.NotPresent or AudioDeviceAvailability.Disconnected => "Microphone Disconnected",
            _ => "Microphone Disconnected"
        };
    }

    private void OnDevicesChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        _logger.Info("Windows 音频设备状态发生变化。");
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void OnRefreshTimerTick(object? sender, EventArgs args)
    {
        _refreshTimer.Stop();
        _ = RefreshAsync();
    }

    private async Task ApplySelectedMicrophoneAsync()
    {
        var version = ++_selectionVersion;
        var selected = _inputDevices.FirstOrDefault(device => device.Id == _selectedDeviceId);
        try
        {
            if (selected?.Availability != AudioDeviceAvailability.Active)
            {
                await _engine.SetMicrophoneAsync(null);
                return;
            }

            InputStatus = "正在初始化麦克风…";
            await _engine.SetMicrophoneAsync(selected.Id);
            _logger.Info($"麦克风采集已初始化：{selected.DisplayName}，{_engine.MicrophoneSampleRate} Hz，{_engine.MicrophoneChannels} 声道。");
            if (!_disposed && version == _selectionVersion)
                InputStatus = "Microphone Capturing";
        }
        catch (Exception exception)
        {
            if (_disposed || version != _selectionVersion) return;
            InputStatus = exception.HResult == unchecked((int)0x80070005)
                ? "无法访问麦克风：请检查 Windows 麦克风权限"
                : $"麦克风初始化失败：{exception.Message}";
            _logger.Error(InputStatus);
        }
    }

    private void OnMeterTimerTick(object? sender, EventArgs args)
    {
        var db = _engine.MicrophonePeakDb;
        foreach (var bus in MixerBuses) bus.RefreshPeak();
        MicMeterPercent = Math.Clamp((db + 60) / 60 * 100, 0, 100);
        MicPeakText = $"{db:0.0} dB";
        EngineStatus = _engine.IsRunning ? "Mixer: Running" : "Mixer: Stopped";
        var outputStatus = _selectedOutputDeviceId is null ? "Voicemeeter 输出：未选择" :
            _output.IsConnected ? "Voicemeeter 输出：已连接" : "Voicemeeter 输出：未连接";
        if (OutputStatus != outputStatus)
        {
            OutputStatus = outputStatus;
            _logger.Info($"混音输出状态：{outputStatus}");
        }
        if (_engine.MicrophoneDeviceId is not null && !_engine.IsMicrophoneCapturing && _engine.MicrophoneError is { Length: > 0 } error)
            InputStatus = $"Microphone Disconnected: {error}";
        var music = _engine.GetMusicPlayback();
        MusicStatus = music.Error is not null ? $"音乐播放失败：{music.Error}" :
            music.Name is null ? "请选择音乐文件" : $"{music.Name} · {(music.IsPlaying ? "播放中" : "已暂停或停止")}";
        MusicTime = $"{music.Position:mm\\:ss} / {music.Duration:mm\\:ss}";
        MusicPositionPercent = music.Duration.TotalSeconds > 0
            ? Math.Clamp(music.Position.TotalSeconds / music.Duration.TotalSeconds * 100, 0, 100) : 0;
        if (DateTime.UtcNow - _lastDiagnosticLog > TimeSpan.FromSeconds(10))
        {
            var underruns = _engine.MicrophoneUnderruns;
            var overruns = _engine.MicrophoneOverruns;
            if (underruns != _lastUnderruns || overruns != _lastOverruns)
                _logger.Warn($"音频缓冲统计：underrun={underruns}，overrun={overruns}，输出丢块={_engine.DroppedOutputBlocks}。");
            _lastUnderruns = underruns;
            _lastOverruns = overruns;
            _lastDiagnosticLog = DateTime.UtcNow;
        }
        if (DateTime.UtcNow - _lastDiagnosticUi > TimeSpan.FromSeconds(1))
        {
            DiagnosticsText =
                $"输入：{_engine.MicrophoneSampleRate} Hz / {_engine.MicrophoneChannels} ch\n" +
                $"请求周期：{_engine.BufferMilliseconds} ms\n" +
                $"Mixer 周期：{_engine.MixerPeriods}；迟到：{_engine.LateMixerPeriods}\n" +
                $"Mic underrun：{_engine.MicrophoneUnderruns}；overrun：{_engine.MicrophoneOverruns}\n" +
                $"输出丢块：{_engine.DroppedOutputBlocks}；音效触发丢弃：{_engine.DroppedSoundTriggers}\n" +
                $"Voicemeeter 输出丢块：{_output.DroppedBlocks}；错误：{_output.LastError ?? "none"}\n" +
                $"运行：{DateTime.UtcNow - _startedAt:hh\\:mm\\:ss}；端到端延迟：未测量";
            _lastDiagnosticUi = DateTime.UtcNow;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _meterTimer.Stop();
        _meterTimer.Tick -= OnMeterTimerTick;
        _catalog.DevicesChanged -= OnDevicesChanged;
        _engine.Dispose();
        _catalog.Dispose();
    }
}
