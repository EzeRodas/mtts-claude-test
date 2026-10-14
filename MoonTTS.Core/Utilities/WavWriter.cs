using System.Buffers.Binary;
using MoonTTS.Domain.Audio;

namespace MoonTTS.Core.Utilities;

/* Single-pass RIFF WAV header encoder. Encodes audio directly into valid WAV format */
public static class WavWriter
{
    public const int HeaderSize = 44;

    /* Prepends a standard 44-byte RIFF WAV header to raw PCM audio data */
    public static byte[] CreateWav(ReadOnlySpan<byte> pcmData, AudioFormat format)
    {
        byte[] wav = new byte[HeaderSize + pcmData.Length];
        WriteWavHeader(wav.AsSpan(0, HeaderSize), pcmData.Length, format);
        pcmData.CopyTo(wav.AsSpan(HeaderSize));
        return wav;
    }

    /* Writes a standard 44-byte RIFF WAVE header into a target memory span using little-endian binary primitives */
    public static void WriteWavHeader(Span<byte> headerSpan, int pcmLength, AudioFormat format)
    {
        "RIFF"u8.CopyTo(headerSpan.Slice(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(4, 4), 36 + pcmLength);
        "WAVE"u8.CopyTo(headerSpan.Slice(8, 4));

        "fmt "u8.CopyTo(headerSpan.Slice(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(16, 4), 16); // Subchunk1Size
        BinaryPrimitives.WriteInt16LittleEndian(headerSpan.Slice(20, 2), 1);  // PCM format
        BinaryPrimitives.WriteInt16LittleEndian(headerSpan.Slice(22, 2), format.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(24, 4), format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(28, 4), format.BytesPerSecond);
        BinaryPrimitives.WriteInt16LittleEndian(headerSpan.Slice(32, 2), (short)format.FrameSize);
        BinaryPrimitives.WriteInt16LittleEndian(headerSpan.Slice(34, 2), (short)format.BitDepth);

        "data"u8.CopyTo(headerSpan.Slice(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(headerSpan.Slice(40, 4), pcmLength);
    }
}
