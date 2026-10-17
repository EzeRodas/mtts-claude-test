using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Domain.Tests.Entities;

/* Verifies the out-of-the-box settings of AppConfig */
public sealed class AppConfigTests
{
    /* No custom models directory is set by default */
    [Fact]
    public void CustomModelsDirectory_Default_IsNull()
    {
        Assert.Null(new AppConfig().CustomModelsDirectory);
    }

    /* No model is active by default */
    [Fact]
    public void ActiveModel_Default_IsUnselected()
    {
        Assert.True(new AppConfig().ActiveModel.IsUnselected);
    }

    /* Window behaviour switches are off by default */
    [Fact]
    public void WindowFlags_Default_AreFalse()
    {
        var config = new AppConfig();

        Assert.False(config.HideOnSynth);
        Assert.False(config.OpenOnStartup);
        Assert.False(config.RememberPosition);
    }

    /* The window starts on the first monitor at the origin by default */
    [Fact]
    public void WindowPlacement_Default_IsFirstMonitorAtOrigin()
    {
        var config = new AppConfig();

        Assert.Equal(0, config.DefaultMonitor);
        Assert.Equal(0, config.PosX);
        Assert.Equal(0, config.PosY);
    }

    /* The window toggle hotkey defaults to Ctrl+Alt+M */
    [Fact]
    public void ToggleAppHotkey_Default_IsCtrlAltM()
    {
        Assert.Equal("Ctrl+Alt+M", new AppConfig().ToggleAppHotkey);
    }

    /* The default toggle hotkey is a valid shortcut */
    [Fact]
    public void ToggleAppHotkey_Default_ParsesAsShortcut()
    {
        Assert.True(ShortcutCombo.TryParse(new AppConfig().ToggleAppHotkey, out _));
    }

    /* The virtual microphone is not created on startup by default */
    [Fact]
    public void CreateMicOnStartup_Default_IsFalse()
    {
        Assert.False(new AppConfig().CreateMicOnStartup);
    }

    /* Primary playback is enabled on the OS default device at full volume */
    [Fact]
    public void PrimaryPlayback_Default_IsEnabledOnOsDefaultAtFullVolume()
    {
        var config = new AppConfig();

        Assert.True(config.EnablePrimaryPlayback);
        Assert.Equal("-1", config.PrimaryAudioDeviceId);
        Assert.Equal(100, config.PrimaryVolume);
    }

    /* Monitor playback is disabled with no device and full volume */
    [Fact]
    public void MonitorPlayback_Default_IsDisabledWithoutDevice()
    {
        var config = new AppConfig();

        Assert.False(config.EnableMonitorPlayback);
        Assert.Null(config.MonitorAudioDeviceId);
        Assert.Equal(100, config.MonitorVolume);
    }

    /* History is saved by default */
    [Fact]
    public void SaveHistory_Default_IsTrue()
    {
        Assert.True(new AppConfig().SaveHistory);
    }

    /* The active model keeps the value it is set to */
    [Fact]
    public void ActiveModel_WhenSet_KeepsValue()
    {
        var model = new ModelId("kokoro", "v1");

        var config = new AppConfig { ActiveModel = model };

        Assert.Equal(model, config.ActiveModel);
    }
}
