using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoonTTS.Avalonia.Services;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISpeechPipeline? _speechPipeline;
    private readonly IWindowService? _windowService;
    private readonly HotkeyManager _hotKeyManager;
    
    public ICommand? CloseAppCommand { get; }
    public ICommand? ShowMainWindowCommand { get; }
    public ICommand? ExitAppCommand { get; }
    public ICommand? ToggleSettingsCommand { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanPlay))] [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private string _textToSpeak = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanPlay))] [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isSynthesizing;

    [ObservableProperty] private bool _isPaused;

    /* Constructor */
    public MainViewModel() : this(null, null, null) { }
    
    /* Initializes main view model with speech pipeline and window management service */
    public MainViewModel(
        ISpeechPipeline? speechPipeline, 
        IWindowService? windowService, 
        HotkeyManager hotKeyManager)
    {
        _speechPipeline = speechPipeline;
        _windowService = windowService;
        _hotKeyManager = hotKeyManager;

        if (_windowService != null)
        {
            /* Hides the app instead of exiting when pressing the "x" */
            CloseAppCommand = new RelayCommand(_windowService.HideMainWindow);

            /* Tray options */
            ShowMainWindowCommand = new RelayCommand(_windowService.ShowMainWindow);
            ExitAppCommand = new RelayCommand(_windowService.ExitApplication);
            
            /* Settings menu */
            ToggleSettingsCommand = new RelayCommand(_windowService.ToggleSettingsWindow);
        }
    }

    public bool CanPlay => !IsSynthesizing && !string.IsNullOrWhiteSpace(TextToSpeak);

    /* Initiates speech synthesis and streaming playback for the entered text */
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (_speechPipeline == null || !CanPlay) return;

        string text = TextToSpeak;
        TextToSpeak = string.Empty;
        IsSynthesizing = true;
        IsPaused = false;

        try
        {
            _windowService?.HideOnSynth();
            await _speechPipeline.SpeakAsync(text);
        }
        finally
        {
            IsPaused = false;
            IsSynthesizing = false;
        }
    }
    
    /* Cancels ongoing speech synthesis and stops audio playback */
    [RelayCommand]
    private void Stop()
    {
        if (_speechPipeline == null) return;
        _speechPipeline.Stop();
        IsPaused = false;
        IsSynthesizing = false;
    }

    /* Toggles between pausing and resuming the active speech playback */
    [RelayCommand]
    private void PauseResume()
    {
        if (_speechPipeline == null) return;

        if (!IsPaused)
        {
            _speechPipeline.Pause();
            IsPaused = true;
        }
        else
        {
            _speechPipeline.Resume();
            IsPaused = false;
        }
    }
}