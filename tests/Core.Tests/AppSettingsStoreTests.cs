using GameSoundboard.Core.Settings;

namespace GameSoundboard.Core.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void RoundTripsSettingsAndRecoversFromInvalidJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gsb-config-{Guid.NewGuid():N}");
        try
        {
            var store = new AppSettingsStore(directory);
            var soundId = Guid.NewGuid();
            store.Save(new AppSettings
            {
                MicrophoneDeviceId = "endpoint-123",
                Sounds = [new SoundSettings(soundId, "C:\\test.wav", "test", 0.7f)],
                Hotkeys = [new HotkeySettings(soundId, 2, 112, "Ctrl + F1")],
                MusicPaths = ["C:\\song.mp3"],
                AudioBufferMilliseconds = 20
            });
            var loaded = store.Load();
            Assert.Equal("endpoint-123", loaded.MicrophoneDeviceId);
            Assert.Equal(soundId, loaded.Sounds.Single().Id);
            Assert.Equal(112u, loaded.Hotkeys.Single().VirtualKey);
            Assert.Equal(20, loaded.AudioBufferMilliseconds);
            Assert.False(File.Exists(store.ConfigPath + ".tmp"));

            File.WriteAllText(store.ConfigPath, "{broken");
            Assert.Empty(store.Load().Sounds);
        }
        finally { Directory.Delete(directory, true); }
    }
}
