#if !WINDOWS
/* Linux audio types only exist outside the Windows build */
namespace MoonTTS.Infrastructure.Tests.Audio.Linux;

/* File Structure:
        - PactlListSinks : string
        - PactlListSinksWithVirtualMic : string
        - PactlListSinkWithoutDescription : string
        - PactlListSinksShort : string
        - PactlListSinksShortWithVirtualMic : string
        - WpctlStatus : string
        - WpctlStatusWithVirtualMic : string
        - WpctlStatusWithoutSinks : string
*/

/* Canned output in the exact format printed by pactl (pipewire-pulse) and wpctl (WirePlumber) on a desktop with
   built-in audio and a USB headset */
internal static class LinuxToolOutputs
{
    /* `pactl list sinks` with two hardware sinks */
    public const string PactlListSinks =
        "Sink #47\n" +
        "\tState: SUSPENDED\n" +
        "\tName: alsa_output.pci-0000_00_1f.3.analog-stereo\n" +
        "\tDescription: Built-in Audio Analog Stereo\n" +
        "\tDriver: PipeWire\n" +
        "\tSample Specification: s32le 2ch 48000Hz\n" +
        "\tChannel Map: front-left,front-right\n" +
        "\tOwner Module: 4294967295\n" +
        "\tMute: no\n" +
        "\tVolume: front-left: 26214 /  40% / -23.88 dB,   front-right: 26214 /  40% / -23.88 dB\n" +
        "\t        balance 0.00\n" +
        "\tBase Volume: 65536 / 100% / 0.00 dB\n" +
        "\tMonitor Source: alsa_output.pci-0000_00_1f.3.analog-stereo.monitor\n" +
        "\tLatency: 0 usec, configured 0 usec\n" +
        "\tFlags: HARDWARE HW_MUTE_CTRL HW_VOLUME_CTRL DECIBEL_VOLUME LATENCY \n" +
        "\tProperties:\n" +
        "\t\talsa.card = \"0\"\n" +
        "\t\tdevice.description = \"Built-in Audio Analog Stereo\"\n" +
        "\t\tnode.name = \"alsa_output.pci-0000_00_1f.3.analog-stereo\"\n" +
        "\tPorts:\n" +
        "\t\tanalog-output-speaker: Speakers (type: Speaker, priority: 10000, availability group: Legacy 1, availability unknown)\n" +
        "\tActive Port: analog-output-speaker\n" +
        "\tFormats:\n" +
        "\t\tpcm\n" +
        "\n" +
        "Sink #52\n" +
        "\tState: RUNNING\n" +
        "\tName: alsa_output.usb-Logitech_G435-00.analog-stereo\n" +
        "\tDescription: G435 Wireless Gaming Headset Analog Stereo\n" +
        "\tDriver: PipeWire\n" +
        "\tSample Specification: s16le 2ch 48000Hz\n" +
        "\tMonitor Source: alsa_output.usb-Logitech_G435-00.analog-stereo.monitor\n" +
        "\tProperties:\n" +
        "\t\tdevice.description = \"G435 Wireless Gaming Headset Analog Stereo\"\n" +
        "\tFormats:\n" +
        "\t\tpcm\n";

    /* `pactl list sinks` once the Moon-TTS null sink exists */
    public const string PactlListSinksWithVirtualMic =
        PactlListSinks +
        "\n" +
        "Sink #68\n" +
        "\tState: IDLE\n" +
        "\tName: Moon-TTS_Sink\n" +
        "\tDescription: Moon-TTS Virtual Sink\n" +
        "\tDriver: PipeWire\n" +
        "\tSample Specification: float32le 2ch 48000Hz\n" +
        "\tMonitor Source: Moon-TTS_Sink.monitor\n" +
        "\tFormats:\n" +
        "\t\tpcm\n";

    /* `pactl list sinks` for a sink that has no Description line */
    public const string PactlListSinkWithoutDescription =
        "Sink #3\n" +
        "\tState: SUSPENDED\n" +
        "\tName: bluez_output.00_1B_66_A1_B2_C3.1\n" +
        "\tDriver: PipeWire\n";

    /* `pactl list sinks short` without the virtual microphone */
    public const string PactlListSinksShort =
        "47\talsa_output.pci-0000_00_1f.3.analog-stereo\tPipeWire\ts32le 2ch 48000Hz\tSUSPENDED\n" +
        "52\talsa_output.usb-Logitech_G435-00.analog-stereo\tPipeWire\ts16le 2ch 48000Hz\tRUNNING\n";

    /* `pactl list sinks short` once the Moon-TTS null sink exists */
    public const string PactlListSinksShortWithVirtualMic =
        PactlListSinksShort +
        "68\tMoon-TTS_Sink\tPipeWire\tfloat32le 2ch 48000Hz\tIDLE\n";

    /* `wpctl status` with two audio sinks (the default one marked with '*') */
    public const string WpctlStatus =
        "PipeWire 'pipewire-0' [1.0.5, moon@desktop, cookie:3735928559]\n" +
        " └─ Clients:\n" +
        "        33. WirePlumber                         [1.0.5, moon@desktop, pid:1520]\n" +
        "        34. pipewire                            [1.0.5, moon@desktop, pid:1519]\n" +
        "\n" +
        "Audio\n" +
        " ├─ Devices:\n" +
        " │      42. Built-in Audio                      [alsa]\n" +
        " │      43. G435 Wireless Gaming Headset        [alsa]\n" +
        " │  \n" +
        " ├─ Sinks:\n" +
        " │  *   48. Built-in Audio Analog Stereo        [vol: 0.40]\n" +
        " │      57. G435 Wireless Gaming Headset Analog Stereo [vol: 1.00 MUTED]\n" +
        " │  \n" +
        " ├─ Sink endpoints:\n" +
        " │  \n" +
        " ├─ Sources:\n" +
        " │  *   49. Built-in Audio Analog Stereo        [vol: 1.00]\n" +
        " │  \n" +
        " ├─ Source endpoints:\n" +
        " │  \n" +
        " └─ Streams:\n" +
        "\n" +
        "Video\n" +
        " ├─ Devices:\n" +
        " │      61. Integrated Camera                   [v4l2]\n" +
        " │  \n" +
        " ├─ Sinks:\n" +
        " │  \n" +
        " ├─ Sources:\n" +
        " │  *   63. Integrated Camera (V4L2)\n" +
        " │  \n" +
        " └─ Streams:\n" +
        "\n" +
        "Settings\n" +
        " └─ Default Configured Node Names:\n" +
        "         0. Audio/Sink    alsa_output.pci-0000_00_1f.3.analog-stereo\n";

    /* `wpctl status` where the Moon-TTS null sink is the configured default sink */
    public const string WpctlStatusWithVirtualMic =
        "Audio\n" +
        " ├─ Sinks:\n" +
        " │  *   48. Built-in Audio Analog Stereo        [vol: 0.40]\n" +
        " │      70. Moon-TTS Virtual Sink               [vol: 1.00]\n" +
        " │  \n" +
        " ├─ Sources:\n" +
        " │  \n" +
        "\n" +
        "Settings\n" +
        " └─ Default Configured Node Names:\n" +
        "         0. Audio/Sink    Moon-TTS_Sink\n";

    /* `wpctl status` of a PipeWire instance that has no audio section at all */
    public const string WpctlStatusWithoutSinks =
        "PipeWire 'pipewire-0' [1.0.5, moon@desktop, cookie:3735928559]\n" +
        " └─ Clients:\n" +
        "        33. WirePlumber                         [1.0.5, moon@desktop, pid:1520]\n";
}

#endif
