using System.ComponentModel;
using MoonTTS.Avalonia.Tests.TestSupport;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Avalonia.Tests.ViewModels;

/* Verifies SettingsViewModel against the real Core services built over in-memory fakes (GuiServices) */
public sealed class SettingsViewModelTests
{
    private static readonly AudioFormat Format = new(22050, 1, 16);

    /* Builds a voice whose display name differs from its id */
    private static VoiceInfo Voice(string id) => new(id, id.ToUpperInvariant());

    /* Builds an output device */
    private static AudioDeviceInfo Device(string id) => new() { DeviceId = id, DeviceName = $"Device {id}" };

    /* Registers an installed model with the given voices and makes it the active one */
    private static ModelMetadata AddActiveModel(GuiServices services, params VoiceInfo[] voices)
    {
        var model = services.AddModel("engine", "active", installed: true, voices);
        services.Config.ActiveModel = model.Id;
        return model;
    }

    /* Waits until the voices of the engine loaded by the constructor are listed */
    private static Task WaitForVoicesAsync(SettingsViewModel vm, int count) =>
        Eventually.UntilAsync(() => vm.AvailableVoices.Count == count, $"{count} voices are listed");

    /* ---------- Constructor & loaders ---------- */

    /* The audio device list always starts with the default system device, followed by the player's devices */
    [Fact]
    public void Ctor_WithDevices_ListsDefaultDeviceFirst()
    {
        using var services = new GuiServices();
        services.Player.Devices.AddRange([Device("a"), Device("b")]);

        var vm = services.CreateViewModel();

        Assert.Equal(["-1", "a", "b"], vm.AvailableOutputDevices.Select(d => d.DeviceId));
        Assert.Equal("Default System Device", vm.AvailableOutputDevices[0].DeviceName);
    }

    /* A device reported twice by the OS is listed once */
    [Fact]
    public void Ctor_DuplicateDeviceIds_ListsEachDeviceOnce()
    {
        using var services = new GuiServices();
        services.Player.Devices.AddRange([Device("a"), Device("a")]);

        var vm = services.CreateViewModel();

        Assert.Equal(["-1", "a"], vm.AvailableOutputDevices.Select(d => d.DeviceId));
    }

    /* When the player already reports the default device, no second default entry is inserted */
    [Fact]
    public void Ctor_PlayerReportsDefaultDevice_KeepsPlayersDefaultEntry()
    {
        using var services = new GuiServices();
        services.Player.Devices.AddRange([Device("a"), new AudioDeviceInfo { DeviceId = "-1", DeviceName = "OS default" }]);

        var vm = services.CreateViewModel();

        Assert.Equal(["a", "-1"], vm.AvailableOutputDevices.Select(d => d.DeviceId));
        Assert.Equal("OS default", vm.AvailableOutputDevices[1].DeviceName);
    }

    /* A failed device enumeration still offers the default system device */
    [Fact]
    public void Ctor_DeviceEnumerationFails_ListsOnlyDefaultDevice()
    {
        using var services = new GuiServices();
        services.Player.DevicesResultOverride = Result<List<AudioDeviceInfo>>.Failure("pactl missing");

        var vm = services.CreateViewModel();

        Assert.Equal(["-1"], vm.AvailableOutputDevices.Select(d => d.DeviceId));
    }

    /* Every model of every installed engine is offered, downloaded or not (issue #6) */
    [Fact]
    public void Ctor_ListsEveryRegisteredModel()
    {
        using var services = new GuiServices();
        var installed = services.AddModel("engine", "installed", installed: true);
        var missing = services.AddModel("other", "missing", installed: false);

        var vm = services.CreateViewModel();

        Assert.Equal([installed, missing], vm.AvailableModels);
    }

    /* Each model gets a status row with its id, display name and installation state */
    [Fact]
    public void Ctor_BuildsModelStatusForEveryModel()
    {
        using var services = new GuiServices();
        var installed = services.AddModel("engine", "installed", installed: true);
        var missing = services.AddModel("engine", "missing", installed: false);

        var vm = services.CreateViewModel();

        Assert.Collection(vm.ModelStatuses,
            s => Assert.Equal((installed.Id, installed.DisplayName, true, 0.0), (s.Model, s.Name, s.IsDownloaded, s.Progress)),
            s => Assert.Equal((missing.Id, missing.DisplayName, false, 0.0), (s.Model, s.Name, s.IsDownloaded, s.Progress)));
    }

    /* The model saved as active is selected */
    [Fact]
    public void Ctor_SelectsActiveModelFromConfig()
    {
        using var services = new GuiServices();
        services.AddModel("engine", "first", installed: true);
        var active = AddActiveModel(services);

        var vm = services.CreateViewModel();

        Assert.Same(active, vm.SelectedModel);
    }

    /* An active model that no installed engine provides leaves the selector empty */
    [Fact]
    public void Ctor_ActiveModelNotProvided_LeavesModelUnselected()
    {
        using var services = new GuiServices();
        services.AddModel("engine", "first", installed: true);
        services.Config.ActiveModel = new ModelId("gone", "model");

        var vm = services.CreateViewModel();

        Assert.Null(vm.SelectedModel);
    }

    /* The voices come from the engine the provider loads for the active model */
    [Fact]
    public async Task Ctor_LoadsVoicesOfActiveEngine()
    {
        using var services = new GuiServices();
        AddActiveModel(services, Voice("a"), Voice("b"));

        var vm = services.CreateViewModel();

        await WaitForVoicesAsync(vm, 2);
        Assert.Equal(["a", "b"], vm.AvailableVoices.Select(v => v.Id));
    }

