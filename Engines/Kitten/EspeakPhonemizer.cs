using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MoonTTS.Engine.Kitten;

/* File Structure:
        - EnsureInitialized() : void
        - Phonemize(string text) : string
        - Tokenize(string phonemizedText) : long[]
        - ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) : IntPtr
        - CleanToTokens(string text) : List<long>
        - espeak_Initialize(int output, int buflength, string? path, int options) : int
        - espeak_SetVoiceByName(string name) : int
        - espeak_TextToPhonemes(byte** textptr, int textmode, int phonememode) : byte*
*/

/* Native phonemizer wrapping libespeak-ng and mapping IPA tokens to the StyleTTS2/KittenTTS vocabulary */
public static unsafe partial class EspeakPhonemizer
{
    private const string LibraryName = "espeak-ng";
    private const int AudioOutputSynchronous = 0x02;
    private const int EspeakCharsUtf8 = 1;
    private const int EspeakPhonemesIpa = 0x02;

    private static readonly object s_initLock = new();
    private static bool s_isInitialized;
    private static int s_resolverRegistered;

    /* 178-character KittenTTS TextCleaner symbol vocabulary */
    private static readonly Dictionary<char, int> s_symbolMap = BuildSymbolMap();

    /* Builds the 178-symbol character index dictionary matching kittenml's TextCleaner */
    private static Dictionary<char, int> BuildSymbolMap()
    {
        const string pad = "$";
        const string punctuation = ";:,.!?¡¿—…\"«»\"\" ";
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        const string lettersIpa = "ɑɐɒæɓʙβɔɕçɗɖðʤəɘɚɛɜɝɞɟʄɡɠɢʛɦɧħɥʜɨɪʝɭɬɫɮʟɱɯɰŋɳɲɴøɵɸθœɶʘɹɺɾɻʀʁɽʂʃʈʧʉʊʋⱱʌɣɤʍχʎʏʑʐʒʔʡʕʢǀǁǂǃˈˌːˑʼʴʰʱʲʷˠˤ˞↓↑→↗↘'̩'ᵻ";

        string symbols = pad + punctuation + letters + lettersIpa;
        var map = new Dictionary<char, int>(symbols.Length);
        for (int i = 0; i < symbols.Length; i++)
        {
            map.TryAdd(symbols[i], i);
        }
        return map;
    }

    /* Registers native resolver and initializes libespeak-ng with en-us voice */
    public static void EnsureInitialized()
    {
        if (s_isInitialized)
            return;

        lock (s_initLock)
        {
            if (s_isInitialized)
                return;

            if (Interlocked.Exchange(ref s_resolverRegistered, 1) == 0)
            {
                NativeLibrary.SetDllImportResolver(typeof(EspeakPhonemizer).Assembly, ResolveLibrary);
            }

            string baseDir = AppContext.BaseDirectory;
            string dataRoot = Path.Combine(baseDir, "espeak-ng-data");

            int rate = espeak_Initialize(AudioOutputSynchronous, 500, Directory.Exists(dataRoot) ? dataRoot : null, 0);
            if (rate <= 0)
                throw new InvalidOperationException($"espeak_Initialize failed with code {rate}.");

            espeak_SetVoiceByName("en-us");
            s_isInitialized = true;
        }
    }

    /* Converts text into IPA phonemes while preserving punctuation, matching phonemizer.EspeakBackend */
    public static string Phonemize(string text)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder();
        var matches = Regex.Matches(text, @"\b[\w']+\b|[^\w\s]|\s+");

        foreach (Match match in matches)
        {
            string token = match.Value;
            if (char.IsLetterOrDigit(token[0]))
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(token + "\0");
                fixed (byte* pUtf8 = utf8)
                {
                    byte* cur = pUtf8;
                    while (cur != null && *cur != 0)
                    {
                        byte* ph = espeak_TextToPhonemes(&cur, EspeakCharsUtf8, EspeakPhonemesIpa);
                        if (ph != null)
                        {
                            string? s = Marshal.PtrToStringUTF8((IntPtr)ph);
                            if (!string.IsNullOrEmpty(s))
                                sb.Append(s);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                sb.Append(token);
            }
        }

        return sb.ToString();
    }

    /* Tokenizes phonemized text, adds start [0] and end [10, 0] tokens, and returns ONNX input_ids */
    public static long[] Tokenize(string phonemizedText)
    {
        if (string.IsNullOrWhiteSpace(phonemizedText))
            return [0, 10, 0];

        var tokens = Regex.Matches(phonemizedText, @"\w+|[^\w\s]")
            .Select(m => m.Value);

        string spaced = string.Join(" ", tokens);
        var indices = new List<long>();

        /* Prepend start token ($) */
        indices.Add(0);

        foreach (char c in spaced)
        {
            if (s_symbolMap.TryGetValue(c, out int id))
                indices.Add(id);
        }

        /* Append end tokens: 10 (…) and 0 ($) */
        indices.Add(10);
        indices.Add(0);

        return indices.ToArray();
    }

    /* Resolves bundled or system libespeak-ng library across platforms */
    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
            return IntPtr.Zero;

        string baseDir = AppContext.BaseDirectory;
        (string rid, string fileName) = OperatingSystem.IsWindows()
            ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64", "libespeak-ng.dll")
            : OperatingSystem.IsMacOS()
                ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64", "libespeak-ng.dylib")
                : (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64", "libespeak-ng.so");

        string[] candidates =
        [
            Path.Combine(baseDir, "runtimes", rid, "native", fileName),
            Path.Combine(baseDir, fileName)
        ];

        foreach (string path in candidates)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int espeak_Initialize(int output, int buflength, string? path, int options);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int espeak_SetVoiceByName(string name);

    [LibraryImport(LibraryName)]
    private static partial byte* espeak_TextToPhonemes(byte** textptr, int textmode, int phonememode);
}
