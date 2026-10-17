# Moon-TTS Unit Tests

Atomic xUnit (v3) tests for the whole application, mirroring the production projects one-to-one.

## Running

Everything, from the repository root:

```bash
./misc/test/run-tests.sh            # Linux / macOS
./misc/test/run-tests.ps1           # Windows (PowerShell)
dotnet test misc/test/MoonTTS.Tests.slnx   # same thing without the script
```

Arguments are forwarded to `dotnet test`:

```bash
./misc/test/run-tests.sh --filter "FullyQualifiedName~ModelId"     # one area
dotnet test misc/test/MoonTTS.Core.Tests                           # one project
```

Each test project is also a standalone executable (`dotnet run --project misc/test/MoonTTS.Core.Tests`).

## Layout

| Project | Tests |
| :--- | :--- |
| `MoonTTS.Domain.Tests` | `MoonTTS.Domain` |
| `MoonTTS.Engine.Protocol.Tests` | `MoonTTS.Engine.Protocol` |
| `MoonTTS.Core.Tests` | `MoonTTS.Core` |
| `MoonTTS.Infrastructure.Tests` | `MoonTTS.Infrastructure` (engine host, persistence, daemon, audio, OS hooks) |
| `MoonTTS.Avalonia.Tests` | `MoonTTS.Avalonia` (view models, services, views on the headless platform) |
| `MoonTTS.CLI.Tests` | `MoonTTS.CLI` |
| `MoonTTS.Tests.Common` | Shared fakes and dummy-OS helpers (not a test project) |
| `MoonTTS.Tests.DummyEngine` | Scriptable engine worker speaking the IPC protocol (not a test project) |

Inside a test project, folders mirror the production project and each production class `Foo` has a `FooTests` class.

## Dummy OS

Nothing talks to the real OS. Each OS boundary is replaced by a dummy that answers the way the OS would:

| Boundary | Dummy |
| :--- | :--- |
| Command-line OS tools (`pactl`, `wpctl`, `paplay`, `pw-play`, `pw-cli`) | `DummyExecutables`: shell scripts put first on `PATH` that record their arguments/stdin and print canned output. |
| User folders (`HOME`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `APPDATA`) | `EnvironmentScope.IsolatedUserDirectories` points them at a `TempDirectory`. |
| Engine workers | `DummyEngineInstaller` installs `MoonTTS.Tests.DummyEngine` as a real engine folder; `dummy-engine.json` scripts its behaviour (hangs, garbage output, crashes, errors, ignored cancels). |
| D-Bus desktop portal (Wayland shortcuts) | A private `dbus-daemon` with a dummy `org.freedesktop.portal.Desktop` service. |
| Native OS APIs (X11, Win32, macOS Carbon / CoreAudio) | Dummy implementations of the native-API interfaces the hooks and players receive in their constructors. |
| Windowing system (Avalonia) | `Avalonia.Headless.XUnit`. |
| Application services | `FakeConfigurationRepository`, `FakeAudioPlayer`, `FakeTtsEngine`, `FakeTtsEngineFactory`, `FakeModelRegistry`, `FakeModelDownloader`, `FakeSpeechPipeline`, `FakeGlobalShortcutHook`. |

## Conventions

- Test names: `Method_Scenario_ExpectedResult`. One behaviour per test.
- Every test method has a `/* ... */` comment saying what it verifies (same commenting style as the app).
- Tests that change environment variables, `PATH` or the current directory use `[Collection(TestCollections.Environment)]`; that collection runs without parallelism.
- Platform-specific tests skip themselves with `SkipWhen`/`SkipUnless` and `TestPlatform`, e.g. `[Fact(Skip = "POSIX only", SkipWhen = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform))]`.
- Pass `TestContext.Current.CancellationToken` to async APIs.
- Tests create their files inside a `TempDirectory`, never in the real user folders.
