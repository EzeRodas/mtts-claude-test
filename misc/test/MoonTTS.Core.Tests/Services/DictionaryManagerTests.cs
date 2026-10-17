using MoonTTS.Core.Services;
using MoonTTS.Domain.Constants;
using MoonTTS.Domain.Entities;

namespace MoonTTS.Core.Tests.Services;

/* Tests for DictionaryManager (entry management and text replacement) */
public sealed class DictionaryManagerTests
{
    private readonly FakeConfigurationRepository _repo = new();

    /* Seeds the repository with entries before the manager is built */
    private void Seed(params DictionaryEntry[] entries) => _repo.SaveDictionary(entries.ToList());

    /* Builds an entry */
    private static DictionaryEntry Entry(string original, string spelling, bool caseSensitive = false) =>
        new() { Id = Guid.NewGuid(), Original = original, Spelling = spelling, CaseSensitive = caseSensitive };

    /* Entries already stored when the manager is built are used for replacement right away */
    [Fact]
    public void Constructor_ExistingEntries_BuildsReplacementRegex()
    {
        Seed(Entry("gg", "good game"));
        var manager = new DictionaryManager(_repo);

        string result = manager.ReplaceText("gg everyone");

        Assert.Equal("good game everyone", result);
    }

    /* GetEntries exposes the repository's entries */
    [Fact]
    public void GetEntries_SeededRepository_ReturnsRepositoryEntries()
    {
        Seed(Entry("a", "b"), Entry("c", "d"));
        var manager = new DictionaryManager(_repo);

        var entries = manager.GetEntries();

        Assert.Equal(["a", "c"], entries.Select(e => e.Original));
    }

    /* A blank original word is rejected and nothing is saved */
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddEntry_BlankOriginal_ReturnsFailure(string original)
    {
        var manager = new DictionaryManager(_repo);

        var result = manager.AddEntry(original, "spelling");

        Assert.True(result.IsFailure);
        Assert.Equal(0, _repo.DictionarySaveCount);
    }

