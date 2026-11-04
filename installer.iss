#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "Moon-TTS"
#define MyAppPublisher "EzeRodas"
#define MyAppURL "https://github.com/EzeRodas/Moon-TTS-CS"
#define MyAppExeName "MoonTTS.Avalonia.exe"

[Setup]
AppId={{8F4E7A21-5B9C-4D3E-A1F2-9C8B7D6E5F4A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=dist
OutputBaseFilename=MoonTTS-Setup-{#MyAppVersion}-win-x64
SetupIconFile=MoonTTS.Avalonia\Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Components]
Name: "core"; Description: "Moon-TTS Application & CLI"; Types: full compact custom; Flags: fixed
Name: "engines\kokoro"; Description: "Kokoro ONNX Neural Engine"; Types: full compact
Name: "engines\kitten"; Description: "Kitten TTS Lightweight ONNX Engine"; Types: full
Name: "engines\mbrola"; Description: "MBROLA Multilingual Diphone Engine"; Types: full

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start {#MyAppName} automatically when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Main GUI (MoonTTS.Avalonia.exe), CLI (mtts.exe), and runtime DLLs
Source: "dist\app\*"; DestDir: "{app}"; Components: core; Flags: ignoreversion recursesubdirs createallsubdirs

; Engine workers installed to %APPDATA%\Moon-TTS\engines
Source: "dist\engines\Kokoro\*"; DestDir: "{userappdata}\Moon-TTS\engines\Kokoro"; Components: engines\kokoro; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\engines\Kitten\*"; DestDir: "{userappdata}\Moon-TTS\engines\Kitten"; Components: engines\kitten; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\engines\Mbrola\*"; DestDir: "{userappdata}\Moon-TTS\engines\Mbrola"; Components: engines\mbrola; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
