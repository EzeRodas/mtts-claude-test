using System.ComponentModel;
using MoonTTS.Avalonia.Tests.TestSupport;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Avalonia.Tests.ViewModels;

/* Verifies the HUD view model: text entry, playback commands and window commands */
public sealed class MainViewModelTests
{
    private readonly FakeWindowService _windows = new();

    /* HotkeyManager is a required dependency the HUD does not use yet; built over fakes */
    private static HotkeyManager NewHotkeyManager() =>
        new(new FakeSpeechPipeline(), new FakeAudioPlayer(), new FakeConfigurationRepository());

    /* Builds the view model with the given pipeline and the fake window service */
    private MainViewModel Create(ISpeechPipeline pipeline) => new(pipeline, _windows, NewHotkeyManager());

    /* ---------- Constructors & window commands ---------- */

    /* With a window service every window command is available */
    [Fact]
    public void Ctor_WithWindowService_CreatesWindowCommands()
    {
        var vm = Create(new FakeSpeechPipeline());

        Assert.All([vm.CloseAppCommand, vm.ShowMainWindowCommand, vm.ExitAppCommand, vm.ToggleSettingsCommand], Assert.NotNull);
    }

    /* Without a window service no window command is created */
    [Fact]
    public void Ctor_WithoutWindowService_LeavesWindowCommandsNull()
    {
        var vm = new MainViewModel(new FakeSpeechPipeline(), null, NewHotkeyManager());

        Assert.All([vm.CloseAppCommand, vm.ShowMainWindowCommand, vm.ExitAppCommand, vm.ToggleSettingsCommand], Assert.Null);
    }

    /* The designer constructor builds a view model without services */
    [Fact]
    public void DesignerCtor_LeavesWindowCommandsNull()
    {
        var vm = new MainViewModel();

        Assert.All([vm.CloseAppCommand, vm.ShowMainWindowCommand, vm.ExitAppCommand, vm.ToggleSettingsCommand], Assert.Null);
    }

    /* The "x" button hides the HUD instead of exiting */
    [Fact]
    public void CloseAppCommand_Executed_HidesMainWindow()
    {
        var vm = Create(new FakeSpeechPipeline());

        vm.CloseAppCommand!.Execute(null);

        Assert.Equal((1, 0), (_windows.HideMainWindowCount, _windows.ExitApplicationCount));
    }

    /* The tray "Open Moon-TTS" item shows the HUD */
    [Fact]
    public void ShowMainWindowCommand_Executed_ShowsMainWindow()
    {
        var vm = Create(new FakeSpeechPipeline());

        vm.ShowMainWindowCommand!.Execute(null);

        Assert.Equal(1, _windows.ShowMainWindowCount);
    }

    /* The tray "Exit" item exits the application */
    [Fact]
    public void ExitAppCommand_Executed_ExitsApplication()
    {
        var vm = Create(new FakeSpeechPipeline());

        vm.ExitAppCommand!.Execute(null);

        Assert.Equal(1, _windows.ExitApplicationCount);
    }

    /* The settings button toggles the settings window */
    [Fact]
    public void ToggleSettingsCommand_Executed_TogglesSettingsWindow()
    {
        var vm = Create(new FakeSpeechPipeline());

        vm.ToggleSettingsCommand!.Execute(null);

        Assert.Equal(1, _windows.ToggleSettingsWindowCount);
    }

    /* ---------- CanPlay ---------- */

    /* Nothing can be played while the input is empty */
    [Fact]
    public void CanPlay_EmptyText_IsFalse()
    {
        var vm = Create(new FakeSpeechPipeline());

        Assert.False(vm.CanPlay);
    }

    /* Whitespace alone cannot be played */
    [Fact]
    public void CanPlay_WhitespaceText_IsFalse()
    {
        var vm = Create(new FakeSpeechPipeline());
        vm.TextToSpeak = "   ";

        Assert.False(vm.CanPlay);
    }

    /* Text can be played */
    [Fact]
    public void CanPlay_WithText_IsTrue()
    {
        var vm = Create(new FakeSpeechPipeline());
        vm.TextToSpeak = "hello";

        Assert.True(vm.CanPlay);
    }

