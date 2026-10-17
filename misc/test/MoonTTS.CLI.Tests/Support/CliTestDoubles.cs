using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.CLI.Tests.Support;

/* File Structure:
        - CliVoiceRejectingEngine(string error, params VoiceInfo[] voices)
        - ActiveVoice : string
        - ListVoices() : IReadOnlyList<VoiceInfo>
        - SynthesizeStreamAsync(string text, float? speedOverride, CancellationToken cancellationToken) : IAsyncEnumerable<ReadOnlyMemory<byte>>
        - SynthesizeAsync(string text, float? speedOverride, CancellationToken cancellationToken) : Task<Result<byte[]>>
        - Dispose() : void
*/

/* A loaded engine whose ActiveVoice setter throws, like an engine that fails to persist the voice preference */
public sealed class CliVoiceRejectingEngine : ITtsEngine
{
    private readonly string _error;
    private readonly IReadOnlyList<VoiceInfo> _voices;

    public AudioFormat OutputFormat => new(22050, 1, 16);
    public bool IsAvailable => true;
    public float Speed { get; set; } = 1.0f;

    /* Reads as empty; setting throws InvalidOperationException with the configured message */
    public string ActiveVoice
    {
        get => string.Empty;
        set => throw new InvalidOperationException(_error);
    }

    /* Stores the error to throw and the voices to offer */
    public CliVoiceRejectingEngine(string error, params VoiceInfo[] voices)
    {
        _error = error;
        _voices = voices;
    }

    /* Returns the configured voices */
    public IReadOnlyList<VoiceInfo> ListVoices() => _voices;

    /* Produces no audio */
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text, float? speedOverride = default,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /* Produces no audio */
    public Task<Result<byte[]>> SynthesizeAsync(string text, float? speedOverride = default, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<byte[]>.Success(Array.Empty<byte>()));

    /* Nothing to release */
    public void Dispose() { }
}

/* File Structure:
        - CliProgressDownloader(params double[] reports)
        - DownloadModelAsync(ModelMetadata model, string destinationFolder, IProgress<double>? progress, CancellationToken cancellationToken) : Task<Result>
*/

/* IModelDownloader that reports the given percentages (0-100, the IModelDownloader contract) and writes the files */
public sealed class CliProgressDownloader : IModelDownloader
{
    private readonly double[] _reports;

    /* Stores the percentages to report, in order */
    public CliProgressDownloader(params double[] reports)
    {
        _reports = reports;
    }

    /* Reports every configured percentage and writes placeholder files */
    public Task<Result> DownloadModelAsync(ModelMetadata model, string destinationFolder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        foreach (double percent in _reports)
            progress?.Report(percent);
        Directory.CreateDirectory(destinationFolder);
        foreach (string file in model.DownloadFiles.Keys)
            File.WriteAllText(Path.Combine(destinationFolder, file), "dummy model");
        return Task.FromResult(Result.Success());
    }
}
