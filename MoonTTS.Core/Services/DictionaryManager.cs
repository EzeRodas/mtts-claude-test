using System.Text.Json;
using System.Text.RegularExpressions;
using MoonTTS.Core.Utilities;
using MoonTTS.Domain.Common;
using MoonTTS.Domain.Constants;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Core.Services;

/* File Structure:
        - DictionaryManager(IConfigurationRepository repo)
        - GetEntries() : IReadOnlyList<DictionaryEntry>
        - AddEntry(string original, string spelling, bool caseSensitive) : Result
        - UpdateEntry(Guid id, string original, string spelling, bool caseSensitive) : Result
        - DeleteEntry(Guid id) : Result
        - ReplaceText(string input) : string
        - RebuildRegex() : void
        - TrimString(string input, int maxLength) : string
*/

public class DictionaryManager
{
    /* Just in case */
    private const int MaxLength = 100;

    /* Config repository */
    private readonly IConfigurationRepository _repo;

    /* Regex to be used for the text replacement */
    private Regex? _cachedRegex;
    private List<DictionaryEntry> _sortedLookUpCache = new();
    
    public DictionaryManager(IConfigurationRepository repo)
    {
        _repo = repo;
        /* Automatically rebuilds the regex when the state changes */
        _repo.DictionaryChanged += RebuildRegex;
        
        RebuildRegex();
    }

    /* Returns list with all the dictionary entries */
    public IReadOnlyList<DictionaryEntry> GetEntries() => _repo.DictionaryEntries;

    /* Receives parameters, turns into DictionaryEntry object, and adds to list */
    public Result AddEntry(string original, string spelling, bool caseSensitive = false)
    {
        /* Checks for errors */
        if (string.IsNullOrWhiteSpace(original)) 
            return Result.Failure("Original word cannot be empty.");
        if (string.IsNullOrWhiteSpace(spelling))
            return Result.Failure("Replacement spelling cannot be empty.");
        if (_repo.DictionaryEntries.Count >= AppConstants.MaxDictionaryEntries)
            return Result.Failure($"Dictionary has reached its maximum limit of {AppConstants.MaxDictionaryEntries} entries.");


        string trimmedOriginal = TrimString(original, MaxLength);
        string trimmedSpelling = TrimString(spelling, MaxLength);

        if (_repo.DictionaryEntries.Any(e => string.Equals(e.Original, trimmedOriginal, StringComparison.OrdinalIgnoreCase)))
            return Result.Failure($"An entry for '{trimmedOriginal}' already exists.");

        var entries = _repo.DictionaryEntries.ToList();
        entries.Add(new DictionaryEntry
        {
            Id = Guid.NewGuid(),
            Original = trimmedOriginal,
            Spelling = trimmedSpelling,
            CaseSensitive = caseSensitive
        });

        _repo.SaveDictionary(entries);
        return Result.Success();
    }

    /* Searches for the corresponding ID, modifies it, and updates the list */
    public Result UpdateEntry(Guid id, string original, string spelling, bool caseSensitive)
    {
        /* Error checking */
        List<DictionaryEntry> entries = _repo.DictionaryEntries.ToList();
        var entry = entries.FirstOrDefault(d => d.Id == id);
        if (entry == null)
            return Result.Failure($"Dictionary entry with id {id} was not found.");
        if (string.IsNullOrWhiteSpace(original))
            return Result.Failure("Original word cannot be empty.");
        if (string.IsNullOrWhiteSpace(spelling))
            return Result.Failure("Replacement spelling cannot be empty.");

        entry.Original = TrimString(original, MaxLength);
        entry.Spelling = TrimString(spelling, MaxLength);
        entry.CaseSensitive = caseSensitive;

        
        _repo.SaveDictionary(entries);
        return Result.Success();
    }

    /* Searches for the ID and deletes it */
    public Result DeleteEntry(Guid id)
    {
        List<DictionaryEntry> entries = _repo.DictionaryEntries.ToList();
        var entry = entries.FirstOrDefault(d => d.Id == id);
        if (entry == null)
        {
            return Result.Failure($"Dictionary entry with id {id} was not found.");
        }
        entries.Remove(entry);
        
        _repo.SaveDictionary(entries);
        return Result.Success();
    }

    /* Main text replacement method */
    public string ReplaceText(string input)
    {
        /* If no entries then it does nothing */
        if (string.IsNullOrWhiteSpace(input) || _cachedRegex == null || _repo.DictionaryEntries.Count == 0) 
            return input;

        return _cachedRegex.Replace(input, match =>
        {
            for (int i = 0; i < _sortedLookUpCache.Count; i++)
            {
                if (match.Groups[i + 1].Success)
                {
                    return _sortedLookUpCache[i].Spelling;
                }
            }
            return match.Value;
        });
    }

    /* Please don't make me explain how Regex works I barely understand it myself, but if you need it here are the docs
       https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex?view=net-10.0 */
    private void RebuildRegex()
    {
        if (_repo.DictionaryEntries.Count == 0)
        {
            _cachedRegex = null;
            _sortedLookUpCache.Clear();
            return;
        }
        
        _sortedLookUpCache = _repo.DictionaryEntries.OrderByDescending(e  => e.Original.Length).ToList();

        List<string> parts = new();

        /* In short It'll look for our custom entries and replace them for the new spellings if found */
        for (int i = 0; i < _sortedLookUpCache.Count; i++)
        {
            var entry = _sortedLookUpCache[i];
            string escaped = Regex.Escape(entry.Original);
            
            /* Only adds word boundary \b if the edge character is a letter / digit */
            string prefix = char.IsLetterOrDigit(entry.Original[0]) ? @"\b" : "";
            string suffix = char.IsLetterOrDigit(entry.Original[^1]) ? @"\b" : "";

            string patternPart = $"({prefix}{escaped}{suffix})";
            parts.Add(entry.CaseSensitive ? patternPart : $@"(?i:{patternPart})");
        }
        
        string combinedPattern = string.Join("|", parts);
        
        /* Compiled flag makes it faster at run time */
       _cachedRegex = new Regex(combinedPattern, RegexOptions.Compiled); 
    }

    /* If the string received goes over the limit gets trimmed, otherwise remains untouched */
    private static string TrimString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Length > maxLength ? input[..maxLength] : input;
    }
}