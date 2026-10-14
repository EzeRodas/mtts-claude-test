using MoonTTS.Domain.Enums;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Infrastructure.Tts;

/* List of all supported models */
/* File Structure:
        - KokoroFp32 : ModelMetadata
        - KokoroFp16 : ModelMetadata
        - KokoroZhFp16 : ModelMetadata
*/

/* The name of the key MUST match the name of the downloaded file, otherwise it won't be able to delete it later */

public static class ModelLibrary
{
    public static readonly ModelMetadata KokoroFp32 = new(
        ModelType: TtsModelType.Kokoro,
        DisplayName: "Kokoro ONNX (FP32 - Highest Quality)",
        DownloadFiles: new Dictionary<string, string>
        {
            ["kokoro.onnx"] = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro.onnx"
        }
    );

    public static readonly ModelMetadata KokoroFp16 = new(
        ModelType: TtsModelType.Kokoro_fp16,
        DisplayName: "Kokoro ONNX (FP16 - Performance)",
        DownloadFiles: new Dictionary<string, string>
        {
            ["kokoro-v1.0-fp16.onnx"] = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro-fp16.onnx"
        }
    );
    
    public static readonly ModelMetadata KokoroZhFp16 = new(
        ModelType: TtsModelType.Kokoro_zh_fp16,
        DisplayName: "Kokoro ONNX Chinese (FP16)",
        DownloadFiles: new Dictionary<string, string>
        {
            ["kokoro-zh-fp16.onnx"] = "https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/download/v2.0.0/kokoro-zh-fp16.onnx"
        }
    );
}