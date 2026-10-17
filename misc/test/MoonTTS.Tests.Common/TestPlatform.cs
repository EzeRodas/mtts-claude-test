namespace MoonTTS.Tests.Common;

/* Platform conditions for xUnit's SkipWhen / SkipUnless, e.g.
   [Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))] */
public static class TestPlatform
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsLinux => OperatingSystem.IsLinux();
    public static bool IsMacOS => OperatingSystem.IsMacOS();

    /* True when a private D-Bus daemon can be started (needed by the dummy desktop portal) */
    public static bool HasDbusDaemon =>
        !IsWindows && (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Any(dir => File.Exists(Path.Combine(dir, "dbus-daemon")));
}
