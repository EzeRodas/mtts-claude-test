# Building Moon-TTS from a Clean Git Pull

This guide walks through building Moon-TTS (Desktop GUI, CLI/Daemon, and the **Kokoro**, **Kitten**, and **Mbrola** out-of-process TTS engine workers) from a fresh `git clone` or `git pull` on **Linux**, **Windows**, and **macOS**.

---

## Architecture Overview: What Gets Built

Moon-TTS consists of two parts that are compiled separately:

1. **The Host Solution (`MoonTTS.slnx`)**:
   - `MoonTTS.Avalonia`: The desktop HUD and settings GUI.
   - `MoonTTS.CLI` (`mtts`): The command-line interface and background IPC daemon.
2. **Out-of-Process Engine Workers (`Engines/*`)**:
   - `Engines/Kokoro`: Kokoro ONNX neural synthesizer (`kokoro-worker`).
   - `Engines/Mbrola`: MBROLA + eSpeak NG G2P diphone synthesizer (`mbrola-worker`).
   - `Engines/Kitten`: KittenTTS lightweight ONNX synthesizer (`kitten-worker`).

> [!IMPORTANT]
> **`Mbrola` and `Kitten` require a one-time native build step (`Engines/Mbrola/build-native.sh`) after a clean clone.**
> Both engines share the custom-compiled `libespeak-ng` library (which links `numediart/MBROLA` `LibOneChannel` in-memory) and the pruned `espeak-ng-data/` directory. Because `Engines/Mbrola/runtimes/` and `Engines/Mbrola/espeak-ng-data/` are git-ignored, you must run `Engines/Mbrola/build-native.sh` once before publishing `Mbrola` or `Kitten`.

---

## 1. Linux (`linux-x64` / `linux-arm64`)

### 1.1 Prerequisites

Install the **.NET 10.0 SDK**, the C/C++ linker prerequisites required by .NET Native AOT (`clang`, `zlib`), and the autotools chain used by `build-native.sh`:

- **Ubuntu / Debian:**
  ```bash
  sudo apt update
  sudo apt install -y git curl python3 build-essential clang zlib1g-dev autoconf automake libtool pkg-config
  ```
- **Arch / Manjaro:**
  ```bash
  sudo pacman -S --needed git curl python base-devel clang zlib autoconf automake libtool pkgconf
  ```
- **Fedora:**
  ```bash
  sudo dnf install -y git curl python3 gcc gcc-c++ make clang zlib-devel autoconf automake libtool pkgconf-pkg-config
  ```

### 1.2 Step-by-Step Build

```bash
# 1. Clone or pull the repository
git clone https://github.com/EzeRodas/Moon-TTS-CS.git
cd Moon-TTS-CS

# 2. Build libespeak-ng.so (with in-memory MBROLA) and espeak-ng-data (required once for Mbrola & Kitten)
bash Engines/Mbrola/build-native.sh

# 3. Publish the Engine Workers into ~/.local/share/Moon-TTS/engines/
ENGINES_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/Moon-TTS/engines"
dotnet publish Engines/Kokoro/MoonTTS.Engine.Kokoro.csproj -c Release -r linux-x64 -o "$ENGINES_DIR/Kokoro"
dotnet publish Engines/Mbrola/MoonTTS.Engine.Mbrola.csproj -c Release -r linux-x64 -o "$ENGINES_DIR/Mbrola"
dotnet publish Engines/Kitten/MoonTTS.Engine.Kitten.csproj -c Release -r linux-x64 -o "$ENGINES_DIR/Kitten"

# 4. Publish the Desktop GUI and CLI (Native AOT)
dotnet publish MoonTTS.Avalonia/MoonTTS.Avalonia.csproj -c Release -r linux-x64 -o dist/app
dotnet publish MoonTTS.CLI/MoonTTS.CLI.csproj -c Release -r linux-x64 -o dist/app
```

> [!TIP]
> **Quick Development Run (without full Native AOT publish):**
> Once Step 2 and Step 3 have populated `~/.local/share/Moon-TTS/engines/`, you can iterate on the host code with:
> ```bash
> dotnet build MoonTTS.slnx
> dotnet run --project MoonTTS.Avalonia/ -f net10.0 --
> dotnet run --project MoonTTS.CLI/ -f net10.0 -- --help
> ```

---

## 2. Windows (`win-x64`)

On Windows you can either let **GitHub Actions** build the installer automatically or build locally on a Windows machine.

### Option A: Automated Build via GitHub Actions (No Local Setup Needed)

The workflow in [`.github/workflows/release-windows.yml`](../../.github/workflows/release-windows.yml) runs on `windows-latest` and handles everything automatically:
1. **Manual build:** Go to the GitHub repository → **Actions** → **Build Windows Installer** → click **Run workflow**, then download **`MoonTTS-Windows-Installer`** from the run's Artifacts.
2. **Release tag build:** Push a version tag:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
   GitHub Actions will build `libespeak-ng.dll`, compile the GUI, CLI, and all three engines with Native AOT, package `MoonTTS-Setup-1.0.0-win-x64.exe` with Inno Setup, and attach it to the GitHub Release.

---

### Option B: Local Build on Windows from a Clean Pull

#### 2.1 Prerequisites

