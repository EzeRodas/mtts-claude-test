using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoonTTS.Avalonia.Services;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Avalonia.ViewModels;

/* File Structure:
        - AdvancedTab (Enum)
        - ModelStatusItem (Class)
        - SettingsViewModel(SettingsManager, IAudioPlayer, IWindowService, DownloadManager, DictionaryManager?, HotkeyManager?, HistoryManager?, TtsEngineProvider?)
        - LoadAudioDevices() : void
        - LoadTtsEngines() : void
        - LoadActiveVoices() : void
        - LoadSettings() : void
        - LoadModelStatuses() : void
        - AutoSave() : void
        - SwitchToAdvanced() : void
        - SwitchToQuick() : void
        - SelectTab(AdvancedTab) : void
        - AddDictionaryEntry() : void
        - DeleteDictionaryEntry(Guid) : void
        - AddHotkeyPhraseAsync() : Task
        - DeleteHotkeyPhrase(Guid) : void
        - PlayHistoryItemAsync(Guid) : Task
        - ClearHistory() : void
        - DeleteHistoryItem(Guid) : void
        - DownloadModelAsync(ModelStatusItem) : Task
        - DeleteModel(ModelStatusItem) : void
        - Close() : void
        - ExitApp() : void
*/

public enum AdvancedTab
{
    General,
    Audio,
    Dictionary,
    Engine,
    EngineManagement,
    Hotkeys,
    History,
}

public partial class ModelStatusItem : ObservableObject
{
    [ObservableProperty]
    private TtsModelType _modelType;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _progress;
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsManager? _settingsManager;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly IWindowService? _windowService;
    private readonly DownloadManager? _downloadManager;
    private readonly DictionaryManager? _dictManager;
    private readonly HotkeyManager? _hotkeyManager;
    private readonly HistoryManager? _historyManager;
    private readonly ITtsEngineProvider? _engineProvider;

    private bool _isLoading;

    /* View Mode & Active Tab */
    [ObservableProperty]
    private bool _isAdvancedView;

    [ObservableProperty]
    private AdvancedTab _selectedTab = AdvancedTab.General;

    /* Quick & Audio Settings */
    [ObservableProperty]
    private TtsModelType _selectedModel = TtsModelType.Kokoro;

    [ObservableProperty]
    private string _selectedVoice = string.Empty;

    [ObservableProperty]
    private bool _enablePrimaryPlayback = true;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedPrimaryDevice;

    [ObservableProperty]
    private int _primaryVolume = 100;

    [ObservableProperty]
    private bool _enableMonitorPlayback;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedMonitorDevice;

    [ObservableProperty]
    private int _monitorVolume = 100;

    [ObservableProperty]
    private float _speechSpeed = 1.0f;

    /* General Settings */
    [ObservableProperty]
    private int _selectedMonitorIndex;

    [ObservableProperty]
    private bool _openOnStartup;

    [ObservableProperty]
    private bool _hideOnSynth;

    [ObservableProperty]
    private bool _rememberPosition;

    [ObservableProperty]
    private bool _createMicOnStartup;

    [ObservableProperty]
    private string _customModelsDirectory = string.Empty;

