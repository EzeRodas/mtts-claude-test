#if !WINDOWS
using MoonTTS.Infrastructure.Audio;

namespace MoonTTS.Infrastructure.Tests.Audio.MacOS;

/* Converts strings through DummyCoreAudioApi, whose CFStrings are reference counted like CoreFoundation's */
public sealed class MacCoreFoundationStringsTests : IDisposable
{
    private readonly DummyCoreAudioApi _api = new();

    /* Frees the dummy */
    public void Dispose() => _api.Dispose();

    /* A null CFString converts to null */
    [Fact]
    public void CFStringToString_NullHandle_ReturnsNull()
    {
        string? text = _api.CFStringToString(IntPtr.Zero);

        Assert.Null(text);
    }

    /* An empty CFString converts to the empty string */
    [Fact]
    public void CFStringToString_EmptyString_ReturnsEmpty()
    {
        IntPtr cf = _api.CreateString("");

        string? text = _api.CFStringToString(cf);

        Assert.Equal(string.Empty, text);
    }

    /* An empty CFString is still released */
    [Fact]
    public void CFStringToString_EmptyString_ReleasesIt()
    {
        IntPtr cf = _api.CreateString("");

        _api.CFStringToString(cf);

        Assert.Equal(0, _api.LiveStringCount);
    }

    /* Every UTF-16 character, non-ASCII included, is copied */
    [Fact]
    public void CFStringToString_Text_ReturnsAllCharacters()
    {
        IntPtr cf = _api.CreateString("Altavoces – Peñón 🎧");

        string? text = _api.CFStringToString(cf);

        Assert.Equal("Altavoces – Peñón 🎧", text);
    }

    /* The CFString is released after being copied (CoreAudio hands out owned strings) */
    [Fact]
    public void CFStringToString_Text_ReleasesIt()
    {
        IntPtr cf = _api.CreateString("Speakers");

        _api.CFStringToString(cf);

        Assert.Equal(0, _api.LiveStringCount);
    }

    /* A .NET string becomes a CFString with the same characters */
    [Fact]
    public void StringToCFString_Text_CreatesStringWithSameCharacters()
    {
        IntPtr cf = _api.StringToCFString("AppleUSBAudioEngine:Headset");

        Assert.Equal("AppleUSBAudioEngine:Headset", _api.CFStringToString(cf));
    }

    /* The created CFString is owned by the caller, who must release it */
    [Fact]
    public void StringToCFString_Text_ReturnsOwnedString()
    {
        _api.StringToCFString("BuiltInSpeakerDevice");

        Assert.Equal(1, _api.LiveStringCount);
    }

    /* The empty string becomes an empty CFString */
    [Fact]
    public void StringToCFString_EmptyString_CreatesEmptyString()
    {
        IntPtr cf = _api.StringToCFString("");

        Assert.Equal(0, _api.CFStringGetLenght(cf));
    }
}
#endif