    /* The voice saved for the active model is selected */
    [Fact]
    public async Task Ctor_SelectsVoiceSavedForActiveModel()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services, Voice("a"), Voice("b"));
        services.Settings.GetEngineConfig(active.Id).VoiceId = "b";

        var vm = services.CreateViewModel();

        await Eventually.UntilAsync(() => vm.SelectedVoice?.Id == "b", "the saved voice is selected");
    }

    /* A saved voice the engine no longer offers falls back to the first voice */
    [Fact]
    public async Task Ctor_SavedVoiceMissing_SelectsFirstVoice()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services, Voice("a"), Voice("b"));
        services.Settings.GetEngineConfig(active.Id).VoiceId = "removed";

        var vm = services.CreateViewModel();

        await Eventually.UntilAsync(() => vm.SelectedVoice?.Id == "a", "the first voice is selected");
    }

    /* Without a selected model the first voice of the loaded engine is selected */
    [Fact]
    public async Task Ctor_NoSelectedModel_SelectsFirstVoice()
    {
        using var services = new GuiServices();
        var engine = new FakeTtsEngine { IsAvailable = false };
        engine.Voices.AddRange([Voice("x"), Voice("y")]);
        services.Factory.Engines[ModelId.Unselected] = () => engine;

        var vm = services.CreateViewModel();

        await Eventually.UntilAsync(() => vm.SelectedVoice?.Id == "x", "the first voice is selected");
        Assert.Null(vm.SelectedModel);
    }

    /* General settings are copied from the app config */
    [Fact]
    public void Ctor_LoadsGeneralSettings()
    {
        using var services = new GuiServices();
        var config = services.Config;
        config.DefaultMonitor = 2;
        config.OpenOnStartup = true;
        config.HideOnSynth = true;
        config.RememberPosition = true;
        config.CreateMicOnStartup = true;
        config.ToggleAppHotkey = "Ctrl+Shift+X";

        var vm = services.CreateViewModel();

        Assert.Equal((2, true, true, true, true, "Ctrl+Shift+X", config.CustomModelsDirectory),
            (vm.SelectedMonitorIndex, vm.OpenOnStartup, vm.HideOnSynth, vm.RememberPosition, vm.CreateMicOnStartup,
             vm.ToggleAppHotkey, vm.CustomModelsDirectory));
    }

    /* Playback switches and volumes are copied from the app config */
    [Fact]
    public void Ctor_LoadsPlaybackSettings()
    {
        using var services = new GuiServices();
        var config = services.Config;
        config.EnablePrimaryPlayback = false;
        config.PrimaryVolume = 30;
        config.EnableMonitorPlayback = true;
        config.MonitorVolume = 60;

        var vm = services.CreateViewModel();

        Assert.Equal((false, 30, true, 60), (vm.EnablePrimaryPlayback, vm.PrimaryVolume, vm.EnableMonitorPlayback, vm.MonitorVolume));
    }

    /* The configured primary and monitor devices are selected */
    [Fact]
    public void Ctor_SelectsConfiguredDevices()
    {
        using var services = new GuiServices();
        services.Player.Devices.AddRange([Device("speakers"), Device("mic")]);
        services.Config.PrimaryAudioDeviceId = "speakers";
        services.Config.MonitorAudioDeviceId = "mic";

        var vm = services.CreateViewModel();

        Assert.Equal(("speakers", "mic"), (vm.SelectedPrimaryDevice?.DeviceId, vm.SelectedMonitorDevice?.DeviceId));
    }

    /* An unknown primary device falls back to the first listed device (the default system device) */
    [Fact]
    public void Ctor_UnknownPrimaryDevice_SelectsFirstDevice()
    {
        using var services = new GuiServices();
        services.Player.Devices.Add(Device("speakers"));
        services.Config.PrimaryAudioDeviceId = "unplugged";

        var vm = services.CreateViewModel();

        Assert.Equal("-1", vm.SelectedPrimaryDevice?.DeviceId);
    }

    /* An unknown monitor device falls back to the default system device */
    [Fact]
    public void Ctor_UnknownMonitorDevice_SelectsDefaultDevice()
    {
        using var services = new GuiServices();
        services.Player.Devices.AddRange([Device("speakers"), new AudioDeviceInfo { DeviceId = "-1", DeviceName = "OS default" }]);
        services.Config.MonitorAudioDeviceId = "unplugged";

        var vm = services.CreateViewModel();

        Assert.Equal("OS default", vm.SelectedMonitorDevice?.DeviceName);
    }

    /* The speech speed saved for the active model is loaded */
    [Fact]
    public void Ctor_LoadsSpeedOfActiveModel()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        services.Settings.GetEngineConfig(active.Id).Speed = 1.5f;

        var vm = services.CreateViewModel();

        Assert.Equal(1.5f, vm.SpeechSpeed);
    }

    /* A non-positive saved speed is replaced by the normal speed */
    [Fact]
    public void Ctor_NonPositiveSavedSpeed_UsesNormalSpeed()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        services.Settings.GetEngineConfig(active.Id).Speed = 0f;

        var vm = services.CreateViewModel();

        Assert.Equal(1.0f, vm.SpeechSpeed);
    }

    /* A config without a custom models folder shows an empty text box */
    [Fact]
    public void Ctor_NoCustomModelsDirectory_ShowsEmptyText()
    {
        using var services = new GuiServices();
        services.Config.CustomModelsDirectory = null;

        var vm = services.CreateViewModel();

        Assert.Equal(string.Empty, vm.CustomModelsDirectory);
    }

    /* The dictionary entries are listed */
    [Fact]
    public void Ctor_LoadsDictionaryEntries()
    {
        using var services = new GuiServices();
        services.Dictionary.AddEntry("GPU", "gee pee you");

        var vm = services.CreateViewModel();

        Assert.Equal(["GPU"], vm.DictionaryEntries.Select(e => e.Original));
    }

    /* The phrase hotkeys are listed */
    [Fact]
    public async Task Ctor_LoadsHotkeyEntries()
    {
        using var services = new GuiServices();
        await services.Hotkeys.AssignHotkeyAsync("Ctrl+1", "hello");

        var vm = services.CreateViewModel();

        Assert.Equal(["Ctrl+1"], vm.HotkeyEntries.Select(e => e.Hotkey));
    }

    /* The speech history is listed */
    [Fact]
    public async Task Ctor_LoadsHistoryEntries()
    {
        using var services = new GuiServices();
        var entry = await services.AddHistoryEntryAsync("spoken", [1, 2], Format);

        var vm = services.CreateViewModel();

        Assert.Equal([entry.Id], vm.HistoryEntries.Select(e => e.Id));
    }

    /* Loading the settings never writes them back */
    [Fact]
    public async Task Ctor_DoesNotSaveSettings()
    {
        using var services = new GuiServices();
        AddActiveModel(services, Voice("a"));
        services.Player.Devices.Add(Device("speakers"));
        int appSaves = services.Repo.AppConfigSaveCount;

        var vm = services.CreateViewModel();

        await WaitForVoicesAsync(vm, 1);
        Assert.Equal((appSaves, 0), (services.Repo.AppConfigSaveCount, services.Repo.EngineConfigSaveCount));
    }

    /* The window opens on the Quick view with the General tab selected */
    [Fact]
    public void Ctor_StartsInQuickViewOnGeneralTab()
    {
        using var services = new GuiServices();

        var vm = services.CreateViewModel();

        Assert.Equal((false, AdvancedTab.General), (vm.IsAdvancedView, vm.SelectedTab));
    }

    /* Without the optional managers and engine provider the related collections stay empty */
    [Fact]
    public void Ctor_WithoutOptionalServices_LeavesOptionalCollectionsEmpty()
    {
        using var services = new GuiServices();
        AddActiveModel(services, Voice("a"));
        services.Dictionary.AddEntry("GPU", "gee pee you");

        var vm = new SettingsViewModel(services.Settings, services.Player, services.Windows, services.Downloads);

        Assert.Empty(vm.AvailableVoices);
        Assert.Empty(vm.DictionaryEntries);
        Assert.Empty(vm.HotkeyEntries);
        Assert.Empty(vm.HistoryEntries);
    }

    /* The previewer constructor builds an empty view model without touching any service */
    [Fact]
    public void PreviewCtor_LoadsNothing()
    {
        var vm = new PreviewSettingsViewModel();

        Assert.Empty(vm.AvailableOutputDevices);
        Assert.Empty(vm.AvailableModels);
        Assert.Empty(vm.ModelStatuses);
        Assert.Empty(vm.AvailableVoices);
    }

    /* ---------- LoadSettings ---------- */

    /* LoadSettings picks up values changed elsewhere (e.g. by the CLI) since the window was built */
    [Fact]
    public void LoadSettings_ConfigChangedExternally_ShowsNewValues()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        services.Config.PrimaryVolume = 12;

        vm.LoadSettings();

        Assert.Equal(12, vm.PrimaryVolume);
    }

    /* LoadSettings refreshes the dictionary list */
    [Fact]
    public void LoadSettings_DictionaryChangedExternally_ListsNewEntries()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        services.Dictionary.AddEntry("CPU", "see pee you");

        vm.LoadSettings();

        Assert.Equal(["CPU"], vm.DictionaryEntries.Select(e => e.Original));
    }

    /* Reloading does not write the settings back */
    [Fact]
    public void LoadSettings_DoesNotSaveSettings()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        int appSaves = services.Repo.AppConfigSaveCount;

        vm.LoadSettings();

        Assert.Equal(appSaves, services.Repo.AppConfigSaveCount);
    }

    /* Reloading cancels a hotkey recording in progress */
    [Fact]
    public void LoadSettings_WhileRecording_StopsRecording()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        vm.LoadSettings();

        Assert.False(vm.IsRecordingToggleHotkey);
    }

    /* Without services LoadSettings keeps the built-in defaults */
    [Fact]
    public void LoadSettings_WithoutServices_KeepsDefaults()
    {
        var vm = new PreviewSettingsViewModel();

        vm.LoadSettings();

        Assert.Equal((100, 1.0f, "Ctrl+Alt+M"), (vm.PrimaryVolume, vm.SpeechSpeed, vm.ToggleAppHotkey));
    }

    /* ---------- Model selection (issue #6) ---------- */

    /* Selecting a model that is not downloaded explains how to install it */
    [Fact]
    public void SelectedModel_NotDownloaded_ShowsNotInstalledMessage()
    {
        using var services = new GuiServices();
        AddActiveModel(services);
        var missing = services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();

        vm.SelectedModel = missing;

        Assert.Equal(SettingsViewModel.ModelNotInstalledMessage, vm.ModelStatusMessage);
    }

    /* Selecting a model that is not downloaded reverts the selector to the saved active model */
    [Fact]
    public async Task SelectedModel_NotDownloaded_RevertsToActiveModel()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var missing = services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();

        vm.SelectedModel = missing;

        await Eventually.UntilAsync(() => vm.SelectedModel == active, "the selection reverts to the active model");
    }

    /* Rejecting a model that is not downloaded saves nothing */
    [Fact]
    public async Task SelectedModel_NotDownloaded_DoesNotSaveSettings()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var missing = services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();
        int appSaves = services.Repo.AppConfigSaveCount;

        vm.SelectedModel = missing;
        await Eventually.UntilAsync(() => vm.SelectedModel == active, "the selection reverts to the active model");

        Assert.Equal((appSaves, 0, active.Id),
            (services.Repo.AppConfigSaveCount, services.Repo.EngineConfigSaveCount, services.Config.ActiveModel));
    }

    /* Rejecting a model that is not downloaded never starts its engine */
    [Fact]
    public async Task SelectedModel_NotDownloaded_DoesNotLoadEngine()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var missing = services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();

        vm.SelectedModel = missing;
        await Eventually.UntilAsync(() => vm.SelectedModel == active, "the selection reverts to the active model");

        Assert.DoesNotContain(missing.Id, services.Factory.CreatedModels);
        Assert.Equal(active.Id, services.Provider.CurrentModel);
    }

    /* Without a saved active model, rejecting a model that is not downloaded clears the selector */
    [Fact]
    public async Task SelectedModel_NotDownloadedWithoutActiveModel_ClearsSelection()
    {
        using var services = new GuiServices();
        var missing = services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();

        vm.SelectedModel = missing;

        await Eventually.UntilAsync(() => vm.SelectedModel == null, "the selection is cleared");
    }

    /* Selecting a downloaded model loads it in the engine provider */
    [Fact]
    public async Task SelectedModel_Downloaded_SwitchesEngine()
    {
        using var services = new GuiServices();
        AddActiveModel(services);
        var next = services.AddModel("engine", "next", installed: true);
        var vm = services.CreateViewModel();

        vm.SelectedModel = next;

        await Eventually.UntilAsync(() => services.Provider.CurrentModel == next.Id, "the new model is loaded");
    }

    /* A successful switch saves the new model as the active one */
    [Fact]
    public async Task SelectedModel_Downloaded_SavesActiveModel()
    {
        using var services = new GuiServices();
        AddActiveModel(services);
        var next = services.AddModel("engine", "next", installed: true);
        var vm = services.CreateViewModel();

        vm.SelectedModel = next;

        await Eventually.UntilAsync(() => services.Config.ActiveModel == next.Id, "the new model is saved");
    }

    /* A successful selection clears the "not installed" message left by a previous attempt */
    [Fact]
    public async Task SelectedModel_DownloadedAfterRejection_ClearsStatusMessage()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var missing = services.AddModel("engine", "missing", installed: false);
        var next = services.AddModel("engine", "next", installed: true);
        var vm = services.CreateViewModel();
        vm.SelectedModel = missing;
        await Eventually.UntilAsync(() => vm.SelectedModel == active, "the selection reverts to the active model");

        vm.SelectedModel = next;

        Assert.Null(vm.ModelStatusMessage);
    }

    /* After a switch the voices of the new engine are listed */
    [Fact]
    public async Task SelectedModel_Downloaded_ListsVoicesOfNewEngine()
    {
        using var services = new GuiServices();
        AddActiveModel(services, Voice("old"));
        var next = services.AddModel("engine", "next", installed: true, Voice("n1"), Voice("n2"));
        var vm = services.CreateViewModel();
        await WaitForVoicesAsync(vm, 1);

        vm.SelectedModel = next;

        await Eventually.UntilAsync(() => vm.AvailableVoices.Select(v => v.Id).SequenceEqual(["n1", "n2"]), "the new voices are listed");
    }

    /* After a switch the voice saved for the new model is selected */
    [Fact]
    public async Task SelectedModel_Downloaded_SelectsVoiceSavedForNewModel()
    {
        using var services = new GuiServices();
        AddActiveModel(services, Voice("old"));
        var next = services.AddModel("engine", "next", installed: true, Voice("n1"), Voice("n2"));
        services.Settings.GetEngineConfig(next.Id).VoiceId = "n2";
        var vm = services.CreateViewModel();

        vm.SelectedModel = next;

        await Eventually.UntilAsync(() => vm.SelectedVoice?.Id == "n2", "the saved voice of the new model is selected");
    }

    /* After a switch the speed saved for the new model is shown */
    [Fact]
    public async Task SelectedModel_Downloaded_LoadsSpeedOfNewModel()
    {
        using var services = new GuiServices();
        AddActiveModel(services);
        var next = services.AddModel("engine", "next", installed: true);
        services.Settings.GetEngineConfig(next.Id).Speed = 1.75f;
        var vm = services.CreateViewModel();

        vm.SelectedModel = next;

        await Eventually.UntilAsync(() => vm.SpeechSpeed == 1.75f, "the speed of the new model is shown");
    }

    /* After a switch a non-positive saved speed of the new model is replaced by the normal speed */
    [Fact]
    public async Task SelectedModel_DownloadedWithNonPositiveSpeed_UsesNormalSpeed()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        services.Settings.GetEngineConfig(active.Id).Speed = 1.5f;
        var next = services.AddModel("engine", "next", installed: true);
        services.Settings.GetEngineConfig(next.Id).Speed = -1f;
        var vm = services.CreateViewModel();

        vm.SelectedModel = next;

        await Eventually.UntilAsync(() => services.Provider.CurrentModel == next.Id && vm.SpeechSpeed == 1.0f, "the normal speed is shown");
    }

    /* A failed switch (e.g. the engine worker cannot start) shows the engine's error */
    [Fact]
    public async Task SelectedModel_SwitchFails_ShowsError()
    {
        using var services = new GuiServices();
        AddActiveModel(services);
        var broken = services.AddModel("engine", "broken", installed: true);
        services.Factory.Failures[broken.Id] = "worker crashed";
        var vm = services.CreateViewModel();

        vm.SelectedModel = broken;

        await Eventually.UntilAsync(() => vm.ModelStatusMessage?.Contains("worker crashed") == true, "the error is shown");
    }

    /* A failed switch reverts the selector to the saved active model */
    [Fact]
    public async Task SelectedModel_SwitchFails_RevertsToActiveModel()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var broken = services.AddModel("engine", "broken", installed: true);
        services.Factory.Failures[broken.Id] = "worker crashed";
        var vm = services.CreateViewModel();

        vm.SelectedModel = broken;

        await Eventually.UntilAsync(() => vm.ModelStatusMessage != null && vm.SelectedModel == active, "the selection reverts");
    }

    /* A failed switch keeps the previous active model saved and loaded */
    [Fact]
    public async Task SelectedModel_SwitchFails_KeepsActiveModel()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var broken = services.AddModel("engine", "broken", installed: true);
        services.Factory.Failures[broken.Id] = "worker crashed";
        var vm = services.CreateViewModel();
        int appSaves = services.Repo.AppConfigSaveCount;

        vm.SelectedModel = broken;
        await Eventually.UntilAsync(() => vm.ModelStatusMessage != null && vm.SelectedModel == active, "the selection reverts");

        Assert.Equal((appSaves, active.Id, active.Id),
            (services.Repo.AppConfigSaveCount, services.Config.ActiveModel, services.Provider.CurrentModel));
    }

    /* Re-selecting the model that is already loaded saves without restarting its engine */
    [Fact]
    public void SelectedModel_AlreadyLoaded_SavesWithoutReloading()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var vm = services.CreateViewModel();
        vm.SelectedModel = null;
        int creations = services.Factory.CreatedModels.Count;
        int appSaves = services.Repo.AppConfigSaveCount;

        vm.SelectedModel = active;

        Assert.Equal(creations, services.Factory.CreatedModels.Count);
        Assert.Equal(appSaves + 1, services.Repo.AppConfigSaveCount);
    }

    /* Clearing the selector does nothing */
    [Fact]
    public void SelectedModel_SetToNull_DoesNotSave()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var vm = services.CreateViewModel();
        int appSaves = services.Repo.AppConfigSaveCount;

        vm.SelectedModel = null;

        Assert.Equal((appSaves, active.Id), (services.Repo.AppConfigSaveCount, services.Config.ActiveModel));
    }

    /* Without an engine provider a downloaded model is saved directly */
    [Fact]
    public void SelectedModel_NoEngineProvider_SavesSelection()
    {
        using var services = new GuiServices();
        var model = services.AddModel("engine", "installed", installed: true);
        var vm = new SettingsViewModel(services.Settings, services.Player, services.Windows, services.Downloads);

        vm.SelectedModel = model;

        Assert.Equal(model.Id, services.Config.ActiveModel);
    }

    /* Without a download manager every model is accepted (nothing can tell whether it is installed) */
    [Fact]
    public void SelectedModel_WithoutServices_KeepsSelection()
    {
        using var services = new GuiServices();
        var model = services.AddModel("engine", "missing", installed: false);
        var vm = new PreviewSettingsViewModel();

        vm.SelectedModel = model;

        Assert.Equal((model, (string?)null), (vm.SelectedModel, vm.ModelStatusMessage));
    }

    /* ---------- Voice & speed ---------- */

    /* Picking a voice applies it to the running engine immediately */
    [Fact]
    public async Task SelectedVoice_Changed_SetsEngineActiveVoice()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services, Voice("a"), Voice("b"));
        var vm = services.CreateViewModel();
        await WaitForVoicesAsync(vm, 2);

        vm.SelectedVoice = vm.AvailableVoices[1];

        Assert.Equal("b", services.Engines[active.Id].ActiveVoice);
    }

    /* Picking a voice remembers it for the selected model */
    [Fact]
    public async Task SelectedVoice_Changed_SavesVoiceForModel()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services, Voice("a"), Voice("b"));
        var vm = services.CreateViewModel();
        await WaitForVoicesAsync(vm, 2);

        vm.SelectedVoice = vm.AvailableVoices[1];

        Assert.Equal("b", services.Settings.GetEngineConfig(active.Id).VoiceId);
    }

    /* Clearing the voice leaves the engine's voice alone */
    [Fact]
    public async Task SelectedVoice_SetToNull_KeepsEngineVoice()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services, Voice("a"));
        var vm = services.CreateViewModel();
        await WaitForVoicesAsync(vm, 1);
        services.Engines[active.Id].ActiveVoice = "a";

        vm.SelectedVoice = null;

        Assert.Equal("a", services.Engines[active.Id].ActiveVoice);
    }

    /* Without a selected voice the voice already saved for the model is kept */
    [Fact]
    public void AutoSave_NoVoiceSelected_KeepsSavedVoiceOfModel()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        services.Settings.GetEngineConfig(active.Id).VoiceId = "saved";
        var vm = services.CreateViewModel();

        vm.PrimaryVolume = 10;

        Assert.Equal("saved", services.Settings.GetEngineConfig(active.Id).VoiceId);
    }

    /* Changing the speed applies it to the running engine immediately */
    [Fact]
    public void SpeechSpeed_Changed_SetsEngineSpeed()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var vm = services.CreateViewModel();

        vm.SpeechSpeed = 1.25f;

        Assert.Equal(1.25f, services.Engines[active.Id].Speed);
    }

    /* Changing the speed remembers it for the selected model */
    [Fact]
    public void SpeechSpeed_Changed_SavesSpeedForModel()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services);
        var vm = services.CreateViewModel();

        vm.SpeechSpeed = 0.75f;

        Assert.Equal(0.75f, services.Settings.GetEngineConfig(active.Id).Speed);
    }

    /* Without a selected model nothing model-specific is saved */
    [Fact]
    public void AutoSave_NoSelectedModel_SavesOnlyAppConfig()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.SpeechSpeed = 1.5f;

        Assert.Equal((0, ModelId.Unselected), (services.Repo.EngineConfigSaveCount, services.Config.ActiveModel));
    }

    /* ---------- Auto-save of every setting ---------- */

    /* Turning primary playback off is saved */
    [Fact]
    public void EnablePrimaryPlayback_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.EnablePrimaryPlayback = false;

        Assert.False(services.Config.EnablePrimaryPlayback);
    }

    /* Picking a primary device is saved */
    [Fact]
    public void SelectedPrimaryDevice_Changed_IsSaved()
    {
        using var services = new GuiServices();
        services.Player.Devices.Add(Device("speakers"));
        var vm = services.CreateViewModel();

        vm.SelectedPrimaryDevice = vm.AvailableOutputDevices[1];

        Assert.Equal("speakers", services.Config.PrimaryAudioDeviceId);
    }

    /* Picking a primary device routes the player to it */
    [Fact]
    public void SelectedPrimaryDevice_Changed_RoutesPlayer()
    {
        using var services = new GuiServices();
        services.Player.Devices.Add(Device("speakers"));
        var vm = services.CreateViewModel();

        vm.SelectedPrimaryDevice = vm.AvailableOutputDevices[1];

        Assert.Equal("speakers", services.Player.PrimaryDeviceId);
    }

    /* Clearing the primary device saves the default system device */
    [Fact]
    public void SelectedPrimaryDevice_SetToNull_SavesDefaultDevice()
    {
        using var services = new GuiServices();
        services.Player.Devices.Add(Device("speakers"));
        services.Config.PrimaryAudioDeviceId = "speakers";
        var vm = services.CreateViewModel();

        vm.SelectedPrimaryDevice = null;

        Assert.Equal("-1", services.Config.PrimaryAudioDeviceId);
    }

    /* The primary volume is saved */
    [Fact]
    public void PrimaryVolume_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.PrimaryVolume = 40;

        Assert.Equal(40, services.Config.PrimaryVolume);
    }

    /* The primary volume is applied to the player */
    [Fact]
    public void PrimaryVolume_Changed_SetsPlayerVolume()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.PrimaryVolume = 40;

        Assert.Equal(40, services.Player.PrimaryVolume);
    }

    /* Turning monitor playback on is saved */
    [Fact]
    public void EnableMonitorPlayback_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.EnableMonitorPlayback = true;

        Assert.True(services.Config.EnableMonitorPlayback);
    }

    /* Picking a monitor device is saved */
    [Fact]
    public void SelectedMonitorDevice_Changed_IsSaved()
    {
        using var services = new GuiServices();
        services.Player.Devices.Add(Device("virtual-mic"));
        var vm = services.CreateViewModel();

        vm.SelectedMonitorDevice = vm.AvailableOutputDevices[1];

        Assert.Equal("virtual-mic", services.Config.MonitorAudioDeviceId);
    }

    /* Picking a monitor device routes the player's monitor output to it */
    [Fact]
    public void SelectedMonitorDevice_Changed_RoutesPlayer()
    {
        using var services = new GuiServices();
        services.Player.Devices.Add(Device("virtual-mic"));
        var vm = services.CreateViewModel();

        vm.SelectedMonitorDevice = vm.AvailableOutputDevices[1];

        Assert.Equal("virtual-mic", services.Player.MonitorDeviceId);
    }

    /* Clearing the monitor device saves the default system device */
    [Fact]
    public void SelectedMonitorDevice_SetToNull_SavesDefaultDevice()
    {
        using var services = new GuiServices();
        services.Player.Devices.Add(Device("virtual-mic"));
        services.Config.MonitorAudioDeviceId = "virtual-mic";
        var vm = services.CreateViewModel();

        vm.SelectedMonitorDevice = null;

        Assert.Equal("-1", services.Config.MonitorAudioDeviceId);
    }

    /* The monitor volume is saved */
    [Fact]
    public void MonitorVolume_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.MonitorVolume = 25;

        Assert.Equal(25, services.Config.MonitorVolume);
    }

    /* The monitor volume is applied to the player */
    [Fact]
    public void MonitorVolume_Changed_SetsPlayerVolume()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.MonitorVolume = 25;

        Assert.Equal(25, services.Player.MonitorVolume);
    }

    /* The start-up monitor is saved */
    [Fact]
    public void SelectedMonitorIndex_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.SelectedMonitorIndex = 3;

        Assert.Equal(3, services.Config.DefaultMonitor);
    }

    /* "Start minimized" is saved */
    [Fact]
    public void OpenOnStartup_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.OpenOnStartup = true;

        Assert.True(services.Config.OpenOnStartup);
    }

    /* "Hide when speech begins" is saved */
    [Fact]
    public void HideOnSynth_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.HideOnSynth = true;

        Assert.True(services.Config.HideOnSynth);
    }

    /* "Remember position" is saved */
    [Fact]
    public void RememberPosition_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.RememberPosition = true;

        Assert.True(services.Config.RememberPosition);
    }

    /* "Create virtual microphone on start-up" is saved */
    [Fact]
    public void CreateMicOnStartup_Changed_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.CreateMicOnStartup = true;

        Assert.True(services.Config.CreateMicOnStartup);
    }

    /* A custom models folder is saved without surrounding whitespace */
    [Fact]
    public void CustomModelsDirectory_Changed_SavesTrimmedPath()
    {
        using var services = new GuiServices();
        string folder = services.Temp.CreateSubdirectory("custom");
        var vm = services.CreateViewModel();

        vm.CustomModelsDirectory = $"  {folder}  ";

        Assert.Equal(folder, services.Config.CustomModelsDirectory);
    }

    /* A blank custom models folder is saved as "use the default folder" */
    [Fact]
    public void CustomModelsDirectory_SetToBlank_SavesDefault()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.CustomModelsDirectory = "   ";

        Assert.Null(services.Config.CustomModelsDirectory);
    }

    /* Every change is persisted through the settings manager right away */
    [Fact]
    public void AutoSave_SettingChanged_SavesAppConfigOnce()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        int appSaves = services.Repo.AppConfigSaveCount;

        vm.HideOnSynth = true;

        Assert.Equal(appSaves + 1, services.Repo.AppConfigSaveCount);
    }

    /* ---------- Toggle hotkey ---------- */

    /* A valid toggle hotkey is saved */
    [Fact]
    public void ToggleAppHotkey_Valid_IsSaved()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.ToggleAppHotkey = "Ctrl+Shift+T";

        Assert.Equal("Ctrl+Shift+T", services.Config.ToggleAppHotkey);
    }

    /* A valid toggle hotkey is announced so the global shortcut is re-registered */
    [Fact]
    public void ToggleAppHotkey_Valid_NotifiesHotkeyManagerListeners()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        string? announced = null;
        services.Hotkeys.OnToggleHotkeyChanged += hotkey => announced = hotkey;

        vm.ToggleAppHotkey = "Ctrl+Shift+T";

        Assert.Equal("Ctrl+Shift+T", announced);
    }

    /* A hotkey without a modifier is rejected and the saved one is shown again */
    [Fact]
    public void ToggleAppHotkey_Invalid_RevertsToSavedHotkey()
    {
        using var services = new GuiServices();
        services.Config.ToggleAppHotkey = "Ctrl+Alt+M";
        var vm = services.CreateViewModel();

        vm.ToggleAppHotkey = "T";

        Assert.Equal("Ctrl+Alt+M", vm.ToggleAppHotkey);
    }

    /* A rejected hotkey is not saved */
    [Fact]
    public void ToggleAppHotkey_Invalid_DoesNotSave()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        int appSaves = services.Repo.AppConfigSaveCount;

        vm.ToggleAppHotkey = "T";

        Assert.Equal((appSaves, "Ctrl+Alt+M"), (services.Repo.AppConfigSaveCount, services.Config.ToggleAppHotkey));
    }

    /* Without a settings manager the hotkey is only kept in the view model */
    [Fact]
    public void ToggleAppHotkey_WithoutServices_KeepsValue()
    {
        var vm = new PreviewSettingsViewModel();

        vm.ToggleAppHotkey = "T";

        Assert.Equal("T", vm.ToggleAppHotkey);
    }

    /* ---------- Hotkey recording ---------- */

    /* "Change" starts recording the toggle hotkey */
    [Fact]
    public void ToggleRecordingToggleHotkey_NotRecording_StartsRecording()
    {
        var vm = new PreviewSettingsViewModel();

        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Assert.True(vm.IsRecordingToggleHotkey);
    }

    /* "Cancel" stops recording the toggle hotkey */
    [Fact]
    public void ToggleRecordingToggleHotkey_Recording_StopsRecording()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Assert.False(vm.IsRecordingToggleHotkey);
    }

    /* Starting the toggle recording stops a phrase recording in progress */
    [Fact]
    public void ToggleRecordingToggleHotkey_PhraseRecording_SwitchesRecording()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Assert.Equal((true, false), (vm.IsRecordingToggleHotkey, vm.IsRecordingPhraseHotkey));
    }

    /* The shortcut button starts recording a phrase hotkey */
    [Fact]
    public void ToggleRecordingPhraseHotkey_NotRecording_StartsRecording()
    {
        var vm = new PreviewSettingsViewModel();

        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        Assert.True(vm.IsRecordingPhraseHotkey);
    }

    /* Pressing the shortcut button again stops recording the phrase hotkey */
    [Fact]
    public void ToggleRecordingPhraseHotkey_Recording_StopsRecording()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        Assert.False(vm.IsRecordingPhraseHotkey);
    }

    /* Starting the phrase recording stops a toggle recording in progress */
    [Fact]
    public void ToggleRecordingPhraseHotkey_ToggleRecording_SwitchesRecording()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        Assert.Equal((false, true), (vm.IsRecordingToggleHotkey, vm.IsRecordingPhraseHotkey));
    }

    /* Clearing the new phrase shortcut empties it */
    [Fact]
    public void ClearNewHotkeyKey_Executed_ClearsShortcut()
    {
        var vm = new PreviewSettingsViewModel { NewHotkeyKey = "Ctrl+1" };

        vm.ClearNewHotkeyKeyCommand.Execute(null);

        Assert.Equal(string.Empty, vm.NewHotkeyKey);
    }

    /* Clearing the new phrase shortcut stops a recording in progress */
    [Fact]
    public void ClearNewHotkeyKey_WhileRecording_StopsRecording()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        vm.ClearNewHotkeyKeyCommand.Execute(null);

        Assert.False(vm.IsRecordingPhraseHotkey);
    }

    /* A recorded toggle hotkey becomes the toggle hotkey and is saved */
    [Fact]
    public void ApplyRecordedHotkey_RecordingToggle_AssignsToggleHotkey()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        vm.ApplyRecordedHotkey("Ctrl+Alt+K");

        Assert.Equal(("Ctrl+Alt+K", "Ctrl+Alt+K"), (vm.ToggleAppHotkey, services.Config.ToggleAppHotkey));
    }

    /* Applying a recorded toggle hotkey ends the recording */
    [Fact]
    public void ApplyRecordedHotkey_RecordingToggle_StopsRecording()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        vm.ApplyRecordedHotkey("Ctrl+Alt+K");

        Assert.False(vm.IsRecordingToggleHotkey);
    }

    /* A recorded phrase hotkey fills the new phrase shortcut and ends the recording */
    [Fact]
    public void ApplyRecordedHotkey_RecordingPhrase_FillsNewHotkeyKey()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        vm.ApplyRecordedHotkey("Ctrl+P");

        Assert.Equal(("Ctrl+P", false), (vm.NewHotkeyKey, vm.IsRecordingPhraseHotkey));
    }

    /* A combination arriving while nothing is being recorded is ignored */
    [Fact]
    public void ApplyRecordedHotkey_NotRecording_ChangesNothing()
    {
        var vm = new PreviewSettingsViewModel();

        vm.ApplyRecordedHotkey("Ctrl+P");

        Assert.Equal(("Ctrl+Alt+M", string.Empty), (vm.ToggleAppHotkey, vm.NewHotkeyKey));
    }

    /* Cancelling stops both recordings */
    [Fact]
    public void CancelRecording_BothRecording_StopsBoth()
    {
        var vm = new PreviewSettingsViewModel { IsRecordingToggleHotkey = true, IsRecordingPhraseHotkey = true };

        vm.CancelRecording();

        Assert.Equal((false, false), (vm.IsRecordingToggleHotkey, vm.IsRecordingPhraseHotkey));
    }

    /* While recording the toggle hotkey the button asks for keys */
    [Fact]
    public void ToggleHotkeyDisplayText_Recording_PromptsForKeys()
    {
        var vm = new PreviewSettingsViewModel { IsRecordingToggleHotkey = true };

        Assert.Equal("Press keys... (Esc to cancel)", vm.ToggleHotkeyDisplayText);
    }

    /* Outside recording the button shows the toggle hotkey */
    [Fact]
    public void ToggleHotkeyDisplayText_NotRecording_ShowsHotkey()
    {
        var vm = new PreviewSettingsViewModel { ToggleAppHotkey = "Ctrl+Alt+Q" };

        Assert.Equal("Ctrl+Alt+Q", vm.ToggleHotkeyDisplayText);
    }

    /* Without a toggle hotkey the button shows "None" */
    [Fact]
    public void ToggleHotkeyDisplayText_NoHotkey_ShowsNone()
    {
        var vm = new PreviewSettingsViewModel { ToggleAppHotkey = " " };

        Assert.Equal("None", vm.ToggleHotkeyDisplayText);
    }

    /* While recording a phrase hotkey the button asks for keys */
    [Fact]
    public void PhraseHotkeyDisplayText_Recording_PromptsForKeys()
    {
        var vm = new PreviewSettingsViewModel { IsRecordingPhraseHotkey = true };

        Assert.Equal("Press keys...", vm.PhraseHotkeyDisplayText);
    }

    /* Without a phrase shortcut the button invites to record one */
    [Fact]
    public void PhraseHotkeyDisplayText_NoShortcut_InvitesToRecord()
    {
        var vm = new PreviewSettingsViewModel();

        Assert.Equal("Record shortcut...", vm.PhraseHotkeyDisplayText);
    }

    /* A recorded phrase shortcut is shown on the button */
    [Fact]
    public void PhraseHotkeyDisplayText_WithShortcut_ShowsShortcut()
    {
        var vm = new PreviewSettingsViewModel { NewHotkeyKey = "Ctrl+9" };

        Assert.Equal("Ctrl+9", vm.PhraseHotkeyDisplayText);
    }

    /* While recording the toggle hotkey the side button reads "Cancel" */
    [Fact]
    public void ToggleRecordButtonText_Recording_ReadsCancel()
    {
        var vm = new PreviewSettingsViewModel { IsRecordingToggleHotkey = true };

        Assert.Equal("Cancel", vm.ToggleRecordButtonText);
    }

    /* Outside recording the side button reads "Change" */
    [Fact]
    public void ToggleRecordButtonText_NotRecording_ReadsChange()
    {
        var vm = new PreviewSettingsViewModel();

        Assert.Equal("Change", vm.ToggleRecordButtonText);
    }

    /* Starting a toggle recording refreshes both texts bound to it */
    [Fact]
    public void IsRecordingToggleHotkey_Changed_NotifiesDisplayTexts()
    {
        var vm = new PreviewSettingsViewModel();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsRecordingToggleHotkey = true;

        Assert.Contains(nameof(SettingsViewModel.ToggleHotkeyDisplayText), changed);
        Assert.Contains(nameof(SettingsViewModel.ToggleRecordButtonText), changed);
    }

    /* Changing the toggle hotkey refreshes its display text */
    [Fact]
    public void ToggleAppHotkey_Changed_NotifiesDisplayText()
    {
        var vm = new PreviewSettingsViewModel();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ToggleAppHotkey = "Ctrl+Alt+Q";

        Assert.Contains(nameof(SettingsViewModel.ToggleHotkeyDisplayText), changed);
    }

    /* Changing the phrase shortcut refreshes its display text */
    [Fact]
    public void NewHotkeyKey_Changed_NotifiesDisplayText()
    {
        var vm = new PreviewSettingsViewModel();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.NewHotkeyKey = "Ctrl+1";

        Assert.Contains(nameof(SettingsViewModel.PhraseHotkeyDisplayText), changed);
    }

    /* Starting a phrase recording refreshes its display text */
    [Fact]
    public void IsRecordingPhraseHotkey_Changed_NotifiesDisplayText()
    {
        var vm = new PreviewSettingsViewModel();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsRecordingPhraseHotkey = true;

        Assert.Contains(nameof(SettingsViewModel.PhraseHotkeyDisplayText), changed);
    }

    /* ---------- Views & tabs ---------- */

    /* The expand button switches to the Advanced view */
    [Fact]
    public void SwitchToAdvanced_Executed_ShowsAdvancedView()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.SwitchToAdvancedCommand.Execute(null);

        Assert.True(vm.IsAdvancedView);
    }

    /* Switching to the Advanced view reloads the settings */
    [Fact]
    public void SwitchToAdvanced_Executed_ReloadsSettings()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        services.Config.MonitorVolume = 7;

        vm.SwitchToAdvancedCommand.Execute(null);

        Assert.Equal(7, vm.MonitorVolume);
    }

    /* The back button switches to the Quick view */
    [Fact]
    public void SwitchToQuick_Executed_ShowsQuickView()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.SwitchToAdvancedCommand.Execute(null);

        vm.SwitchToQuickCommand.Execute(null);

        Assert.False(vm.IsAdvancedView);
    }

    /* Switching to the Quick view reloads the settings */
    [Fact]
    public void SwitchToQuick_Executed_ReloadsSettings()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        services.Config.PrimaryVolume = 9;

        vm.SwitchToQuickCommand.Execute(null);

        Assert.Equal(9, vm.PrimaryVolume);
    }

    /* The sidebar selects the clicked tab */
    [Theory]
    [InlineData(AdvancedTab.General)]
    [InlineData(AdvancedTab.Audio)]
    [InlineData(AdvancedTab.Dictionary)]
    [InlineData(AdvancedTab.Engine)]
    [InlineData(AdvancedTab.EngineManagement)]
    [InlineData(AdvancedTab.Hotkeys)]
    [InlineData(AdvancedTab.History)]
    public void SelectTab_Executed_SelectsTab(AdvancedTab tab)
    {
        var vm = new PreviewSettingsViewModel { SelectedTab = AdvancedTab.Audio == tab ? AdvancedTab.General : AdvancedTab.Audio };

        vm.SelectTabCommand.Execute(tab);

        Assert.Equal(tab, vm.SelectedTab);
    }

    /* ---------- Dictionary ---------- */

    /* A new dictionary entry is added with trimmed words and its case-sensitivity */
    [Fact]
    public void AddDictionaryEntry_Valid_AddsTrimmedEntry()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewDictOriginal = "  GPU ";
        vm.NewDictSpelling = " gee pee you ";
        vm.NewDictCaseSensitive = true;

        vm.AddDictionaryEntryCommand.Execute(null);

        var entry = Assert.Single(vm.DictionaryEntries);
        Assert.Equal(("GPU", "gee pee you", true), (entry.Original, entry.Spelling, entry.CaseSensitive));
    }

    /* After adding an entry the input boxes are cleared */
    [Fact]
    public void AddDictionaryEntry_Valid_ClearsInputs()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewDictOriginal = "GPU";
        vm.NewDictSpelling = "gee pee you";

        vm.AddDictionaryEntryCommand.Execute(null);

        Assert.Equal((string.Empty, string.Empty), (vm.NewDictOriginal, vm.NewDictSpelling));
    }

    /* An entry without the original word is not added */
    [Fact]
    public void AddDictionaryEntry_BlankOriginal_AddsNothing()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewDictOriginal = " ";
        vm.NewDictSpelling = "gee pee you";

        vm.AddDictionaryEntryCommand.Execute(null);

        Assert.Equal(0, services.Repo.DictionarySaveCount);
    }

    /* An entry without the spelling is not added */
    [Fact]
    public void AddDictionaryEntry_BlankSpelling_AddsNothing()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewDictOriginal = "GPU";
        vm.NewDictSpelling = "";

        vm.AddDictionaryEntryCommand.Execute(null);

        Assert.Equal(0, services.Repo.DictionarySaveCount);
    }

    /* A rejected entry (duplicate word) keeps the inputs so the user can fix them */
    [Fact]
    public void AddDictionaryEntry_Duplicate_KeepsInputs()
    {
        using var services = new GuiServices();
        services.Dictionary.AddEntry("GPU", "first");
        var vm = services.CreateViewModel();
        vm.NewDictOriginal = "gpu";
        vm.NewDictSpelling = "second";

        vm.AddDictionaryEntryCommand.Execute(null);

        Assert.Equal(("gpu", "second", 1), (vm.NewDictOriginal, vm.NewDictSpelling, vm.DictionaryEntries.Count));
    }

    /* Without a dictionary manager nothing happens */
    [Fact]
    public void AddDictionaryEntry_WithoutDictionaryManager_KeepsInputs()
    {
        var vm = new PreviewSettingsViewModel { NewDictOriginal = "GPU", NewDictSpelling = "gee pee you" };

        vm.AddDictionaryEntryCommand.Execute(null);

        Assert.Equal(("GPU", 0), (vm.NewDictOriginal, vm.DictionaryEntries.Count));
    }

    /* Deleting an entry removes it from the dictionary and the list */
    [Fact]
    public void DeleteDictionaryEntry_Existing_RemovesEntry()
    {
        using var services = new GuiServices();
        services.Dictionary.AddEntry("GPU", "gee pee you");
        services.Dictionary.AddEntry("CPU", "see pee you");
        var vm = services.CreateViewModel();
        var gpu = vm.DictionaryEntries.Single(e => e.Original == "GPU");

        vm.DeleteDictionaryEntryCommand.Execute(gpu.Id);

        Assert.Equal(["CPU"], vm.DictionaryEntries.Select(e => e.Original));
        Assert.Equal(["CPU"], services.Dictionary.GetEntries().Select(e => e.Original));
    }

    /* Deleting an unknown entry changes nothing */
    [Fact]
    public void DeleteDictionaryEntry_Unknown_ChangesNothing()
    {
        using var services = new GuiServices();
        services.Dictionary.AddEntry("GPU", "gee pee you");
        var vm = services.CreateViewModel();
        int saves = services.Repo.DictionarySaveCount;

        vm.DeleteDictionaryEntryCommand.Execute(Guid.NewGuid());

        Assert.Equal((saves, 1), (services.Repo.DictionarySaveCount, vm.DictionaryEntries.Count));
    }

    /* Without a dictionary manager deleting does nothing */
    [Fact]
    public void DeleteDictionaryEntry_WithoutDictionaryManager_DoesNotThrow()
    {
        var vm = new PreviewSettingsViewModel();

        var error = Record.Exception(() => vm.DeleteDictionaryEntryCommand.Execute(Guid.NewGuid()));

        Assert.Null(error);
    }

    /* ---------- Phrase hotkeys ---------- */

    /* Adding a phrase pre-renders it through the speech pipeline without playing it */
    [Fact]
    public async Task AddHotkeyPhrase_Valid_PreRendersPhraseSilently()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewHotkeyKey = "Ctrl+1";
        vm.NewHotkeyPhrase = "  hello  ";

        await vm.AddHotkeyPhraseCommand.ExecuteAsync(null);

        var (text, options) = Assert.Single(services.Pipeline.Requests);
        Assert.Equal(("hello", false, false), (text, options!.EnablePlayback, options.RecordHistory));
    }

    /* Adding a phrase lists the new hotkey */
    [Fact]
    public async Task AddHotkeyPhrase_Valid_ListsHotkey()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewHotkeyKey = "Ctrl+1";
        vm.NewHotkeyPhrase = "hello";

        await vm.AddHotkeyPhraseCommand.ExecuteAsync(null);

        var entry = Assert.Single(vm.HotkeyEntries);
        Assert.Equal(("Ctrl+1", "hello"), (entry.Hotkey, entry.Text));
    }

    /* After adding a phrase the inputs are cleared */
    [Fact]
    public async Task AddHotkeyPhrase_Valid_ClearsInputs()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewHotkeyKey = "Ctrl+1";
        vm.NewHotkeyPhrase = "hello";

        await vm.AddHotkeyPhraseCommand.ExecuteAsync(null);

        Assert.Equal((string.Empty, string.Empty), (vm.NewHotkeyKey, vm.NewHotkeyPhrase));
    }

    /* A phrase without a shortcut is not added */
    [Fact]
    public async Task AddHotkeyPhrase_NoShortcut_AddsNothing()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewHotkeyPhrase = "hello";

        await vm.AddHotkeyPhraseCommand.ExecuteAsync(null);

        Assert.Empty(services.Pipeline.Requests);
    }

    /* A shortcut without a phrase is not added */
    [Fact]
    public async Task AddHotkeyPhrase_NoPhrase_AddsNothing()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        vm.NewHotkeyKey = "Ctrl+1";
        vm.NewHotkeyPhrase = "  ";

        await vm.AddHotkeyPhraseCommand.ExecuteAsync(null);

        Assert.Empty(services.Pipeline.Requests);
    }

    /* A phrase that cannot be synthesized is not added and the inputs are kept */
    [Fact]
    public async Task AddHotkeyPhrase_SynthesisFails_KeepsInputs()
    {
        using var services = new GuiServices();
        services.Pipeline.ResultToReturn = Result<SpeechResult>.Failure("engine missing");
        var vm = services.CreateViewModel();
        vm.NewHotkeyKey = "Ctrl+1";
        vm.NewHotkeyPhrase = "hello";

        await vm.AddHotkeyPhraseCommand.ExecuteAsync(null);

        Assert.Equal(("Ctrl+1", "hello", 0), (vm.NewHotkeyKey, vm.NewHotkeyPhrase, vm.HotkeyEntries.Count));
    }

    /* Without a hotkey manager nothing happens */
    [Fact]
    public async Task AddHotkeyPhrase_WithoutHotkeyManager_KeepsInputs()
    {
        var vm = new PreviewSettingsViewModel { NewHotkeyKey = "Ctrl+1", NewHotkeyPhrase = "hello" };

        await vm.AddHotkeyPhraseCommand.ExecuteAsync(null);

        Assert.Equal("Ctrl+1", vm.NewHotkeyKey);
    }

    /* Deleting a phrase hotkey removes it */
    [Fact]
    public async Task DeleteHotkeyPhrase_Existing_RemovesHotkey()
    {
        using var services = new GuiServices();
        await services.Hotkeys.AssignHotkeyAsync("Ctrl+1", "one");
        await services.Hotkeys.AssignHotkeyAsync("Ctrl+2", "two");
        var vm = services.CreateViewModel();
        var first = vm.HotkeyEntries.Single(e => e.Hotkey == "Ctrl+1");

        vm.DeleteHotkeyPhraseCommand.Execute(first.Id);

        Assert.Equal(["Ctrl+2"], vm.HotkeyEntries.Select(e => e.Hotkey));
        Assert.Equal(["Ctrl+2"], services.Hotkeys.GetHotkeys().Select(e => e.Hotkey));
    }

    /* Deleting an unknown phrase hotkey changes nothing */
    [Fact]
    public async Task DeleteHotkeyPhrase_Unknown_ChangesNothing()
    {
        using var services = new GuiServices();
        await services.Hotkeys.AssignHotkeyAsync("Ctrl+1", "one");
        var vm = services.CreateViewModel();
        int saves = services.Repo.HotkeysSaveCount;

        vm.DeleteHotkeyPhraseCommand.Execute(Guid.NewGuid());

        Assert.Equal((saves, 1), (services.Repo.HotkeysSaveCount, vm.HotkeyEntries.Count));
    }

    /* Without a hotkey manager deleting does nothing */
    [Fact]
    public void DeleteHotkeyPhrase_WithoutHotkeyManager_DoesNotThrow()
    {
        var vm = new PreviewSettingsViewModel();

        var error = Record.Exception(() => vm.DeleteHotkeyPhraseCommand.Execute(Guid.NewGuid()));

        Assert.Null(error);
    }

    /* ---------- History ---------- */

    /* The play button replays the recorded audio of the entry */
    [Fact]
    public async Task PlayHistoryItem_Existing_PlaysRecordedAudio()
    {
        using var services = new GuiServices();
        var entry = await services.AddHistoryEntryAsync("spoken", [1, 2, 3, 4], Format);
        var vm = services.CreateViewModel();

        await vm.PlayHistoryItemCommand.ExecuteAsync(entry.Id);

        var (pcm, format) = Assert.Single(services.Player.PlayedBuffers);
        Assert.Equal([1, 2, 3, 4], pcm);
        Assert.Equal(Format, format);
    }

    /* Without a history manager playing does nothing */
    [Fact]
    public async Task PlayHistoryItem_WithoutHistoryManager_DoesNotThrow()
    {
        var vm = new PreviewSettingsViewModel();

        var error = await Record.ExceptionAsync(() => vm.PlayHistoryItemCommand.ExecuteAsync(Guid.NewGuid()));

        Assert.Null(error);
    }

    /* "Clear All" empties the history and its list */
    [Fact]
    public async Task ClearHistory_Executed_RemovesEveryEntry()
    {
        using var services = new GuiServices();
        await services.AddHistoryEntryAsync("one", [1], Format);
        await services.AddHistoryEntryAsync("two", [2], Format);
        var vm = services.CreateViewModel();

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Empty(vm.HistoryEntries);
        Assert.Empty(services.History.GetHistory());
    }

    /* Without a history manager clearing does nothing */
    [Fact]
    public async Task ClearHistory_WithoutHistoryManager_DoesNotThrow()
    {
        var vm = new PreviewSettingsViewModel();

        var error = await Record.ExceptionAsync(() => vm.ClearHistoryCommand.ExecuteAsync(null));

        Assert.Null(error);
    }

    /* Deleting a history entry removes only that entry */
    [Fact]
    public async Task DeleteHistoryItem_Existing_RemovesEntry()
    {
        using var services = new GuiServices();
        var first = await services.AddHistoryEntryAsync("one", [1], Format);
        var second = await services.AddHistoryEntryAsync("two", [2], Format);
        var vm = services.CreateViewModel();

        await vm.DeleteHistoryItemCommand.ExecuteAsync(first.Id);

        Assert.Equal([second.Id], vm.HistoryEntries.Select(e => e.Id));
    }

    /* Deleting an unknown history entry changes nothing */
    [Fact]
    public async Task DeleteHistoryItem_Unknown_ChangesNothing()
    {
        using var services = new GuiServices();
        await services.AddHistoryEntryAsync("one", [1], Format);
        var vm = services.CreateViewModel();
        int saves = services.Repo.HistorySaveCount;

        await vm.DeleteHistoryItemCommand.ExecuteAsync(Guid.NewGuid());

        Assert.Equal((saves, 1), (services.Repo.HistorySaveCount, vm.HistoryEntries.Count));
    }

    /* Without a history manager deleting does nothing */
    [Fact]
    public async Task DeleteHistoryItem_WithoutHistoryManager_DoesNotThrow()
    {
        var vm = new PreviewSettingsViewModel();

        var error = await Record.ExceptionAsync(() => vm.DeleteHistoryItemCommand.ExecuteAsync(Guid.NewGuid()));

        Assert.Null(error);
    }

    /* ---------- Engine management ---------- */

    /* A successful download marks the model as installed */
    [Fact]
    public async Task DownloadModel_Succeeds_MarksModelDownloaded()
    {
        using var services = new GuiServices();
        var model = services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();
        var item = vm.ModelStatuses.Single();

        await vm.DownloadModelCommand.ExecuteAsync(item);

        Assert.Equal((true, false), (item.IsDownloaded, item.IsDownloading));
        Assert.Equal(model, Assert.Single(services.Downloader.Downloads).Model);
    }

    /* The download goes into the engine's models folder */
    [Fact]
    public async Task DownloadModel_Executed_DownloadsIntoEngineFolder()
    {
        using var services = new GuiServices();
        var model = services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();

        await vm.DownloadModelCommand.ExecuteAsync(vm.ModelStatuses.Single());

        Assert.Equal(services.Settings.GetModelDirectory(model.Id), Assert.Single(services.Downloader.Downloads).Destination);
    }

    /* The downloader's 0-100 percentage is shown as is on the progress bar (Maximum 100) */
    [Fact]
    public async Task DownloadModel_ProgressReported_ShowsPercentage()
    {
        var downloader = new GatedModelDownloader();
        using var services = new GuiServices(downloader);
        services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();
        var item = vm.ModelStatuses.Single();
        var download = vm.DownloadModelCommand.ExecuteAsync(item);

        downloader.ReportProgress(42);

        await Eventually.UntilAsync(() => item.Progress != 0, "the progress is reported");
        Assert.Equal(42, item.Progress);
        downloader.Complete(Result.Success());
        await download;
    }

    /* While the download runs the row shows it as in progress */
    [Fact]
    public async Task DownloadModel_InProgress_MarksRowDownloading()
    {
        var downloader = new GatedModelDownloader();
        using var services = new GuiServices(downloader);
        services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();
        var item = vm.ModelStatuses.Single();

        var download = vm.DownloadModelCommand.ExecuteAsync(item);

        Assert.True(item.IsDownloading);
        downloader.Complete(Result.Success());
        await download;
    }

    /* A second click while the model is downloading starts no second download */
    [Fact]
    public async Task DownloadModel_AlreadyDownloading_IsIgnored()
    {
        var downloader = new GatedModelDownloader();
        using var services = new GuiServices(downloader);
        services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();
        var item = vm.ModelStatuses.Single();
        var download = vm.DownloadModelCommand.ExecuteAsync(item);

        await vm.DownloadModelCommand.ExecuteAsync(item);

        Assert.Single(downloader.Requests);
        downloader.Complete(Result.Success());
        await download;
    }

    /* A failed download leaves the model not installed */
    [Fact]
    public async Task DownloadModel_Fails_LeavesModelNotDownloaded()
    {
        using var services = new GuiServices();
        services.Downloader.ResultToReturn = Result.Failure("network down");
        services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();
        var item = vm.ModelStatuses.Single();

        await vm.DownloadModelCommand.ExecuteAsync(item);

        Assert.Equal((false, false), (item.IsDownloaded, item.IsDownloading));
    }

    /* A download that throws (e.g. the models folder cannot be created) marks the model as not installed */
    [Fact]
    public async Task DownloadModel_Throws_MarksModelNotDownloaded()
    {
        using var services = new GuiServices();
        services.AddModel("engine", "installed", installed: true);
        var vm = services.CreateViewModel();
        var item = vm.ModelStatuses.Single();
        services.Config.CustomModelsDirectory = services.Temp.WriteFile("not-a-folder", "file");

        await vm.DownloadModelCommand.ExecuteAsync(item);

        Assert.Equal((false, false), (item.IsDownloaded, item.IsDownloading));
    }

    /* A successful download refreshes the voice list from the engine */
    [Fact]
    public async Task DownloadModel_Succeeds_RefreshesVoices()
    {
        using var services = new GuiServices();
        var active = AddActiveModel(services, Voice("a"));
        services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();
        await WaitForVoicesAsync(vm, 1);
        services.Engines[active.Id].Voices.Add(Voice("b"));

        await vm.DownloadModelCommand.ExecuteAsync(vm.ModelStatuses.Single(s => s.Name == "engine missing"));

        await WaitForVoicesAsync(vm, 2);
    }

    /* Without a row nothing is downloaded */
    [Fact]
    public async Task DownloadModel_NullItem_DownloadsNothing()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        await vm.DownloadModelCommand.ExecuteAsync(null);

        Assert.Empty(services.Downloader.Downloads);
    }

    /* Deleting an installed model removes its files */
    [Fact]
    public void DeleteModel_Installed_RemovesFiles()
    {
        using var services = new GuiServices();
        var model = services.AddModel("engine", "installed", installed: true);
        var vm = services.CreateViewModel();

        vm.DeleteModelCommand.Execute(vm.ModelStatuses.Single());

        Assert.False(services.Downloads.IsModelDownloaded(model.Id));
    }

    /* Deleting an installed model resets its row */
    [Fact]
    public void DeleteModel_Installed_ResetsRow()
    {
        using var services = new GuiServices();
        services.AddModel("engine", "installed", installed: true);
        var vm = services.CreateViewModel();
        var item = vm.ModelStatuses.Single();
        item.Progress = 100;

        vm.DeleteModelCommand.Execute(item);

        Assert.Equal((false, 0.0), (item.IsDownloaded, item.Progress));
    }

    /* A failed delete (no files on disk) leaves the row untouched */
    [Fact]
    public void DeleteModel_NoFiles_LeavesRowUntouched()
    {
        using var services = new GuiServices();
        services.AddModel("engine", "missing", installed: false);
        var vm = services.CreateViewModel();
        var item = vm.ModelStatuses.Single();
        item.IsDownloaded = true;
        item.Progress = 50;

        vm.DeleteModelCommand.Execute(item);

        Assert.Equal((true, 50.0), (item.IsDownloaded, item.Progress));
    }

    /* Without a row nothing is deleted */
    [Fact]
    public void DeleteModel_NullItem_DoesNotThrow()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        var error = Record.Exception(() => vm.DeleteModelCommand.Execute(null));

        Assert.Null(error);
    }

    /* ---------- Window commands ---------- */

    /* The close button hides the settings window */
    [Fact]
    public void Close_Executed_HidesSettingsWindow()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.CloseCommand.Execute(null);

        Assert.Equal(1, services.Windows.HideSettingsWindowCount);
    }

    /* Outside hotkey recording the window can be closed */
    [Fact]
    public void Close_NotRecording_CanExecute()
    {
        var vm = new PreviewSettingsViewModel();

        Assert.True(vm.CloseCommand.CanExecute(null));
    }

    /* While recording the toggle hotkey, closing is disabled so Escape cancels the recording instead */
    [Fact]
    public void Close_RecordingToggleHotkey_CannotExecute()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Assert.False(vm.CloseCommand.CanExecute(null));
    }

    /* While recording a phrase hotkey, closing is disabled so Escape cancels the recording instead */
    [Fact]
    public void Close_RecordingPhraseHotkey_CannotExecute()
    {
        var vm = new PreviewSettingsViewModel();
        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        Assert.False(vm.CloseCommand.CanExecute(null));
    }

    /* Starting or ending a recording re-evaluates the close command (and the Escape key binding using it) */
    [Fact]
    public void IsRecordingToggleHotkey_Changed_RaisesCloseCanExecuteChanged()
    {
        var vm = new PreviewSettingsViewModel();
        int raised = 0;
        vm.CloseCommand.CanExecuteChanged += (_, _) => raised++;

        vm.IsRecordingToggleHotkey = true;

        Assert.Equal(1, raised);
    }

    /* Starting or ending a phrase recording re-evaluates the close command */
    [Fact]
    public void IsRecordingPhraseHotkey_Changed_RaisesCloseCanExecuteChanged()
    {
        var vm = new PreviewSettingsViewModel();
        int raised = 0;
        vm.CloseCommand.CanExecuteChanged += (_, _) => raised++;

        vm.IsRecordingPhraseHotkey = true;

        Assert.Equal(1, raised);
    }

    /* "Exit Moon-TTS" exits the application */
    [Fact]
    public void ExitApp_Executed_ExitsApplication()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();

        vm.ExitAppCommand.Execute(null);

        Assert.Equal(1, services.Windows.ExitApplicationCount);
    }

    /* Without a window service the window commands do nothing */
    [Fact]
    public void WindowCommands_WithoutWindowService_DoNotThrow()
    {
        var vm = new PreviewSettingsViewModel();

        var error = Record.Exception(() =>
        {
            vm.CloseCommand.Execute(null);
            vm.ExitAppCommand.Execute(null);
        });

        Assert.Null(error);
    }
}
