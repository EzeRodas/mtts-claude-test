using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.CLI.Tests.Commands.Voices;

[Collection(TestCollections.Environment)]
public sealed class SetVoiceCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Activates "kokoro/v1" with two voices and returns its engine */
    private FakeTtsEngine ActivateWithVoices()
    {
        var engine = _host.ActivateModel(_host.AddDownloadedModel("kokoro", "v1"));
        engine.Voices.Add(new VoiceInfo("af_heart", "Heart"));
        engine.Voices.Add(new VoiceInfo("bf_emma", "Emma"));
        return engine;
    }

    /* Build creates the "set" command with a required "voice" argument */
    [Fact]
    public void Build_Always_CreatesSetCommandWithVoiceArgument()
    {
        var command = SetVoiceCommand.Build(_host.Services);

        Assert.Equal("set", command.Name);
        Assert.Equal("voice", Assert.Single(command.Arguments).Name);
    }

    /* Without a loaded model the command fails */
    [Fact]
    public async Task Set_NoModelLoaded_ReturnsError()
    {
        var run = await _host.RunAsync(SetVoiceCommand.Build, "af_heart");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: No TTS model is currently loaded.", run.Output);
    }

    /* A voice the model does not offer fails with a hint and keeps the current voice */
    [Fact]
    public async Task Set_UnknownVoice_ReturnsErrorWithHint()
    {
        var engine = ActivateWithVoices();
        engine.ActiveVoice = "af_heart";

        var run = await _host.RunAsync(SetVoiceCommand.Build, "zz_missing");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: Unknown voice 'zz_missing'.", run.Output);
        Assert.Contains("mtts voice list", run.Output);
        Assert.Equal("af_heart", engine.ActiveVoice);
    }

    /* A known voice becomes the engine's active voice */
    [Fact]
    public async Task Set_KnownVoice_SetsActiveVoice()
    {
        var engine = ActivateWithVoices();

        var run = await _host.RunAsync(SetVoiceCommand.Build, "bf_emma");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("bf_emma", engine.ActiveVoice);
        Assert.Contains("Successfully switched active voice to: bf_emma", run.Output);
    }

    /* The voice id is matched ignoring case and stored with the engine's spelling */
    [Fact]
    public async Task Set_VoiceIdDifferentCase_UsesCanonicalId()
    {
        var engine = ActivateWithVoices();

        await _host.RunAsync(SetVoiceCommand.Build, "BF_EMMA");

        Assert.Equal("bf_emma", engine.ActiveVoice);
    }

    /* An engine that rejects the voice reports the reason and exits with 1 */
    [Fact]
    public async Task Set_EngineRejectsVoice_ReturnsErrorWithMessage()
    {
        var model = _host.AddDownloadedModel("kokoro", "v1");
        _host.EngineFactory.Engines[model.Id] = () => new CliVoiceRejectingEngine("disk full", new VoiceInfo("af_heart"));
        _host.Repository.AppConfig.ActiveModel = model.Id;

        var run = await _host.RunAsync(SetVoiceCommand.Build, "af_heart");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Failed to set voice: disk full", run.Output);
    }
}
