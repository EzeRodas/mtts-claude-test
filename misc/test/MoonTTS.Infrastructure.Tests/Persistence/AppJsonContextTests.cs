using System.Text.Json;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;
using MoonTTS.Infrastructure.Daemon.DTOs;
using MoonTTS.Infrastructure.Persistence;

namespace MoonTTS.Infrastructure.Tests.Persistence;

/* Round trips through the source-generated AppJsonContext for every persisted model and daemon DTO */
public sealed class AppJsonContextTests
{
    /* Serializes and deserializes with the given generated metadata */
    private static T RoundTrip<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(value, typeInfo), typeInfo)!;

    /* AppConfig keeps every setting, including the active model */
    [Fact]
    public void AppConfig_RoundTrip_KeepsAllSettings()
    {
        var config = new AppConfig
        {
            CustomModelsDirectory = "/models",
            ActiveModel = new ModelId("kokoro", "v1"),
            HideOnSynth = true,
            OpenOnStartup = true,
            DefaultMonitor = 2,
            RememberPosition = true,
            PosX = 10,
            PosY = 20,
            ToggleAppHotkey = "Ctrl+Shift+T",
            CreateMicOnStartup = true,
            EnablePrimaryPlayback = false,
            PrimaryAudioDeviceId = "speakers",
            PrimaryVolume = 80,
            EnableMonitorPlayback = true,
            MonitorAudioDeviceId = "virtual",
            MonitorVolume = 60,
            SaveHistory = false
        };

        var copy = RoundTrip(config, AppJsonContext.Default.AppConfig);

        Assert.Equivalent(config, copy, strict: true);
    }

    /* An unselected model round trips as Unselected */
    [Fact]
    public void AppConfig_UnselectedModel_RoundTripsAsUnselected()
    {
        var copy = RoundTrip(new AppConfig(), AppJsonContext.Default.AppConfig);

        Assert.True(copy.ActiveModel.IsUnselected);
    }

    /* Persisted files are human-editable (indented) */
    [Fact]
    public void AppConfig_Serialize_IsIndented()
    {
        string json = JsonSerializer.Serialize(new AppConfig(), AppJsonContext.Default.AppConfig);

        Assert.Contains("\n", json);
    }

    /* Dictionary entries keep id, words and case sensitivity */
    [Fact]
    public void DictionaryEntries_RoundTrip_KeepsEntries()
    {
        var entries = new List<DictionaryEntry> { new() { Original = "gg", Spelling = "good game", CaseSensitive = true } };

        Assert.Equivalent(entries, RoundTrip(entries, AppJsonContext.Default.ListDictionaryEntry), strict: true);
    }

    /* Profiles keep their names, folders, entries and the active profile */
    [Fact]
    public void ProfileDataStore_RoundTrip_KeepsProfiles()
    {
        var store = new ProfileDataStore
        {
            ActiveProfileName = "Work",
            Profiles =
            [
                new HotkeyProfile { Name = "Work", FolderName = "work", Entries = [new HotkeyEntry { Id = Guid.NewGuid(), Text = "hi", Hotkey = "Ctrl+1" }] }
            ]
        };

        Assert.Equivalent(store, RoundTrip(store, AppJsonContext.Default.ProfileDataStore), strict: true);
    }

    /* Hotkeys keep id, text and key combination */
    [Fact]
    public void HotkeyEntries_RoundTrip_KeepsEntries()
    {
        var entries = new List<HotkeyEntry> { new() { Id = Guid.NewGuid(), Text = "brb", Hotkey = "Alt+B" } };

        Assert.Equivalent(entries, RoundTrip(entries, AppJsonContext.Default.ListHotkeyEntry), strict: true);
    }

    /* History entries keep their required members */
    [Fact]
    public void HistoryEntries_RoundTrip_KeepsEntries()
    {
        var entries = new List<HistoryEntry>
        {
            new() { Id = Guid.NewGuid(), Text = "hello", CreatedAt = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc), WavFileName = "a.wav" }
        };

        Assert.Equivalent(entries, RoundTrip(entries, AppJsonContext.Default.ListHistoryEntry), strict: true);
    }

    /* Per-model preferences keep voice and speed */
    [Fact]
    public void EngineConfig_RoundTrip_KeepsVoiceAndSpeed()
    {
        var config = new EngineConfig { VoiceId = "af_heart", Speed = 1.2f };

        Assert.Equivalent(config, RoundTrip(config, AppJsonContext.Default.EngineConfig), strict: true);
    }

    /* Daemon requests keep action, text and speed */
    [Fact]
    public void DaemonRequest_RoundTrip_KeepsFields()
    {
        var request = new DaemonRequest { Action = DaemonIpcProtocol.ActionStatus, Text = "hello", Speed = 1.5f };

        Assert.Equivalent(request, RoundTrip(request, AppJsonContext.Default.DaemonRequest), strict: true);
    }

    /* Daemon responses keep status, model, idle time and the audio format */
    [Fact]
    public void DaemonResponse_RoundTrip_KeepsFieldsAndFormat()
    {
        var response = new DaemonResponse
        {
            Success = true,
            ErrorMessage = "none",
            ActiveModel = "kokoro/v1",
            IdleSecondsRemaining = 120,
            Format = new AudioFormat(24000, 1, 16)
        };

        Assert.Equivalent(response, RoundTrip(response, AppJsonContext.Default.DaemonResponse), strict: true);
    }

    /* A daemon response without format keeps it null */
    [Fact]
    public void DaemonResponse_WithoutFormat_RoundTripsNullFormat()
    {
        Assert.Null(RoundTrip(new DaemonResponse { Success = false }, AppJsonContext.Default.DaemonResponse).Format);
    }

    /* AudioFormat keeps sample rate, channels and bit depth */
    [Fact]
    public void AudioFormat_RoundTrip_KeepsGeometry()
    {
        var format = new AudioFormat(44100, 2, 24);

        Assert.Equal(format, RoundTrip(format, AppJsonContext.Default.AudioFormat));
    }
}