    /* Nothing new can be played while a synthesis is running */
    [Fact]
    public void CanPlay_WhileSynthesizing_IsFalse()
    {
        var vm = Create(new FakeSpeechPipeline());
        vm.TextToSpeak = "hello";
        vm.IsSynthesizing = true;

        Assert.False(vm.CanPlay);
    }

    /* The play command is enabled exactly when CanPlay is true */
    [Fact]
    public void PlayCommand_WithText_CanExecute()
    {
        var vm = Create(new FakeSpeechPipeline());
        vm.TextToSpeak = "hello";

        Assert.True(vm.PlayCommand.CanExecute(null));
    }

    /* The play command is disabled while the input is empty */
    [Fact]
    public void PlayCommand_EmptyText_CannotExecute()
    {
        var vm = Create(new FakeSpeechPipeline());

        Assert.False(vm.PlayCommand.CanExecute(null));
    }

    /* Typing re-evaluates the play button */
    [Fact]
    public void TextToSpeak_Changed_RaisesPlayCanExecuteChanged()
    {
        var vm = Create(new FakeSpeechPipeline());
        int raised = 0;
        vm.PlayCommand.CanExecuteChanged += (_, _) => raised++;

        vm.TextToSpeak = "hello";

        Assert.Equal(1, raised);
    }

    /* Typing refreshes CanPlay for its bindings */
    [Fact]
    public void TextToSpeak_Changed_NotifiesCanPlay()
    {
        var vm = Create(new FakeSpeechPipeline());
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.TextToSpeak = "hello";

        Assert.Contains(nameof(MainViewModel.CanPlay), changed);
    }

    /* Starting or ending a synthesis refreshes CanPlay for its bindings */
    [Fact]
    public void IsSynthesizing_Changed_NotifiesCanPlay()
    {
        var vm = Create(new FakeSpeechPipeline());
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsSynthesizing = true;

        Assert.Contains(nameof(MainViewModel.CanPlay), changed);
    }

    /* ---------- Play ---------- */

    /* Playing sends the typed text to the speech pipeline */
    [Fact]
    public async Task Play_WithText_SpeaksText()
    {
        var pipeline = new FakeSpeechPipeline();
        var vm = Create(pipeline);
        vm.TextToSpeak = "hello world";

        await vm.PlayCommand.ExecuteAsync(null);

        var request = Assert.Single(pipeline.Requests);
        Assert.Equal("hello world", request.Text);
        Assert.Null(request.Options);
    }

