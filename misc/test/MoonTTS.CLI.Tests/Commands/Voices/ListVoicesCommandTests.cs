using MoonTTS.CLI.commands;
using MoonTTS.CLI.Tests.Support;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.CLI.Tests.Commands.Voices;

[Collection(TestCollections.Environment)]
public sealed class ListVoicesCommandTests : IDisposable
{
    private readonly CliTestHost _host = new();

    /* Releases the host */
    public void Dispose() => _host.Dispose();

    /* Activates "kokoro/v1" with three voices and returns its engine */
    private FakeTtsEngine ActivateWithVoices()
    {
        var engine = _host.ActivateModel(_host.AddDownloadedModel("kokoro", "v1"));
        engine.Voices.Add(new VoiceInfo("af_heart", "Heart", "en-US"));
        engine.Voices.Add(new VoiceInfo("bf_emma", "Emma", "en-GB"));
        engine.Voices.Add(new VoiceInfo("jf_alpha", "Alpha", "ja-JP"));
        return engine;
    }

    /* Build creates the "list" command with an optional "filter" argument */
    [Fact]
    public void Build_Always_CreatesListCommandWithOptionalFilter()
    {
        var command = ListVoicesCommand.Build(_host.Services);

        Assert.Equal("list", command.Name);
        var filter = Assert.Single(command.Arguments);
        Assert.Equal("filter", filter.Name);
        Assert.Equal(0, filter.Arity.MinimumNumberOfValues);
    }

    /* Without a loaded model the command fails and explains how to select one */
    [Fact]
    public async Task List_NoModelLoaded_ReturnsErrorWithHint()
    {
        var run = await _host.RunAsync(ListVoicesCommand.Build);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Error: No TTS model is currently loaded.", run.Output);
        Assert.Contains("mtts model set <MODEL>", run.Output);
    }

    /* A model without voices only gets a warning */
    [Fact]
    public async Task List_NoVoices_WarnsAndReturnsZero()
    {
        _host.ActivateModel(_host.AddDownloadedModel("kokoro", "v1"));

        var run = await _host.RunAsync(ListVoicesCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No voices available for the active model.", run.Output);
    }

    /* Without a filter every voice is listed under the active model's title */
    [Fact]
    public async Task List_NoFilter_ListsAllVoices()
    {
        ActivateWithVoices();

        var run = await _host.RunAsync(ListVoicesCommand.Build);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Available Voices for 'kokoro/v1'", run.Output);
        Assert.Contains("Heart", CliOutput.LineContaining(run.Output, "af_heart"));
        Assert.Contains("en-GB", CliOutput.LineContaining(run.Output, "bf_emma"));
        Assert.Contains("ja-JP", CliOutput.LineContaining(run.Output, "jf_alpha"));
        Assert.Contains("Showing 3 of 3 voices.", run.Output);
    }

    /* A whitespace filter is ignored */
    [Fact]
    public async Task List_WhitespaceFilter_ListsAllVoices()
    {
        ActivateWithVoices();

        var run = await _host.RunAsync(ListVoicesCommand.Build, "   ");

        Assert.Contains("Showing 3 of 3 voices.", run.Output);
    }

    /* The filter matches voice ids, ignoring case */
    [Fact]
    public async Task List_FilterById_ShowsOnlyMatchingVoices()
    {
        ActivateWithVoices();

        var run = await _host.RunAsync(ListVoicesCommand.Build, "BF_");

        Assert.Contains("bf_emma", run.Output);
        Assert.DoesNotContain("af_heart", run.Output);
        Assert.Contains("Showing 1 of 3 voices.", run.Output);
    }

    /* The filter matches voice names */
    [Fact]
    public async Task List_FilterByName_ShowsOnlyMatchingVoices()
    {
        ActivateWithVoices();

        var run = await _host.RunAsync(ListVoicesCommand.Build, "heart");

        Assert.Contains("af_heart", run.Output);
        Assert.DoesNotContain("bf_emma", run.Output);
    }

    /* The filter matches voice languages */
    [Fact]
    public async Task List_FilterByLanguage_ShowsOnlyMatchingVoices()
    {
        ActivateWithVoices();

        var run = await _host.RunAsync(ListVoicesCommand.Build, "en-");

        Assert.Contains("af_heart", run.Output);
        Assert.Contains("bf_emma", run.Output);
        Assert.DoesNotContain("jf_alpha", run.Output);
        Assert.Contains("Showing 2 of 3 voices.", run.Output);
    }

    /* A filter matching nothing only gets a warning */
    [Fact]
    public async Task List_FilterWithoutMatches_WarnsAndReturnsZero()
    {
        ActivateWithVoices();

        var run = await _host.RunAsync(ListVoicesCommand.Build, "zz");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("No voices matched filter 'zz'.", run.Output);
    }

    /* The engine's active voice is marked ACTIVE (ignoring case) and the others are not */
    [Fact]
    public async Task List_ActiveVoice_MarkedActive()
    {
        var engine = ActivateWithVoices();
        engine.ActiveVoice = "BF_EMMA";

        var run = await _host.RunAsync(ListVoicesCommand.Build);

        Assert.Contains("ACTIVE", CliOutput.LineContaining(run.Output, "bf_emma"));
        Assert.DoesNotContain("ACTIVE", CliOutput.LineContaining(run.Output, "af_heart"));
    }

    /* Voices without name or language show dashes in those columns */
    [Fact]
    public async Task List_VoiceWithoutNameOrLanguage_ShowsDashes()
    {
        var engine = _host.ActivateModel(_host.AddDownloadedModel("kokoro", "v1"));
        engine.Voices.Add(new VoiceInfo("bare_voice"));

        var run = await _host.RunAsync(ListVoicesCommand.Build);

        var cells = CliOutput.LineContaining(run.Output, "bare_voice")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["bare_voice", "-", "-", "-"], cells);
    }
}
