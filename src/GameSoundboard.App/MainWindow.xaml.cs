using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using GameSoundboard.App.Services;
using GameSoundboard.App.ViewModels;
using GameSoundboard.Core.Models;
using Microsoft.Win32;

namespace GameSoundboard.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private GlobalHotkeyService? _hotkeys;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) =>
        {
            _hotkeys = new GlobalHotkeyService(new WindowInteropHelper(this).Handle);
            _hotkeys.Triggered += _viewModel.PlaySound;
        };
        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            if (double.IsFinite(_viewModel.SavedWindowWidth) && _viewModel.SavedWindowWidth >= MinWidth)
                Width = _viewModel.SavedWindowWidth;
            if (double.IsFinite(_viewModel.SavedWindowHeight) && _viewModel.SavedWindowHeight >= MinHeight)
                Height = _viewModel.SavedWindowHeight;
            MusicLoopBox.SelectedIndex = _viewModel.SavedMusicLoopMode;
            AudioBufferBox.SelectedIndex = _viewModel.SavedBufferMilliseconds switch
            {
                5 => 0, 20 => 2, 40 => 3, _ => 1
            };
            MicMonitorBox.IsChecked = _viewModel.SavedMicMonitorEnabled;
            if (_hotkeys is null) return;
            foreach (var saved in _viewModel.SavedHotkeys)
            {
                if (_viewModel.Sounds.All(sound => sound.Id != saved.SoundId)) continue;
                if (_hotkeys.TryRegister(saved.SoundId, saved.Modifiers, saved.VirtualKey, saved.DisplayName, out var error))
                    _viewModel.SetSoundHotkey(saved.SoundId, saved.DisplayName);
                else _viewModel.ShowSoundboardStatus(error);
            }
        };
        Closing += (_, _) =>
        {
            try { _viewModel.SaveSettings(_hotkeys?.GetBindings() ?? [], Width, Height, MusicLoopBox.SelectedIndex); }
            catch (Exception exception) { MessageBox.Show(this, $"配置保存失败：{exception.Message}", "Yiqth 音效器"); }
        };
        Closed += (_, _) => _hotkeys?.Dispose();
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void AddSounds_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加音效",
            Filter = "音效文件 (*.wav;*.mp3;*.flac;*.ogg;*.m4a)|*.wav;*.mp3;*.flac;*.ogg;*.m4a|所有文件 (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.AddSoundsAsync(dialog.FileNames);
    }

    private void PlaySound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id }) _viewModel.PlaySound(id);
    }

    private void StopSound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id }) _viewModel.StopSound(id);
    }

    private void RemoveSound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id })
        {
            _hotkeys?.Remove(id);
            _viewModel.RemoveSound(id);
        }
    }

    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id } || _hotkeys is null) return;
        var dialog = new Window
        {
            Title = "设置全局快捷键",
            Width = 390,
            Height = 150,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };
        dialog.Content = new TextBlock
        {
            Text = "按 Ctrl/Alt/Shift/Win 加一个按键；Delete 清除，Esc 取消。",
            Margin = new Thickness(18),
            TextWrapping = TextWrapping.Wrap
        };
        dialog.PreviewKeyDown += (_, args) =>
        {
            var key = args.Key == Key.System ? args.SystemKey : args.Key;
            if (key == Key.Escape)
            {
                dialog.Close();
                return;
            }
            if (key == Key.Delete)
            {
                _hotkeys.Remove(id);
                _viewModel.SetSoundHotkey(id, "");
                dialog.Close();
                return;
            }
            if (!GlobalHotkeyService.TryFromKeyEvent(args, out var modifiers, out var virtualKey, out var displayName)) return;
            args.Handled = true;
            if (_hotkeys.TryRegister(id, modifiers, virtualKey, displayName, out var error))
            {
                _viewModel.SetSoundHotkey(id, displayName);
                _viewModel.ShowSoundboardStatus($"已绑定 {displayName}。即使窗口失焦也可以播放。 ");
                dialog.Close();
            }
            else
            {
                _viewModel.ShowSoundboardStatus(error);
                if (dialog.Content is TextBlock text) text.Text = error + " 请换一个组合键。";
            }
        };
        dialog.ShowDialog();
    }

    private void EditSound_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        var sound = _viewModel.Sounds.FirstOrDefault(item => item.Id == id);
        if (sound is null) return;
        var dialog = new Window
        {
            Title = "编辑音效名称",
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        var nameBox = new TextBox { Text = sound.Name, MaxLength = 80, Margin = new Thickness(0, 0, 0, 12) };
        var saveButton = new Button { Content = "保存", HorizontalAlignment = HorizontalAlignment.Right };
        saveButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text)) return;
            _viewModel.RenameSound(id, nameBox.Text);
            dialog.DialogResult = true;
        };
        panel.Children.Add(nameBox);
        panel.Children.Add(saveButton);
        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private void StopAllSounds_Click(object sender, RoutedEventArgs e) => _viewModel.StopAllSounds();

    private async void StartRecording_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存本地混音 WAV",
            Filter = "WAV 文件 (*.wav)|*.wav",
            FileName = $"Yiqth-音效器-{DateTime.Now:yyyyMMdd-HHmmss}.wav"
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.StartLocalRecordingAsync(dialog.FileName);
    }

    private async void StopRecording_Click(object sender, RoutedEventArgs e) => await _viewModel.StopLocalRecordingAsync();

    private void AddMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加音乐",
            Filter = "音乐文件 (*.wav;*.mp3;*.flac;*.ogg;*.m4a)|*.wav;*.mp3;*.flac;*.ogg;*.m4a|所有文件 (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true) _viewModel.AddMusic(dialog.FileNames);
    }

    private void PlayMusic_Click(object sender, RoutedEventArgs e) => _viewModel.PlayMusic();
    private void RemoveMusic_Click(object sender, RoutedEventArgs e) => _viewModel.RemoveSelectedMusic();
    private void PauseMusic_Click(object sender, RoutedEventArgs e) => _viewModel.PauseMusic();
    private void StopMusic_Click(object sender, RoutedEventArgs e) => _viewModel.StopMusic();
    private void NextMusic_Click(object sender, RoutedEventArgs e) => _viewModel.NextMusic();
    private void PreviousMusic_Click(object sender, RoutedEventArgs e) => _viewModel.PreviousMusic();
    private void MusicList_DoubleClick(object sender, MouseButtonEventArgs e) => _viewModel.PlaySelectedMusic();
    private void MusicSeekSlider_MouseUp(object sender, MouseButtonEventArgs e) => _viewModel.SeekMusic(MusicSeekSlider.Value);
    private void MusicLoopBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || MusicLoopBox is null) return;
        _viewModel.SetMusicLoopMode((MusicLoopMode)Math.Clamp(MusicLoopBox.SelectedIndex, 0, 2));
    }

    private async void AudioBufferBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || AudioBufferBox is null || AudioBufferBox.SelectedIndex < 0) return;
        var selected = new[] { 5, 10, 20, 40 }[AudioBufferBox.SelectedIndex];
        await _viewModel.SetBufferMillisecondsAsync(selected);
    }

    private async void MicMonitorBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || MicMonitorBox is null) return;
        await _viewModel.SetMicMonitorAsync(MicMonitorBox.IsChecked == true);
    }

    private void SoundVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { Tag: Guid id }) _viewModel.SetSoundVolume(id, e.NewValue);
    }
}