    /* A blank spelling is rejected and nothing is saved */
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddEntry_BlankSpelling_ReturnsFailure(string spelling)
    {
        var manager = new DictionaryManager(_repo);

        var result = manager.AddEntry("word", spelling);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _repo.DictionarySaveCount);
    }

    /* A full dictionary rejects new entries */
    [Fact]
    public void AddEntry_DictionaryFull_ReturnsFailure()
    {
        Seed(Enumerable.Range(0, AppConstants.MaxDictionaryEntries).Select(i => Entry($"w{i}", "x")).ToArray());
        var manager = new DictionaryManager(_repo);

        var result = manager.AddEntry("extra", "word");

        Assert.True(result.IsFailure);
        Assert.Equal(AppConstants.MaxDictionaryEntries, _repo.DictionaryEntries.Count);
    }

    /* An original that already exists (ignoring case) is rejected */
    [Theory]
    [InlineData("lol")]
    [InlineData("LOL")]
    public void AddEntry_DuplicateOriginal_ReturnsFailure(string original)
    {
        Seed(Entry("lol", "laughing out loud"));
        var manager = new DictionaryManager(_repo);

        var result = manager.AddEntry(original, "other");

        Assert.True(result.IsFailure);
        Assert.Single(_repo.DictionaryEntries);
    }

    /* A valid entry is saved with its values and a fresh id */
    [Fact]
    public void AddEntry_ValidEntry_SavesEntry()
    {
        var manager = new DictionaryManager(_repo);

        var result = manager.AddEntry("brb", "be right back", caseSensitive: true);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(_repo.DictionaryEntries);
        Assert.Equal("brb", entry.Original);
        Assert.Equal("be right back", entry.Spelling);
        Assert.True(entry.CaseSensitive);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    /* Entries are case-insensitive unless asked otherwise */
    [Fact]
    public void AddEntry_DefaultCaseSensitivity_IsCaseInsensitive()
    {
        var manager = new DictionaryManager(_repo);

        manager.AddEntry("brb", "be right back");

        Assert.False(Assert.Single(_repo.DictionaryEntries).CaseSensitive);
    }

    /* Originals and spellings longer than 100 characters are truncated to 100 */
    [Fact]
    public void AddEntry_OverlongValues_AreTruncatedTo100Characters()
    {
        var manager = new DictionaryManager(_repo);

        manager.AddEntry(new string('a', 150), new string('b', 120));

        var entry = Assert.Single(_repo.DictionaryEntries);
        Assert.Equal(new string('a', 100), entry.Original);
        Assert.Equal(new string('b', 100), entry.Spelling);
    }

    /* A newly added entry takes part in replacement immediately */
    [Fact]
    public void AddEntry_ValidEntry_IsUsedByReplaceText()
    {
        var manager = new DictionaryManager(_repo);

        manager.AddEntry("afk", "away from keyboard");

        Assert.Equal("I am away from keyboard", manager.ReplaceText("I am afk"));
    }

    /* Updating an unknown id fails without saving */
    [Fact]
    public void UpdateEntry_UnknownId_ReturnsFailure()
    {
        var manager = new DictionaryManager(_repo);

        var result = manager.UpdateEntry(Guid.NewGuid(), "a", "b", false);

        Assert.True(result.IsFailure);
        Assert.Equal(0, _repo.DictionarySaveCount);
    }

    /* Updating with a blank original fails and keeps the entry */
    [Fact]
    public void UpdateEntry_BlankOriginal_ReturnsFailure()
    {
        var entry = Entry("a", "b");
        Seed(entry);
        var manager = new DictionaryManager(_repo);

        var result = manager.UpdateEntry(entry.Id, " ", "c", false);

        Assert.True(result.IsFailure);
        Assert.Equal("a", _repo.DictionaryEntries[0].Original);
    }

    /* Updating with a blank spelling fails and keeps the entry */
    [Fact]
    public void UpdateEntry_BlankSpelling_ReturnsFailure()
    {
        var entry = Entry("a", "b");
        Seed(entry);
        var manager = new DictionaryManager(_repo);

        var result = manager.UpdateEntry(entry.Id, "a", "", false);

        Assert.True(result.IsFailure);
        Assert.Equal("b", _repo.DictionaryEntries[0].Spelling);
    }

    /* A valid update changes every field and saves */
    [Fact]
    public void UpdateEntry_ValidUpdate_SavesNewValues()
    {
        var entry = Entry("a", "b");
        Seed(entry);
        int savesBefore = _repo.DictionarySaveCount;
        var manager = new DictionaryManager(_repo);

        var result = manager.UpdateEntry(entry.Id, "new", "spelling", true);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(_repo.DictionaryEntries);
        Assert.Equal(entry.Id, saved.Id);
        Assert.Equal("new", saved.Original);
        Assert.Equal("spelling", saved.Spelling);
        Assert.True(saved.CaseSensitive);
        Assert.Equal(savesBefore + 1, _repo.DictionarySaveCount);
    }

    /* Updated values are truncated to 100 characters like added ones */
    [Fact]
    public void UpdateEntry_OverlongValues_AreTruncatedTo100Characters()
    {
        var entry = Entry("a", "b");
        Seed(entry);
        var manager = new DictionaryManager(_repo);

        manager.UpdateEntry(entry.Id, new string('x', 101), new string('y', 300), false);

        Assert.Equal(new string('x', 100), _repo.DictionaryEntries[0].Original);
        Assert.Equal(new string('y', 100), _repo.DictionaryEntries[0].Spelling);
    }

    /* After an update the new spelling is used for replacement */
    [Fact]
    public void UpdateEntry_ValidUpdate_IsUsedByReplaceText()
    {
        var entry = Entry("btw", "by the way");
        Seed(entry);
        var manager = new DictionaryManager(_repo);

        manager.UpdateEntry(entry.Id, "btw", "between", false);

        Assert.Equal("between us", manager.ReplaceText("btw us"));
    }

    /* Deleting an unknown id fails without saving */
    [Fact]
    public void DeleteEntry_UnknownId_ReturnsFailure()
    {
        var manager = new DictionaryManager(_repo);

        var result = manager.DeleteEntry(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal(0, _repo.DictionarySaveCount);
    }

    /* Deleting removes only the matching entry and saves */
    [Fact]
    public void DeleteEntry_KnownId_RemovesEntry()
    {
        var keep = Entry("keep", "k");
        var remove = Entry("remove", "r");
        Seed(keep, remove);
        var manager = new DictionaryManager(_repo);

        var result = manager.DeleteEntry(remove.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal([keep.Id], _repo.DictionaryEntries.Select(e => e.Id));
    }

    /* A deleted entry no longer takes part in replacement, even when it was the last one */
    [Fact]
    public void DeleteEntry_LastEntry_StopsReplacing()
    {
        var entry = Entry("omg", "oh my god");
        Seed(entry);
        var manager = new DictionaryManager(_repo);

        manager.DeleteEntry(entry.Id);

        Assert.Equal("omg", manager.ReplaceText("omg"));
    }

    /* Blank input is returned unchanged */
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ReplaceText_BlankInput_ReturnsInput(string input)
    {
        Seed(Entry("a", "b"));
        var manager = new DictionaryManager(_repo);

        Assert.Equal(input, manager.ReplaceText(input));
    }

    /* Without entries the text is returned unchanged */
    [Fact]
    public void ReplaceText_NoEntries_ReturnsInput()
    {
        var manager = new DictionaryManager(_repo);

        Assert.Equal("nothing to replace", manager.ReplaceText("nothing to replace"));
    }

    /* Only whole words are replaced, never parts of other words */
    [Fact]
    public void ReplaceText_WordInsideAnotherWord_IsNotReplaced()
    {
        Seed(Entry("cat", "dog"));
        var manager = new DictionaryManager(_repo);

        Assert.Equal("concatenate dog", manager.ReplaceText("concatenate cat"));
    }

    /* Case-insensitive entries match any casing */
    [Fact]
    public void ReplaceText_CaseInsensitiveEntry_MatchesAnyCase()
    {
        Seed(Entry("nasa", "N A S A"));
        var manager = new DictionaryManager(_repo);

        Assert.Equal("N A S A and N A S A", manager.ReplaceText("NASA and nasa"));
    }

    /* Case-sensitive entries only match their exact casing */
    [Fact]
    public void ReplaceText_CaseSensitiveEntry_MatchesExactCaseOnly()
    {
        Seed(Entry("US", "United States", caseSensitive: true));
        var manager = new DictionaryManager(_repo);

        Assert.Equal("United States, not us", manager.ReplaceText("US, not us"));
    }

    /* When entries overlap the longest original wins */
    [Fact]
    public void ReplaceText_OverlappingEntries_PrefersLongestOriginal()
    {
        Seed(Entry("New", "Old"), Entry("New York", "Big Apple"));
        var manager = new DictionaryManager(_repo);

        Assert.Equal("Big Apple is Old", manager.ReplaceText("New York is New"));
    }

    /* Originals ending in symbols still match (no word boundary is required at a symbol edge) */
    [Fact]
    public void ReplaceText_OriginalWithSymbolEdge_IsReplaced()
    {
        Seed(Entry("C++", "C plus plus"));
        var manager = new DictionaryManager(_repo);

        Assert.Equal("I write C plus plus daily", manager.ReplaceText("I write C++ daily"));
    }

    /* Regex metacharacters in an original are matched literally */
    [Fact]
    public void ReplaceText_OriginalWithRegexMetacharacters_IsMatchedLiterally()
    {
        Seed(Entry("a.b", "dot"));
        var manager = new DictionaryManager(_repo);

        Assert.Equal("axb dot", manager.ReplaceText("axb a.b"));
    }

    /* Spellings are inserted literally, so substitution tokens like $1 are not expanded */
    [Fact]
    public void ReplaceText_SpellingWithSubstitutionToken_IsInsertedLiterally()
    {
        Seed(Entry("price", "$1"));
        var manager = new DictionaryManager(_repo);

        Assert.Equal("$1 tag", manager.ReplaceText("price tag"));
    }

    /* Every occurrence of every entry is replaced in one pass */
    [Fact]
    public void ReplaceText_MultipleEntriesAndOccurrences_ReplacesAll()
    {
        Seed(Entry("gg", "good game"), Entry("wp", "well played"));
        var manager = new DictionaryManager(_repo);

        Assert.Equal("good game well played, good game", manager.ReplaceText("gg wp, gg"));
    }

    /* Entries saved straight to the repository (e.g. by another process) are picked up through DictionaryChanged */
    [Fact]
    public void ReplaceText_RepositoryChangedExternally_UsesNewEntries()
    {
        var manager = new DictionaryManager(_repo);

        _repo.SaveDictionary([Entry("tbh", "to be honest")]);

        Assert.Equal("to be honest", manager.ReplaceText("tbh"));
    }
}