    /* Hotkey Settings */
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleHotkeyDisplayText))]
    private string _toggleAppHotkey = "Ctrl+Alt+M";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhraseHotkeyDisplayText))]
    private string _newHotkeyKey = string.Empty;

    [ObservableProperty]
    private string _newHotkeyPhrase = string.Empty;

    /* Hotkey Recording State */
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleHotkeyDisplayText))]
    [NotifyPropertyChangedFor(nameof(ToggleRecordButtonText))]
    private bool _isRecordingToggleHotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhraseHotkeyDisplayText))]
    private bool _isRecordingPhraseHotkey;

    /* Dictionary Composer */
    [ObservableProperty]
    private string _newDictOriginal = string.Empty;

    [ObservableProperty]
    private string _newDictSpelling = string.Empty;

    [ObservableProperty]
    private bool _newDictCaseSensitive;

    /* Collections */
    public ObservableCollection<TtsModelType> AvailableModels { get; } = new();
    public ObservableCollection<string> AvailableVoices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> AvailableOutputDevices { get; } = new();
    public ObservableCollection<DictionaryEntry> DictionaryEntries { get; } = new();
    public ObservableCollection<HotkeyEntry> HotkeyEntries { get; } = new();
    public ObservableCollection<HistoryEntry> HistoryEntries { get; } = new();
    public ObservableCollection<ModelStatusItem> ModelStatuses { get; } = new();

    /* Constructor for previewer */
    protected SettingsViewModel()
        : this(null!, null!, null!, null!, null!, null!, null!, null!) { }

    /* Main Constructor */
    public SettingsViewModel(
        SettingsManager settingsManager,
        IAudioPlayer audioPlayer,
        IWindowService windowService,
        DownloadManager downloadManager,
        DictionaryManager? dictManager = null,
        HotkeyManager? hotkeyManager = null,
        HistoryManager? historyManager = null,
        ITtsEngineProvider? engineProvider = null
    )
    {
        _settingsManager = settingsManager;
        _audioPlayer = audioPlayer;
        _windowService = windowService;
        _downloadManager = downloadManager;
        _dictManager = dictManager;
        _hotkeyManager = hotkeyManager;
        _historyManager = historyManager;
        _engineProvider = engineProvider;

        if (_settingsManager != null && _audioPlayer != null)
        {
            LoadAudioDevices();
            LoadTtsEngines();
            LoadActiveVoices();
            LoadModelStatuses();
            LoadSettings();
        }
    }

    public string ToggleHotkeyDisplayText =>
        IsRecordingToggleHotkey
            ? "Press keys... (Esc to cancel)"
            : (string.IsNullOrWhiteSpace(ToggleAppHotkey) ? "None" : ToggleAppHotkey);

    public string PhraseHotkeyDisplayText =>
        IsRecordingPhraseHotkey
            ? "Press keys..."
            : (string.IsNullOrWhiteSpace(NewHotkeyKey) ? "Record shortcut..." : NewHotkeyKey);

    public string ToggleRecordButtonText => IsRecordingToggleHotkey ? "Cancel" : "Change";

    /* Populates audio outputs from platform sink enumerator */
    private void LoadAudioDevices()
    {
        AvailableOutputDevices.Clear();

        if (_audioPlayer != null)
        {
            var result = _audioPlayer.GetAvailableDevices();
            if (result.IsSuccess && result.Value != null)
            {
                foreach (var dev in result.Value)
                {
                    if (AvailableOutputDevices.All(d => d.DeviceId != dev.DeviceId))
                        AvailableOutputDevices.Add(dev);
                }
            }
        }

        if (AvailableOutputDevices.All(d => d.DeviceId != "-1"))
        {
            AvailableOutputDevices.Insert(0, new AudioDeviceInfo
            {
                DeviceId = "-1",
                DeviceName = "Default System Device",
            });
        }
    }

    /* Populates all supported TTS models */
    private void LoadTtsEngines()
    {
        AvailableModels.Clear();
        AvailableModels.Add(TtsModelType.Kokoro);
        AvailableModels.Add(TtsModelType.Kokoro_fp16);
        AvailableModels.Add(TtsModelType.Kokoro_zh_fp16);
    }

    /* Populates active voices with reliable fallback to KokoroVoiceId enum */
    private void LoadActiveVoices()
    {
        AvailableVoices.Clear();
        var engineVoices = _engineProvider?.CurrentEngine?.ListVoices();
        if (engineVoices != null && engineVoices.Count > 0)
        {
            foreach (var voice in engineVoices)
                AvailableVoices.Add(voice);
        }
        else
        {
            foreach (var voice in Enum.GetValues<KokoroVoiceId>())
            {
                if (voice != KokoroVoiceId.Unselected)
                    AvailableVoices.Add(voice.ToString());
            }
        }
    }

    /* Populates model download statuses */
    private void LoadModelStatuses()
    {
        ModelStatuses.Clear();
        if (_downloadManager == null)
            return;

        foreach (var model in _downloadManager.GetAvailableModels())
        {
            ModelStatuses.Add(
                new ModelStatusItem
                {
                    ModelType = model.ModelType,
                    Name = model.DisplayName,
                    IsDownloaded = _downloadManager.IsModelDownloaded(model.ModelType),
                    Progress = 0,
                }
            );
        }
    }

    /* Loads all configuration values and binds selection items */
    public void LoadSettings()
    {
        if (_settingsManager == null)
            return;

        _isLoading = true;
        try
        {
            var appConfig = _settingsManager.CurrentAppConfig;
            var activeModel =
                appConfig.ActiveModel != TtsModelType.Unselected
                    ? appConfig.ActiveModel
                    : TtsModelType.Kokoro;
            var engineConfig = _settingsManager.GetEngineConfig<KokoroEngineConfig>(activeModel);

            SelectedModel =
                appConfig.ActiveModel != TtsModelType.Unselected
                    ? appConfig.ActiveModel
                    : TtsModelType.Kokoro;
            SelectedVoice =
                string.IsNullOrEmpty(engineConfig.VoiceId)
                || engineConfig.VoiceId == KokoroVoiceId.Unselected.ToString()
                    ? KokoroVoiceId.af_heart.ToString()
                    : engineConfig.VoiceId;

            EnablePrimaryPlayback = appConfig.EnablePrimaryPlayback;
            SelectedPrimaryDevice =
                AvailableOutputDevices.FirstOrDefault(d =>
                    d.DeviceId == appConfig.PrimaryAudioDeviceId
                ) ?? AvailableOutputDevices.FirstOrDefault();
            PrimaryVolume = appConfig.PrimaryVolume;

            EnableMonitorPlayback = appConfig.EnableMonitorPlayback;
            SelectedMonitorDevice =
                AvailableOutputDevices.FirstOrDefault(d =>
                    d.DeviceId == appConfig.MonitorAudioDeviceId
                ) ?? AvailableOutputDevices.FirstOrDefault(d => d.DeviceId == "-1");
            MonitorVolume = appConfig.MonitorVolume;

            SpeechSpeed = engineConfig.Speed > 0 ? engineConfig.Speed : 1.0f;
            SelectedMonitorIndex = appConfig.DefaultMonitor;
            OpenOnStartup = appConfig.OpenOnStartup;
            HideOnSynth = appConfig.HideOnSynth;
            RememberPosition = appConfig.RememberPosition;
            CreateMicOnStartup = appConfig.CreateMicOnStartup;
            ToggleAppHotkey = appConfig.ToggleAppHotkey;
            CustomModelsDirectory = appConfig.CustomModelsDirectory ?? string.Empty;

            CancelRecording();

            if (_dictManager != null)
            {
                DictionaryEntries.Clear();
                foreach (var entry in _dictManager.GetEntries())
                    DictionaryEntries.Add(entry);
            }

            if (_hotkeyManager != null)
            {
                HotkeyEntries.Clear();
                foreach (var hk in _hotkeyManager.GetHotkeys())
                    HotkeyEntries.Add(hk);
            }

            if (_historyManager != null)
            {
                HistoryEntries.Clear();
                foreach (var h in _historyManager.GetHistory())
                    HistoryEntries.Add(h);
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    /* Auto-save triggers */
    partial void OnSelectedModelChanged(TtsModelType value)
    {
        if (_isLoading)
            return;

        if (_engineProvider != null && _engineProvider.CurrentModelType != value)
        {
            var switchResult = _engineProvider.SwitchModel(value);
            if (switchResult.IsSuccess)
            {
                LoadActiveVoices();
                var newEngineConfig = _settingsManager?.GetEngineConfig<KokoroEngineConfig>(value);
                if (
                    newEngineConfig != null
                    && !string.IsNullOrEmpty(newEngineConfig.VoiceId)
                    && AvailableVoices.Contains(newEngineConfig.VoiceId)
                )
                {
                    SelectedVoice = newEngineConfig.VoiceId;
                }
                else if (AvailableVoices.Count > 0 && !AvailableVoices.Contains(SelectedVoice))
                {
                    SelectedVoice = AvailableVoices[0];
                }
                AutoSave();
            }
            else
            {
                /* Model switch failed (e.g. model not downloaded) - revert dropdown without saving */
                _isLoading = true;
                try
                {
                    SelectedModel = _engineProvider.CurrentModelType;
                }
                finally
                {
                    _isLoading = false;
                }
            }
        }
        else
        {
            AutoSave();
        }
    }

    partial void OnSelectedVoiceChanged(string value)
    {
        if (!_isLoading && _engineProvider?.CurrentEngine != null && !string.IsNullOrEmpty(value))
        {
            _engineProvider.CurrentEngine.ActiveVoice = value;
        }
        AutoSave();
    }

    partial void OnSpeechSpeedChanged(float value)
    {
        if (!_isLoading && _engineProvider?.CurrentEngine != null)
        {
            _engineProvider.CurrentEngine.Speed = value;
        }
        AutoSave();
    }

    partial void OnToggleAppHotkeyChanged(string value)
    {
        if (_isLoading || _settingsManager == null)
            return;

        if (!ShortcutCombo.TryParse(value, out _))
        {
            _isLoading = true;
            try
            {
                ToggleAppHotkey = _settingsManager.CurrentAppConfig.ToggleAppHotkey;
            }
            finally
            {
                _isLoading = false;
            }
            return;
        }

        if (_hotkeyManager != null)
        {
            var result = _hotkeyManager.AssignToggleHotkey(value);
            if (result.IsFailure)
                return;
        }

        AutoSave();
    }

    partial void OnEnablePrimaryPlaybackChanged(bool value) => AutoSave();

    partial void OnSelectedPrimaryDeviceChanged(AudioDeviceInfo? value) => AutoSave();

    partial void OnPrimaryVolumeChanged(int value) => AutoSave();

    partial void OnEnableMonitorPlaybackChanged(bool value) => AutoSave();

    partial void OnSelectedMonitorDeviceChanged(AudioDeviceInfo? value) => AutoSave();

    partial void OnMonitorVolumeChanged(int value) => AutoSave();

    partial void OnSelectedMonitorIndexChanged(int value) => AutoSave();

    partial void OnOpenOnStartupChanged(bool value) => AutoSave();

    partial void OnHideOnSynthChanged(bool value) => AutoSave();

    partial void OnRememberPositionChanged(bool value) => AutoSave();

    partial void OnCreateMicOnStartupChanged(bool value) => AutoSave();

    partial void OnCustomModelsDirectoryChanged(string value) => AutoSave();

    /* Persists active settings immediately to disk and applies audio changes */
    private void AutoSave()
    {
        if (_isLoading || _settingsManager == null || _audioPlayer == null)
            return;

        var appConfig = _settingsManager.CurrentAppConfig;
        appConfig.ActiveModel = SelectedModel;
        appConfig.EnablePrimaryPlayback = EnablePrimaryPlayback;
        appConfig.PrimaryAudioDeviceId = SelectedPrimaryDevice?.DeviceId ?? "-1";
        appConfig.PrimaryVolume = PrimaryVolume;

        appConfig.EnableMonitorPlayback = EnableMonitorPlayback;
        appConfig.MonitorAudioDeviceId = SelectedMonitorDevice?.DeviceId ?? "-1";
        appConfig.MonitorVolume = MonitorVolume;

        appConfig.DefaultMonitor = SelectedMonitorIndex;
        appConfig.OpenOnStartup = OpenOnStartup;
        appConfig.HideOnSynth = HideOnSynth;
        appConfig.RememberPosition = RememberPosition;
        appConfig.CreateMicOnStartup = CreateMicOnStartup;
        appConfig.CustomModelsDirectory = string.IsNullOrWhiteSpace(CustomModelsDirectory)
            ? null
            : CustomModelsDirectory.Trim();

        var engineConfig = _settingsManager.GetEngineConfig<KokoroEngineConfig>(SelectedModel);
        engineConfig.VoiceId = SelectedVoice;
        engineConfig.Speed = SpeechSpeed;

        _settingsManager.SaveAppConfig(appConfig);
        _settingsManager.SaveEngineConfig(SelectedModel, engineConfig);

        _audioPlayer.SetOutputDevices(
            appConfig.PrimaryAudioDeviceId,
            appConfig.MonitorAudioDeviceId
        );
        _audioPlayer.SetVolume(appConfig.PrimaryVolume, appConfig.MonitorVolume);
    }

    /* Handles applying the recorded hotkey combination */
    public void ApplyRecordedHotkey(string shortcut)
    {
        if (IsRecordingToggleHotkey)
        {
            ToggleAppHotkey = shortcut;
            IsRecordingToggleHotkey = false;
        }
        else if (IsRecordingPhraseHotkey)
        {
            NewHotkeyKey = shortcut;
            IsRecordingPhraseHotkey = false;
        }
    }

    /* Cancels any ongoing hotkey recording */
    public void CancelRecording()
    {
        IsRecordingToggleHotkey = false;
        IsRecordingPhraseHotkey = false;
    }

    [RelayCommand]
    private void ToggleRecordingToggleHotkey()
    {
        if (IsRecordingToggleHotkey)
        {
            CancelRecording();
        }
        else
        {
            CancelRecording();
            IsRecordingToggleHotkey = true;
        }
    }

    [RelayCommand]
    private void ToggleRecordingPhraseHotkey()
    {
        if (IsRecordingPhraseHotkey)
        {
            CancelRecording();
        }
        else
        {
            CancelRecording();
            IsRecordingPhraseHotkey = true;
        }
    }

    [RelayCommand]
    private void ClearNewHotkeyKey()
    {
        NewHotkeyKey = string.Empty;
        CancelRecording();
    }

    /* View Switchers */
    [RelayCommand]
    private void SwitchToAdvanced()
    {
        LoadSettings();
        IsAdvancedView = true;
    }

    [RelayCommand]
    private void SwitchToQuick()
    {
        LoadSettings();
        IsAdvancedView = false;
    }

    [RelayCommand]
    private void SelectTab(AdvancedTab tab) => SelectedTab = tab;

    /* Dictionary Actions */
    [RelayCommand]
    private void AddDictionaryEntry()
    {
        if (
            _dictManager == null
            || string.IsNullOrWhiteSpace(NewDictOriginal)
            || string.IsNullOrWhiteSpace(NewDictSpelling)
        )
            return;
        var res = _dictManager.AddEntry(
            NewDictOriginal.Trim(),
            NewDictSpelling.Trim(),
            NewDictCaseSensitive
        );
        if (res.IsSuccess)
        {
            NewDictOriginal = string.Empty;
            NewDictSpelling = string.Empty;
            DictionaryEntries.Clear();
            foreach (var e in _dictManager.GetEntries())
                DictionaryEntries.Add(e);
        }
    }

    [RelayCommand]
    private void DeleteDictionaryEntry(Guid id)
    {
        if (_dictManager == null)
            return;
        var res = _dictManager.DeleteEntry(id);
        if (res.IsSuccess)
        {
            DictionaryEntries.Clear();
            foreach (var e in _dictManager.GetEntries())
                DictionaryEntries.Add(e);
        }
    }

    /* Hotkey Actions */
    [RelayCommand]
    private async Task AddHotkeyPhraseAsync()
    {
        if (
            _hotkeyManager == null
            || string.IsNullOrWhiteSpace(NewHotkeyKey)
            || string.IsNullOrWhiteSpace(NewHotkeyPhrase)
        )
            return;
        var res = await _hotkeyManager.AssignHotkeyAsync(
            NewHotkeyKey.Trim(),
            NewHotkeyPhrase.Trim()
        );
        if (res.IsSuccess)
        {
            NewHotkeyKey = string.Empty;
            NewHotkeyPhrase = string.Empty;
            HotkeyEntries.Clear();
            foreach (var hk in _hotkeyManager.GetHotkeys())
                HotkeyEntries.Add(hk);
        }
    }

    [RelayCommand]
    private void DeleteHotkeyPhrase(Guid id)
    {
        if (_hotkeyManager == null)
            return;
        var res = _hotkeyManager.DeleteHotkey(id);
        if (res.IsSuccess)
        {
            HotkeyEntries.Clear();
            foreach (var hk in _hotkeyManager.GetHotkeys())
                HotkeyEntries.Add(hk);
        }
    }

    /* History Actions */
    [RelayCommand]
    private async Task PlayHistoryItemAsync(Guid id)
    {
        if (_historyManager == null)
            return;
        await _historyManager.PlayEntryAsync(id);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (_historyManager == null)
            return;

        var result = await _historyManager.ClearHistory();
        if (result.IsSuccess)
            HistoryEntries.Clear();
    }

    [RelayCommand]
    private async Task DeleteHistoryItemAsync(Guid id)
    {
        if (_historyManager == null)
            return;

        var result = await _historyManager.DeleteEntry(id);
        if (result.IsSuccess)
        {
            HistoryEntries.Clear();
            foreach (var entry in _historyManager.GetHistory())
                HistoryEntries.Add(entry);
        }
    }

    /* Engine Management Actions */
    [RelayCommand]
    private async Task DownloadModelAsync(ModelStatusItem item)
    {
        if (_downloadManager == null || item == null || item.IsDownloading)
            return;

        item.IsDownloading = true;
        try
        {
            var progress = new Progress<double>(p => item.Progress = p * 100);
            var result = await _downloadManager.DownloadModelAsync(item.ModelType, progress);
            item.IsDownloaded = result.IsSuccess;
            if (result.IsSuccess)
                LoadActiveVoices();
        }
        catch (Exception ex)
        {
            item.IsDownloaded = false;
            /* Surface this through a bound status property in the UI. */
            await Console.Error.WriteLineAsync($"Model download failed: {ex.Message}");
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    [RelayCommand]
    private void DeleteModel(ModelStatusItem item)
    {
        if (_downloadManager == null || item == null)
            return;
        var res = _downloadManager.DeleteModel(item.ModelType);
        if (res.IsSuccess)
        {
            item.IsDownloaded = false;
            item.Progress = 0;
        }
    }

    [RelayCommand]
    private void Close() => _windowService?.HideSettingsWindow();

    [RelayCommand]
    private void ExitApp() => _windowService?.ExitApplication();
}
