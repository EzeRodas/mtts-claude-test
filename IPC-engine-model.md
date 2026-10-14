# Moon-TTS: Out-of-Process Engine Architecture & IPC Specification

> **Document Version:** 1.0.0  
> **Status:** Architecture Proposal / Specification  
> **Domain:** Local Accessibility & Speech Synthesis Platform  

---

## 1. Architectural Vision & Core Goals

### 1.1 The Problem Today
Currently, `MoonTTS.Infrastructure` directly links speech synthesis engines (specifically `KokoroSharp` and its G2P dependency `MisakiSharp`) into the main application assembly. 
- In **Native AOT**, this forces all static dictionaries (such as `MisakiSharp`'s 67 MB embedded CMUdict) to be permanently baked into the `.rodata` segment of the primary `MoonTTS.Avalonia` and `mtts` binaries.
- If a user only intends to use a lightweight cloud voice, an OS-native voice (SAPI/SpeechDispatcher), or an alternative local model (e.g., Piper), they are still forced to download and store 70 MB of unused neural dependencies.
- Third-party contributors wanting to add new engines (e.g., in Python, Rust, C++, or Go) cannot do so without modifying the C# source code and ensuring 100% Native AOT compatibility.

### 1.2 The Out-of-Process Solution
Decouple Moon-TTS into an **ultra-lean core host (~20 MB)** and **modular, out-of-process engine workers**:

```
┌───────────────────────────────────────────────────────────────┐
│                    Moon-TTS Host Core (~20 MB)                │
│  - Avalonia GUI & CLI Frontends                               │
│  - Global OS Shortcuts (Wayland / X11 / Win / macOS)          │
│  - Audio Routing & Playback (pw-play / aplay / NAudio / Core) │
│  - Text History, Replacement Dictionaries, Hotkeys            │
└───────────────────────────────┬───────────────────────────────┘
                                │
                  Standard Binary IPC Stream
             (stdin/stdout pipes or Unix Sockets)
                                │
         ┌──────────────────────┼──────────────────────┐
         ▼                      ▼                      ▼
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│  Kokoro Engine   │  │   Piper Engine   │  │  Custom Engine   │
│  (Worker Proc)   │  │   (Worker Proc)  │  │ (Python/Rust/C++)│
│  - KokoroSharp   │  │  - piper binary  │  │  - Any library   │
│  - Misaki Dict   │  │  - onnxruntime   │  │  - Cloud API     │
└──────────────────┘  └──────────────────┘  └──────────────────┘
```

### 1.3 Key Architectural Benefits
1. **Core Binary Footprint:** Drops the main GUI and CLI binaries from ~96 MB down to **~20 MB**.
2. **Language Independence:** Community contributors can write engine plugins in **any language** (Python, Rust, C++, C#, Go).
3. **Zero Native AOT Constraints on Plugins:** Plugins do not need to satisfy .NET Native AOT compiler trimming or reflection limits.
4. **Crash Isolation & Sandboxing:** If a neural engine runs out of VRAM, corrupts memory, or throws a segfault, **Moon-TTS never crashes**. The host detects process termination and can gracefully restart it.
5. **Drop-in Extensibility:** Users install new engines simply by placing a folder into `~/.local/share/moontts/engines/<id>/`.

---

## 2. Binary IPC Framing Protocol Specification

Communication between Moon-TTS and an engine worker runs over standard input/output streams (`stdin` / `stdout`) or local Unix domain sockets. To ensure maximum throughput with zero-copy audio, the protocol uses strict binary framing.

### 2.1 Packet Structure

Every frame sent between Host and Worker follows a fixed 9-byte header:

```text
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Magic (0x4D 0x54)       |    Opcode     |   Flags (1B)  |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                        Payload Length                         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
+                       Payload (N bytes)                       +
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

- **Magic (2 bytes):** `0x4D 0x54` (`"MT"` in ASCII) — verifies frame alignment.
- **Opcode (1 byte):** Defines command or response type.
- **Flags (1 byte):** Reserved for stream flags (e.g., `0x01` = Compressed payload).
- **Payload Length (4 bytes):** 32-bit unsigned integer (big-endian), size of following payload in bytes.
- **Payload ($N$ bytes):** Binary body (raw PCM audio, UTF-8 JSON, or control parameters).

---

### 2.2 Opcode Definitions

| Opcode | Name | Direction | Payload Format | Description |
| :---: | :--- | :---: | :--- | :--- |
| `0x01` | `Ping` | Host $\to$ Worker | None | Liveness heartbeat check. |
| `0x02` | `Pong` | Worker $\to$ Host | None | Heartbeat response. |
| `0x10` | `GetMetadata` | Host $\to$ Worker | None | Requests engine capabilities, supported sample rates, voices. |
| `0x11` | `MetadataResponse` | Worker $\to$ Host | JSON (UTF-8) | Engine capabilities and supported configurations. |
| `0x12` | `ListVoices` | Host $\to$ Worker | None | Requests available voice IDs and descriptions. |
| `0x13` | `VoicesResponse` | Worker $\to$ Host | JSON (UTF-8) | Array of voice descriptors. |
| `0x20` | `Synthesize` | Host $\to$ Worker | JSON (UTF-8) | Speech request (text, voice ID, speed, pitch). |
| `0x21` | `AudioChunk` | Worker $\to$ Host | Binary PCM | Streamed raw PCM audio chunk (16-bit signed, mono). |
| `0x22` | `EndOfStream` | Worker $\to$ Host | None | Indicates completion of the current synthesis stream. |
| `0x23` | `Cancel` | Host $\to$ Worker | None | Immediately aborts the current synthesis job. |
| `0xFE` | `Error` | Worker $\to$ Host | JSON (UTF-8) | Error payload with code and human-readable message. |
| `0xFF` | `Shutdown` | Host $\to$ Worker | None | Graceful termination signal. |

---

### 2.3 Payload Formats

#### `0x11: MetadataResponse` (JSON)
```json
{
  "engineId": "kokoro-onnx",
  "engineName": "Kokoro ONNX Neural Synthesizer",
  "version": "1.0.0",
  "author": "Moon-TTS Team",
  "audioFormat": {
    "sampleRate": 24000,
    "channels": 1,
    "bitDepth": 16,
    "encoding": "PCM_SIGNED"
  },
  "supportsStreaming": true,
  "supportsPitchAdjustment": false,
  "supportsSpeedAdjustment": true
}
```

#### `0x20: Synthesize` (JSON)
```json
{
  "text": "Hello world, this is accessibility speech synthesis.",
  "voiceId": "af_heart",
  "speed": 1.0,
  "pitch": 1.0,
  "streamChunks": true
}
```

#### `0x21: AudioChunk` (Binary)
- **Header:** Standard 9-byte packet header (`Opcode = 0x21`).
- **Payload:** Raw, uncompressed PCM audio bytes (16-bit little-endian samples).
- **Latency Rule:** The worker emits chunks as soon as a phonetic clause or sentence is rendered (typically 100ms–250ms of audio per chunk) to achieve instant playback start.

---

## 3. Plugin Discovery & Manifest Format

Engines are discovered dynamically at startup by scanning standard platform directories:
- **Linux:** `~/.local/share/moontts/engines/` and `/usr/share/moontts/engines/`
- **Windows:** `%LocalAppData%/MoonTTS/engines/`
- **macOS:** `~/Library/Application Support/MoonTTS/engines/`

### 3.1 Folder Structure
```text
~/.local/share/moontts/engines/
├── piper/
│   ├── engine.json            (Plugin manifest)
│   ├── bin/
│   │   └── piper-worker       (Executable)
│   └── models/
│       ├── en_US-lessac.onnx
│       └── en_US-lessac.json
└── kokoro/
    ├── engine.json
    ├── bin/
    │   └── kokoro-worker
    └── models/
        ├── kokoro-v0_19.onnx
        └── phonemes.bin       (Decoupled pronunciation dictionary)
```

### 3.2 Manifest Schema (`engine.json`)
```json
{
  "$schema": "https://moontts.org/schemas/engine-v1.json",
  "id": "piper-tts",
  "displayName": "Piper Neural Synthesizer",
  "description": "Ultra-fast local neural text-to-speech engine running on ONNX.",
  "version": "1.2.0",
  "executable": "bin/piper-worker",
  "supportedPlatforms": ["linux-x64", "linux-arm64", "win-x64", "osx-arm64"],
  "defaultSampleRate": 22050,
  "channels": 1,
  "bitDepth": 16,
  "voices": [
    { "id": "en_US-lessac-medium", "name": "Lessac (US English, Medium)", "language": "en-US" },
    { "id": "es_ES-davefx-medium", "name": "Dave (Spanish, Medium)", "language": "es-ES" }
  ]
}
```

---

## 4. Host Implementation Design (`MoonTTS.Core`)

### 4.1 New Domain & Core Interfaces

In `MoonTTS.Domain/Interfaces`:
```csharp
/* Represents an out-of-process engine adapter implementing ITtsEngine */
public interface IExternalEngineProcess : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken);
    Task<Result<EngineMetadata>> InitializeAsync(CancellationToken cancellationToken);
    Task<Result<List<string>>> GetVoicesAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(string text, SpeechOptions options, CancellationToken ct);
    Task StopAsync();
}
```

### 4.2 Engine Lifecycle Manager (`EngineProcessWorker.cs`)

```csharp
public class EngineProcessWorker : IExternalEngineProcess
{
    private Process? _process;
    private Stream? _stdin;
    private Stream? _stdout;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public async Task StartAsync(string executablePath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to spawn engine worker");
        _stdin = _process.StandardInput.BaseStream;
        _stdout = _process.StandardOutput.BaseStream;

        /* Monitor process exit unexpectedly */
        _process.EnableRaisingEvents = true;
        _process.Exited += (s, e) => HandleProcessUnexpectedTermination();
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text, 
        SpeechOptions options, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct);
        try
        {
            /* 1. Send 0x20 Synthesize Request */
            await SendPacketAsync(Opcode.Synthesize, JsonSerializer.SerializeToUtf8Bytes(new { text, voice = options.VoiceId }));

            /* 2. Read incoming frames until 0x22 EndOfStream */
            while (!ct.IsCancellationRequested)
            {
                var frame = await ReadPacketAsync(ct);
                if (frame.Opcode == Opcode.EndOfStream)
                    yield break;

                if (frame.Opcode == Opcode.AudioChunk)
                    yield return frame.Payload;

                if (frame.Opcode == Opcode.Error)
                    throw new InvalidOperationException($"Engine reported error: {Encoding.UTF8.GetString(frame.Payload.Span)}");
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }
}
```

---

## 5. Contributor Reference Implementation (Python Template)

A third-party developer can create an engine in under 60 lines of Python using standard library utilities:

```python
#!/usr/bin/env python3
import sys
import struct
import json

MAGIC = b'\x4D\x54'
OP_METADATA_REQ = 0x10
OP_METADATA_RESP = 0x11
OP_SYNTHESIZE = 0x20
OP_AUDIO_CHUNK = 0x21
OP_END_OF_STREAM = 0x22

def send_frame(opcode, payload=b''):
    header = struct.pack('>2sBB I', MAGIC, opcode, 0, len(payload))
    sys.stdout.buffer.write(header + payload)
    sys.stdout.buffer.flush()

def read_frame():
    header = sys.stdin.buffer.read(8)
    if len(header) < 8:
        return None, None
    magic, opcode, flags, length = struct.unpack('>2sBB I', header)
    if magic != MAGIC:
        raise ValueError("Invalid magic bytes")
    payload = sys.stdin.buffer.read(length) if length > 0 else b''
    return opcode, payload

def main():
    while True:
        opcode, payload = read_frame()
        if opcode is None:
            break

