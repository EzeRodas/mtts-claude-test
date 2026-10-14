using System;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Infrastructure.Audio;

public class AudioPlayerManager
{
    /* Manager that returns correct object with correct audio handling depending on platform */
    public static IAudioPlayer Create()
    {
        #if WINDOWS
        return new WindowsAudioPlayer();
        #else
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            return new LinuxAudioPlayer();
        }
        throw new PlatformNotSupportedException($"{Environment.OSVersion} is not supported to play audio yet");
        #endif
    }
}