using System.ComponentModel;
using System.Runtime.CompilerServices;
using GameSoundboard.Core.Models;

namespace GameSoundboard.App.ViewModels;

public sealed class MixerBusViewModel : INotifyPropertyChanged
{
    private readonly IAudioEngine _engine;
    private float _volume = 1;
    private bool _isMuted;
    private double _peakPercent;
    private string _peakText = "-60.0 dB";

    public MixerBusViewModel(IAudioEngine engine, AudioBus bus, string name)
    {
        _engine = engine;
        Bus = bus;
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public AudioBus Bus { get; }
    public string Name { get; }

    public double Volume
    {
        get => _volume;
        set
        {
            var next = (float)Math.Clamp(value, 0, 2);
            if (Math.Abs(_volume - next) < 0.0001) return;
            _volume = next;
            _engine.SetBusVolume(Bus, next);
            OnPropertyChanged();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            _engine.SetMute(Bus, value);
            OnPropertyChanged();
        }
    }

    public double PeakPercent
    {
        get => _peakPercent;
        private set { _peakPercent = value; OnPropertyChanged(); }
    }

    public string PeakText
    {
        get => _peakText;
        private set { _peakText = value; OnPropertyChanged(); }
    }

    public void RefreshPeak()
    {
        var db = _engine.GetBusPeakDb(Bus);
        PeakPercent = Math.Clamp((db + 60) / 60 * 100, 0, 100);
        PeakText = $"{db:0.0} dB";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
