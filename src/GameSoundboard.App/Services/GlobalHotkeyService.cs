using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using GameSoundboard.Core.Settings;

namespace GameSoundboard.App.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource _source;
    private readonly Dictionary<Guid, Registration> _bySound = [];
    private readonly Dictionary<int, Guid> _byId = [];
    private int _nextId = 0x5000;

    public GlobalHotkeyService(IntPtr windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle) ?? throw new InvalidOperationException("窗口句柄不可用。");
        _source.AddHook(WndProc);
    }

    public event Action<Guid>? Triggered;

    public string? GetDisplayName(Guid soundId) =>
        _bySound.TryGetValue(soundId, out var registration) ? registration.DisplayName : null;

    public IReadOnlyList<HotkeySettings> GetBindings() => _bySound
        .Select(pair => new HotkeySettings(pair.Key, pair.Value.Modifiers, pair.Value.VirtualKey, pair.Value.DisplayName))
        .ToArray();

    public bool TryRegister(Guid soundId, uint modifiers, uint virtualKey, string displayName, out string error)
    {
        error = string.Empty;
        if (_bySound.Any(pair => pair.Key != soundId && pair.Value.Modifiers == modifiers && pair.Value.VirtualKey == virtualKey))
        {
            error = "该快捷键已经被其他音效使用。";
            return false;
        }

        if (_bySound.TryGetValue(soundId, out var current) &&
            current.Modifiers == modifiers && current.VirtualKey == virtualKey) return true;

        var id = ++_nextId;
        if (!RegisterHotKey(_source.Handle, id, modifiers | ModNoRepeat, virtualKey))
        {
            error = $"Windows 无法注册该快捷键（错误 {Marshal.GetLastWin32Error()}），可能已被其他程序占用。";
            return false;
        }

        Remove(soundId);
        _bySound[soundId] = new Registration(id, modifiers, virtualKey, displayName);
        _byId[id] = soundId;
        return true;
    }

    public void Remove(Guid soundId)
    {
        if (!_bySound.Remove(soundId, out var old)) return;
        UnregisterHotKey(_source.Handle, old.Id);
        _byId.Remove(old.Id);
    }

    public static bool TryFromKeyEvent(KeyEventArgs args, out uint modifiers, out uint virtualKey, out string displayName)
    {
        modifiers = 0;
        virtualKey = 0;
        displayName = string.Empty;
        var key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None) return false;

        var pressed = Keyboard.Modifiers;
        if ((pressed & ModifierKeys.Control) != 0) modifiers |= ModControl;
        if ((pressed & ModifierKeys.Alt) != 0) modifiers |= ModAlt;
        if ((pressed & ModifierKeys.Shift) != 0) modifiers |= ModShift;
        if ((pressed & ModifierKeys.Windows) != 0) modifiers |= ModWin;
        if (modifiers == 0) return false;

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0) return false;
        displayName = string.Join(" + ", new[]
        {
            (modifiers & ModControl) != 0 ? "Ctrl" : null,
            (modifiers & ModAlt) != 0 ? "Alt" : null,
            (modifiers & ModShift) != 0 ? "Shift" : null,
            (modifiers & ModWin) != 0 ? "Win" : null,
            key.ToString()
        }.Where(part => part is not null));
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _byId.TryGetValue(wParam.ToInt32(), out var soundId))
        {
            Triggered?.Invoke(soundId);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var soundId in _bySound.Keys.ToArray()) Remove(soundId);
        _source.RemoveHook(WndProc);
    }

    private readonly record struct Registration(int Id, uint Modifiers, uint VirtualKey, string DisplayName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
