using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoonTTS.Avalonia.Services;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Avalonia.ViewModels;

/* File Structure:
        - MainViewModel()
        - MainViewModel(ISpeechPipeline?, IWindowService?, HotkeyManager)
        - CanPlay : bool
        - PlayAsync() : Task
        - ShowStatusAsync(string message) : void
        - Stop() : void
        - PauseResume() : void
*/

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

    /* Transient feedback shown in the popup above the input box (e.g. no model selected) */
    [ObservableProperty] private string? _statusMessage;

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

    /* Initiates speech synthesis and streaming playback for the entered text.
       On failure the text is restored and the error is shown in the status popup */
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (_speechPipeline == null || !CanPlay) return;

        string text = TextToSpeak;
        TextToSpeak = string.Empty;
        IsSynthesizing = true;
        IsPaused = false;
        StatusMessage = null;

        try
        {
            _windowService?.HideOnSynth();
            var result = await _speechPipeline.SpeakAsync(text);

            /* A cancellation comes from the user pressing Stop, so it is not reported */
            if (result.IsFailure && result.ErrorMessage != SpeechPipeline.CancelledMessage)
            {
                if (string.IsNullOrEmpty(TextToSpeak))
                    TextToSpeak = text;

                /* HideOnSynth may have hidden the window; bring it back so the popup and restored text are visible */
                _windowService?.RevealMainWindow();
                ShowStatusAsync(result.ErrorMessage ?? "Speech synthesis failed.");
            }
        }
        finally
        {
            IsPaused = false;
            IsSynthesizing = false;
        }
    }

    /* Shows a transient message in the status popup and clears it after a few seconds,
       unless a newer message replaced it in the meantime */
    private async void ShowStatusAsync(string message)
    {
        try
        {
            StatusMessage = message;
            await Task.Delay(5000);
            if (StatusMessage == message)
                StatusMessage = null;
        }
        catch
        {
            /* Ignored */
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