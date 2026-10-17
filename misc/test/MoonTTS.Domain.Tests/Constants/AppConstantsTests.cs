using MoonTTS.Domain.Constants;

namespace MoonTTS.Domain.Tests.Constants;

/* Pins the application-wide limits so a change to them is a deliberate decision */
public sealed class AppConstantsTests
{
    /* At most 20 history entries are kept */
    [Fact]
    public void MaxHistoryEntries_Value_IsTwenty()
    {
        Assert.Equal(20, AppConstants.MaxHistoryEntries);
    }

    /* At most 20 hotkey entries are allowed per profile */
    [Fact]
    public void MaxHotkeyEntries_Value_IsTwenty()
    {
        Assert.Equal(20, AppConstants.MaxHotkeyEntries);
    }

    /* At most 500 dictionary entries are allowed */
    [Fact]
    public void MaxDictionaryEntries_Value_IsFiveHundred()
    {
        Assert.Equal(500, AppConstants.MaxDictionaryEntries);
    }

    /* Text is only split into chunks once it reaches 80 characters */
    [Fact]
    public void MinSplittingLenght_Value_IsEighty()
    {
        Assert.Equal(80, AppConstants.MinSplittingLenght);
    }
}
