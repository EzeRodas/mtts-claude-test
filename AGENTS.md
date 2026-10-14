# AGENTS.md

## Project Overview
<!-- One-to-two sentence high-level summary of the codebase, tech stack, and framework versions -->
- **Stack:** .NET 10.0
- **Domain:** Local TTS Accesibility tool

## Core Persona & Rules
<!-- Define what kind of specialist the agent should act as -->
- Act as a senior .NET and accesibility consultant focused on type safety and performance. Always keep in mind Domain Driven Design (DDD) and polymorphism.

## Build & Test Commands
<!-- Precise, exact shell commands required to manage the project -->
- **Solution Root Directory:** `./Moon-TTS-CS`
- **Build Solution:** `dotnet build Moon-TTS-CS/MoonTTS.slnx`
- **Publish CLI:** `dotnet publish Moon-TTS-CS/MoonTTS.CLI/MoonTTS.CLI.csproj`
- **Build/Typecheck GUI:** `dotnet run --project Moon-TTS-CS/MoonTTS.Avalonia/ -f net10.0 --`
- **Build/Typecheck CLI:** `dotnet run --project Moon-TTS-CS/MoonTTS.CLI/ -f net10.0 --`

### Code Style & Commenting Conventions
- Use `/* ... */` block comments above functions and properties instead of XML doc comments (`///`).
- Keep the `/* File Structure: ... */` block at the top of class files synchronized whenever methods are added, removed,or modified.
- Return `Result` / `Result<T>` for operational outcomes instead of throwing business exceptions across boundaries.

### Technical & Domain Invariants
- **Dynamic Audio:** Never hardcode audio formats or sample rates. Always inspect WAV headers via `FormatReader` or query `ITtsEngine.OutputFormat`. Audio streaming to `IAudioPlayer` must strictly use `ReadOnlyMemory<byte>` via `StreamPcmAsync` or `PlayPcmAsync`.
- **MVVM model:** Avalonia uses an MVVM model that has to be reespected and each part must only take care of their responisibilities.
- **Compiling:** The app MUST copile in AOT (Ahead Of Time) mode, and not use the JIT dotnet runtime. Therefore reflection and other such things that could break in AOT must be avoided. 
- **Binary IPC Protocol:** IPC communication between CLI and the background daemon must strictly use binary framing (`DaemonIpcProtocol` / `DaemonOpcode`). Audio data must never be Base64-encoded or embedded inside JSON DTOs; audio must stream as raw PCM chunks via `DaemonOpcode.AudioChunk` frames.
- **AOT Persistence:** All persistent models and list collections must be registered with `[JsonSerializable]` in `AppJsonContext.cs`. File persistence in `IConfigurationRepository` must be atomic (`.tmp` write + `File.Move`).

## Operational Boundaries
<!-- Explicit constraints on what the agent can do freely vs. what needs permission -->
- Never write placeholder code or comments like `/* TODO: implement later */`.
- Never write temporal patches or workarounds. If something doesn't make architectural sense, consult with the user.
- Never delete or refactor existing comments UNLESS the functionality of the codeblock they're commenting changes.
- **Working Tree Awareness:** Before suggesting or modifying files, check `git status` or inspect existing working-tree edits to align with any manual changes or refactorings made by the user during the session.
- When stating a modification on a file, make sure to add the line of the code that needs to be modified.
- Always when implementing a new feature show first a description of the recommended changes, why they're the best option, and the relation between gains and implementation effort.

### Always Do
- Run the build command before declaring a task complete.
- Comment every function.
- When refactoring a function, lookup where it's used in other placesof the code to make sure its calls and adjacent logic also makes sense.
- Use relevant skills like dotnet, advanced dotnet, improved architecture, and thermo-nuclear code to improve the quality of the code provided.
- After each structural change, modify MoonTTS.md to better reflect the current state of the project.

### Ask First
- Before adding new third-party production dependency.

### Never Do
- Never downgrade existing package versions.
- Never hardcode API keys or secrets.
- Never write extra files like plans or any agent related code into the codebase directory. If you need to write a plan / artifact do so inside your own folder.

## Architectural Layout
<!-- Help the agent navigate your file structure quickly -->
- `/Moon-TTS-CS/MoonTTS.Avalonia`: GUI related code, including window management.
- `/Moon-TTS-CS/MoonTTS.CLI`: Code for de headless CLI version of the app, including the daemonthread forkeeping the app in memory when not using the GUI.
- `/Moon-TTS-CS/MoonTTS.Domain`: Domain entities, interfaces and objects of the project.
- `/Moon-TTS-CS/MoonTTS.Core`: Main logic of the program and enviroment agnostic code.
- `/Moon-TTS-CS/MoonTTS.Infrastructure`: Everything that needs to interact with either the OS or a third party dependency.
- For the whole project description refer to 'MoonTTS.md', make sure to fully read it andunderstand the structure before attempting changes.