    /* Playing empties the input box right away */
    [Fact]
    public async Task Play_WithText_ClearsInput()
    {
        var pipeline = new GatedSpeechPipeline();
        var vm = Create(pipeline);
        vm.TextToSpeak = "hello";

        var play = vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.TextToSpeak);
        pipeline.Complete();
        await play;
    }

    /* Playing lets the window service hide the HUD when "hide on synthesis" is on */
    [Fact]
    public async Task Play_WithText_NotifiesHideOnSynth()
    {
        var vm = Create(new FakeSpeechPipeline());
        vm.TextToSpeak = "hello";

        await vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(1, _windows.HideOnSynthCount);
    }

    /* While the pipeline speaks the HUD shows the synthesis controls */
    [Fact]
    public async Task Play_InProgress_IsSynthesizing()
    {
        var pipeline = new GatedSpeechPipeline();
        var vm = Create(pipeline);
        vm.TextToSpeak = "hello";

        var play = vm.PlayCommand.ExecuteAsync(null);

        Assert.True(vm.IsSynthesizing);
        pipeline.Complete();
        await play;
    }

    /* A new playback starts un-paused */
    [Fact]
    public async Task Play_WhilePaused_StartsUnpaused()
    {
        var pipeline = new GatedSpeechPipeline();
        var vm = Create(pipeline);
        vm.IsPaused = true;
        vm.TextToSpeak = "hello";

        var play = vm.PlayCommand.ExecuteAsync(null);

        Assert.False(vm.IsPaused);
        pipeline.Complete();
        await play;
    }

    /* When the pipeline finishes the HUD returns to the idle state */
    [Fact]
    public async Task Play_Completed_ResetsState()
    {
        var pipeline = new GatedSpeechPipeline();
        var vm = Create(pipeline);
        vm.TextToSpeak = "hello";
        var play = vm.PlayCommand.ExecuteAsync(null);
        vm.IsPaused = true;

        pipeline.Complete();
        await play;

        Assert.Equal((false, false), (vm.IsSynthesizing, vm.IsPaused));
    }

    /* A crashing pipeline still returns the HUD to the idle state */
    [Fact]
    public async Task Play_PipelineThrows_ResetsSynthesizing()
    {
        var pipeline = new GatedSpeechPipeline();
        var vm = Create(pipeline);
        vm.TextToSpeak = "hello";
        var play = vm.PlayCommand.ExecuteAsync(null);

        pipeline.Fail(new InvalidOperationException("engine died"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => play);
        Assert.False(vm.IsSynthesizing);
    }

    /* Executing play without text speaks nothing */
    [Fact]
    public async Task Play_EmptyText_SpeaksNothing()
    {
        var pipeline = new FakeSpeechPipeline();
        var vm = Create(pipeline);

        await vm.PlayCommand.ExecuteAsync(null);

        Assert.Empty(pipeline.Requests);
    }

    /* Without a pipeline playing keeps the text */
    [Fact]
    public async Task Play_WithoutPipeline_KeepsText()
    {
        var vm = new MainViewModel { TextToSpeak = "hello" };

        await vm.PlayCommand.ExecuteAsync(null);

        Assert.Equal(("hello", false), (vm.TextToSpeak, vm.IsSynthesizing));
    }

    /* ---------- Stop ---------- */

    /* Stop cancels the pipeline */
    [Fact]
    public void Stop_Executed_StopsPipeline()
    {
        var pipeline = new FakeSpeechPipeline();
        var vm = Create(pipeline);

        vm.StopCommand.Execute(null);

        Assert.Equal(1, pipeline.StopCount);
    }

    /* Stop returns the HUD to the idle state */
    [Fact]
    public void Stop_Executed_ResetsState()
    {
        var vm = Create(new FakeSpeechPipeline());
        vm.IsSynthesizing = true;
        vm.IsPaused = true;

        vm.StopCommand.Execute(null);

        Assert.Equal((false, false), (vm.IsSynthesizing, vm.IsPaused));
    }

    /* Without a pipeline Stop leaves the state untouched */
    [Fact]
    public void Stop_WithoutPipeline_LeavesStateUntouched()
    {
        var vm = new MainViewModel { IsSynthesizing = true, IsPaused = true };

        vm.StopCommand.Execute(null);

        Assert.Equal((true, true), (vm.IsSynthesizing, vm.IsPaused));
    }

    /* ---------- Pause / Resume ---------- */

    /* While playing the button pauses */
    [Fact]
    public void PauseResume_Playing_PausesPipeline()
    {
        var pipeline = new FakeSpeechPipeline();
        var vm = Create(pipeline);

        vm.PauseResumeCommand.Execute(null);

        Assert.Equal((1, 0, true), (pipeline.PauseCount, pipeline.ResumeCount, vm.IsPaused));
    }

    /* While paused the button resumes */
    [Fact]
    public void PauseResume_Paused_ResumesPipeline()
    {
        var pipeline = new FakeSpeechPipeline();
        var vm = Create(pipeline);
        vm.PauseResumeCommand.Execute(null);

        vm.PauseResumeCommand.Execute(null);

        Assert.Equal((1, 1, false), (pipeline.PauseCount, pipeline.ResumeCount, vm.IsPaused));
    }

    /* Without a pipeline the button does nothing */
    [Fact]
    public void PauseResume_WithoutPipeline_KeepsState()
    {
        var vm = new MainViewModel();

        vm.PauseResumeCommand.Execute(null);

        Assert.False(vm.IsPaused);
    }
}
