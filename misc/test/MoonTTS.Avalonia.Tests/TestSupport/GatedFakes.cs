using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Avalonia.Tests.TestSupport;

/* File Structure:
        - GatedModelDownloader
            - DownloadModelAsync(ModelMetadata model, string destinationFolder, IProgress<double>? progress, CancellationToken cancellationToken) : Task<Result>
            - ReportProgress(double percentage) : void
            - Complete(Result result) : void
        - GatedSpeechPipeline
            - SpeakAsync(string text, CancellationToken cancellationToken) : Task<Result<SpeechResult>>
            - SpeakAsync(string text, SpeechOptions options, CancellationToken cancellationToken) : Task<Result<SpeechResult>>
            - Pause() : void
            - Resume() : void
            - Stop() : void
            - Complete() : void
            - Fail(Exception exception) : void
        - PreviewSettingsViewModel
*/

/* IModelDownloader whose download stays in progress until the test completes it, so in-flight UI state
   (IsDownloading, progress, repeated clicks) can be observed */
public sealed class GatedModelDownloader : IModelDownloader
{
    private readonly TaskCompletionSource<Result> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IProgress<double>? _progress;

    public List<ModelMetadata> Requests { get; } = new();

    /* Records the request and returns a task that finishes when Complete is called */
    public Task<Result> DownloadModelAsync(ModelMetadata model, string destinationFolder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Requests.Add(model);
        _progress = progress;
        return _completion.Task;
    }

    /* Reports a 0-100 percentage through the progress callback, like HttpModelDownloader does */
    public void ReportProgress(double percentage) => _progress?.Report(percentage);

    /* Finishes the pending download with `result` */
    public void Complete(Result result) => _completion.TrySetResult(result);
}

/* ISpeechPipeline whose SpeakAsync stays pending until the test completes or fails it */
public sealed class GatedSpeechPipeline : ISpeechPipeline
{
    private readonly TaskCompletionSource<Result<SpeechResult>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> Requests { get; } = new();
    public bool IsSpeaking => !_completion.Task.IsCompleted;
    public bool IsPaused => false;
    public int StopCount { get; private set; }

    /* Records the request and waits for the gate */
    public Task<Result<SpeechResult>> SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        Requests.Add(text);
        return _completion.Task;
    }

    /* Records the request and waits for the gate */
    public Task<Result<SpeechResult>> SpeakAsync(string text, SpeechOptions options, CancellationToken cancellationToken = default) =>
        SpeakAsync(text, cancellationToken);

    /* Not used by the gated scenarios */
    public void Pause() { }

    /* Not used by the gated scenarios */
    public void Resume() { }

    /* Counts stops */
    public void Stop() => StopCount++;

    /* Lets the pending synthesis finish successfully */
    public void Complete() => _completion.TrySetResult(Result<SpeechResult>.Success(new SpeechResult()));

    /* Makes the pending synthesis throw */
    public void Fail(Exception exception) => _completion.TrySetException(exception);
}

/* Exposes the designer/previewer constructor, which builds the view model without any service */
public sealed class PreviewSettingsViewModel : SettingsViewModel
{
    /* Calls the protected parameterless constructor */
    public PreviewSettingsViewModel()
    {
    }
}