1. **.NET 10.0 SDK** (`winget install Microsoft.DotNet.SDK.10`)
2. **Visual Studio 2022/2026 Build Tools** with the **"Desktop development with C++"** workload installed (required by .NET Native AOT for `link.exe` and the Windows SDK).
3. **MSYS2** (`winget install MSYS2.MSYS2`) — only required to run `Engines/Mbrola/build-native.sh` once.
4. **Inno Setup 6** (`winget install JRSoftware.InnoSetup`) — only required if you want to compile the `MoonTTS-Setup.exe` installer.

#### 2.2 Step 1 — Build `libespeak-ng.dll` & `espeak-ng-data` (MSYS2 UCRT64)

Open the **MSYS2 UCRT64** terminal, install the build packages, and run `build-native.sh`:

```bash
pacman -S --needed base-devel autotools git curl python mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-toolchain

cd /c/path/to/Moon-TTS-CS
bash Engines/Mbrola/build-native.sh
```
This stages `Engines/Mbrola/runtimes/win-x64/native/libespeak-ng.dll` and `Engines/Mbrola/espeak-ng-data/`.

#### 2.3 Step 2 — Publish the Native AOT Binaries (PowerShell)

Open **PowerShell** in the repository root and run:

```powershell
# 1. Publish the Desktop GUI (MoonTTS.Avalonia.exe)
dotnet publish MoonTTS.Avalonia/MoonTTS.Avalonia.csproj -c Release -r win-x64 -o dist/app

# 2. Publish the CLI & Daemon (mtts.exe)
dotnet publish MoonTTS.CLI/MoonTTS.CLI.csproj -c Release -r win-x64 -o dist/app

# 3. Publish all three Engine Workers
dotnet publish Engines/Kokoro/MoonTTS.Engine.Kokoro.csproj -c Release -r win-x64 -o dist/engines/Kokoro
dotnet publish Engines/Mbrola/MoonTTS.Engine.Mbrola.csproj -c Release -r win-x64 -o dist/engines/Mbrola
dotnet publish Engines/Kitten/MoonTTS.Engine.Kitten.csproj -c Release -r win-x64 -o dist/engines/Kitten
```

#### 2.4 Step 3 — Either Build the Installer or Copy Engines for Local Testing

- **To create the `MoonTTS-Setup-1.0.0-win-x64.exe` installer:**
  ```powershell
  & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/DMyAppVersion=1.0.0" installer.iss
  ```
  The compiled installer will be created at `dist\MoonTTS-Setup-1.0.0-win-x64.exe`.

- **Or to run directly from `dist\app` without running the installer:**
  Copy the built engines into `%APPDATA%\Moon-TTS\engines`:
  ```powershell
  New-Item -ItemType Directory -Force -Path "$env:APPDATA\Moon-TTS\engines" | Out-Null
  Copy-Item -Recurse -Force dist\engines\* "$env:APPDATA\Moon-TTS\engines\"
  .\dist\app\MoonTTS.Avalonia.exe
  ```

---

## 3. macOS (`osx-arm64` / `osx-x64`)

### 3.1 Prerequisites

1. **Xcode Command Line Tools** (provides `clang` and the macOS linker required by .NET Native AOT):
   ```bash
   xcode-select --install
   ```
2. **Homebrew packages** for building `libespeak-ng.dylib`:
   ```bash
   brew install autoconf automake libtool pkg-config python3
   ```
3. **.NET 10.0 SDK** (`brew install --cask dotnet-sdk`)

### 3.2 Step-by-Step Build

Set `RID` to `osx-arm64` (Apple Silicon) or `osx-x64` (Intel):

```bash
# 1. Clone or pull the repository
git clone https://github.com/EzeRodas/Moon-TTS-CS.git
cd Moon-TTS-CS

# 2. Build libespeak-ng.dylib and espeak-ng-data
bash Engines/Mbrola/build-native.sh

# 3. Choose target RID (osx-arm64 for Apple Silicon, osx-x64 for Intel)
RID="osx-arm64"
ENGINES_DIR="$HOME/Library/Application Support/Moon-TTS/engines"

# 4. Publish the Engine Workers into ~/Library/Application Support/Moon-TTS/engines/
dotnet publish Engines/Kokoro/MoonTTS.Engine.Kokoro.csproj -c Release -r "$RID" -o "$ENGINES_DIR/Kokoro"
dotnet publish Engines/Mbrola/MoonTTS.Engine.Mbrola.csproj -c Release -r "$RID" -o "$ENGINES_DIR/Mbrola"
dotnet publish Engines/Kitten/MoonTTS.Engine.Kitten.csproj -c Release -r "$RID" -o "$ENGINES_DIR/Kitten"

# 5. Publish the Desktop GUI and CLI (Native AOT)
dotnet publish MoonTTS.Avalonia/MoonTTS.Avalonia.csproj -c Release -r "$RID" -o dist/app
dotnet publish MoonTTS.CLI/MoonTTS.CLI.csproj -c Release -r "$RID" -o dist/app
```

---

## Summary of Runtime Directories by OS

| Purpose | Linux | Windows | macOS |
| :--- | :--- | :--- | :--- |
| **Config & Profiles** | `~/.config/Moon-TTS` | `%APPDATA%\Moon-TTS` | `~/Library/Application Support/Moon-TTS` |
| **Installed Engines** | `~/.local/share/Moon-TTS/engines` | `%APPDATA%\Moon-TTS\engines` | `~/Library/Application Support/Moon-TTS/engines` |
| **Downloaded Models** | `~/.local/share/Moon-TTS/models` | `%APPDATA%\Moon-TTS\models` | `~/Library/Application Support/Moon-TTS/models` |
