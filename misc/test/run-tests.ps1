# Single entry point for every Moon-TTS unit test. Extra arguments go to `dotnet test`,
# e.g.  ./run-tests.ps1 --filter "FullyQualifiedName~ModelId"
dotnet test (Join-Path $PSScriptRoot "MoonTTS.Tests.slnx") @args
exit $LASTEXITCODE
