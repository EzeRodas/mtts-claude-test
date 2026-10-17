#!/bin/sh
# Single entry point for every Moon-TTS unit test. Extra arguments go to `dotnet test`,
# e.g.  ./run-tests.sh --filter "FullyQualifiedName~ModelId"
exec dotnet test "$(dirname "$0")/MoonTTS.Tests.slnx" "$@"