        if opcode == OP_METADATA_REQ:
            metadata = {
                "engineId": "python-tts",
                "sampleRate": 22050,
                "voices": ["default"]
            }
            send_frame(OP_METADATA_RESP, json.dumps(metadata).encode('utf-8'))

        elif opcode == OP_SYNTHESIZE:
            req = json.loads(payload.decode('utf-8'))
            text = req.get("text", "")

            # Perform synthesis & yield chunks
            # for pcm_chunk in my_tts_model.stream(text):
            #     send_frame(OP_AUDIO_CHUNK, pcm_chunk)

            send_frame(OP_END_OF_STREAM)

if __name__ == '__main__':
    main()
```

---

## 6. Phased Implementation Roadmap

When you are ready to implement this in the future:

1. **Phase 1: Domain Protocol Definitions**
   - Add `EngineIpcProtocol.cs` in `MoonTTS.Domain/Audio/` with binary framing helpers.
   - Define `ExternalEngineManifest` DTOs.
2. **Phase 2: External Engine Adapter**
   - Implement `ExternalEngineAdapter : ITtsEngine` in `MoonTTS.Infrastructure`.
   - Implement process lifecycle supervisor with auto-restart on crash.
3. **Phase 3: Separate Kokoro Worker**
   - Move `KokoroOnnxEngine.cs` and `KokoroSharp` into a separate standalone project: `MoonTTS.Engine.Kokoro`.
   - Build it as a standalone worker executable.
4. **Phase 4: Decouple Core Dependencies**
   - Remove `<PackageReference Include="KokoroSharp" />` and `OnnxRuntime` from `MoonTTS.Infrastructure.csproj`.
   - Core app binary immediately drops from **96 MB $\to$ ~20 MB**.
5. **Phase 5: Plugin Discovery**
   - Wire `DownloadManager` and `ModelLibrary` to download engine worker bundles into `~/.local/share/moontts/engines/`.
