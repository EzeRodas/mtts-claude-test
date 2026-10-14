using System.Runtime.CompilerServices;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using Microsoft.ML.OnnxRuntime;
using MoonTTS.Core.Services;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Audio;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Enums;
using MoonTTS.Domain.Exceptions;
using MoonTTS.Domain.Interfaces;
using MoonTTS.Infrastructure.Audio;

namespace MoonTTS.Infrastructure.Tts;

/* File Structure:
        - ListVoices() : List<string>
        - SynthesizeStreamAsync(string text, float? speedOverride, CancellationToken cancellationToken) : IAsyncEnumerable<ReadOnlyMemory<byte>>
        - SynthesizeAsync(string text, float? speedOverride, CancellationToken cancellationToken) : Task<Result<byte[]>>
        - Dispose() : void
*/

public class KokoroOnnxEngine : ITtsEngine
{
    private KokoroWavSynthesizer? _synthesizer;
    private TtsModelType _activeModel;
    
    private readonly SettingsManager _settings;
    private CancellationTokenSource? _cancellTokenSource;
    private  bool _disposed;
    private KokoroVoice? _activeVoice;
    
    public AudioFormat OutputFormat => new AudioFormat(24000, 1, 16);
    private KokoroEngineConfig CurrentConfig => _settings.GetEngineConfig<KokoroEngineConfig>(_activeModel);
    
    /* Initializes Kokoro ONNX engine, loads model weights from disk, and sets active voice */
    public KokoroOnnxEngine(SettingsManager settingsManager, TtsModelType modelType)
    {
        
        /*Load general settings*/
        _settings = settingsManager;
        _activeModel = modelType;
        
        /*Checks if model is "Unselected", and skips loading if true*/
        if (_activeModel != TtsModelType.Unselected)
        {
            string? modelFileName = _activeModel switch
            {
                TtsModelType.Kokoro_zh_fp16 => "kokoro-v1.1-zh.onnx",
                TtsModelType.Kokoro_fp16 => "kokoro-v1.0-fp16.onnx",
                TtsModelType.Kokoro => "kokoro-v1.0.onnx",
                _ => null
            };
            string modelsDir = _settings.GetModelsDirectory();

            if (modelFileName != null)
            {
                string fullPath = Path.Combine(modelsDir, modelFileName);
                if (File.Exists(fullPath))
                {
                    /* Suppress constant-folding and optimization warnings from ONNX Runtime */
                    using var options = new SessionOptions
                    {
                        LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
                    };
                    _synthesizer = KokoroWavSynthesizer.LoadModel(fullPath, options);
                }
            }
        }
        
        /*Load model settings*/
        KokoroEngineConfig modelConfig = _settings.GetEngineConfig<KokoroEngineConfig>(_activeModel);
        
        /*Load voice or fallback to default if no setting found*/
        if (Enum.TryParse<KokoroVoiceId>(modelConfig.VoiceId, out var parsedVoice))
        {
            if (parsedVoice != KokoroVoiceId.Unselected)
            {
                _activeVoice = KokoroVoiceManager.GetVoice(parsedVoice.ToString());
            }
        }
    }


    /*Gets and sets active voice*/
    public string ActiveVoice
    {
        get => CurrentConfig.VoiceId;
        set
        {
            var config = CurrentConfig;
            config.VoiceId = value;
            _settings.SaveEngineConfig(_activeModel, config);
            
            _activeVoice = KokoroVoiceManager.GetVoice(value);
        }
    }

    /*Gets and sets speed*/
    public float Speed
    {
        get => CurrentConfig.Speed;
        set
        {
            var config = CurrentConfig;
            config.Speed = value;
            _settings.SaveEngineConfig(_activeModel, config);
        }
    }

    /*Returns if model is loaded on memory*/
    public bool IsAvailable => _synthesizer != null;

    /* Lists all voice identifiers supported by the Kokoro model */
    public List<string> ListVoices() =>
        Enum.GetNames<KokoroVoiceId>()
            .Where( v => v != nameof(KokoroVoiceId.Unselected))
            .ToList();

    /* Synthesizes text into raw 24kHz 16-bit PCM chunks with in-place de-clicking */
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text, 
        float? speedOverride = default, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsAvailable || _synthesizer == null || _activeVoice == null)
            yield break;

        float effectiveSpeed = speedOverride ?? CurrentConfig.Speed;
        var pipelineConfig = new KokoroTTSPipelineConfig { Speed = effectiveSpeed };

        byte[] rawPcmBytes = await _synthesizer.SynthesizeAsync(text, _activeVoice, pipelineConfig, cancellationToken);

        // De-click in-place with SIMD (zero allocations)
        SimdAudioProcessor.ApplyDeClickingInPlace(rawPcmBytes, OutputFormat.SampleRate);

        yield return rawPcmBytes;
    }

    /* Synthesizes text into a complete RIFF WAV byte array */
    public async Task<Result<byte[]>> SynthesizeAsync(string text, float? speedOverride = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result<byte[]>.Failure("Text to speak is empty.");
        
        /* Check if model is selected */
        if (!IsAvailable || _synthesizer == null)
            return Result<byte[]>.Failure(
                new InvalidEngineException(
                    "No TTS engine selected or model files are missing. Please select a model in settings before synthesizing."));
        
        /* If that succeds, check for voice != unselected */
        if (_activeVoice == null)
            return Result<byte[]>.Failure(new InvalidVoiceException("No voice selected."));

        using var ms = new MemoryStream();
        await foreach (var chunk in SynthesizeStreamAsync(text, speedOverride, cancellationToken))
        {
            ms.Write(chunk.Span);
        }

        byte[] wav = WavWriter.CreateWav(ms.ToArray(), OutputFormat);
        return Result<byte[]>.Success(wav);
    }

    /* Releases ONNX runtime synthesizer session and active tokens */
    public void Dispose()
    {
        if (_disposed) return;
        
        (_synthesizer as IDisposable)?.Dispose();
        _synthesizer = null;
        _activeVoice = null;
        
        _cancellTokenSource?.Cancel();
        _cancellTokenSource?.Dispose();
        _cancellTokenSource = null;
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}