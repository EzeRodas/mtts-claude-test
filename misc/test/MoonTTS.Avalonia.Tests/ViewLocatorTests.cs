using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MoonTTS.Avalonia.Tests.TestSupport;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Avalonia.Views;

namespace MoonTTS.Avalonia.Tests;

/* Verifies the reflection-free view locator */
public sealed class ViewLocatorTests
{
    private readonly ViewLocator _locator = new();

    /* The HUD view model maps to the HUD window */
    [AvaloniaFact]
    public void Build_MainViewModel_ReturnsMainWindow()
    {
        Assert.IsType<MainWindow>(_locator.Build(new MainViewModel()));
    }

    /* The settings view model maps to the settings window */
    [AvaloniaFact]
    public void Build_SettingsViewModel_ReturnsSettingsWindow()
    {
        Assert.IsType<SettingsWindow>(_locator.Build(new PreviewSettingsViewModel()));
    }

    /* Anything else shows a "Not Found" text naming the type */
    [AvaloniaFact]
    public void Build_UnknownObject_ReturnsNotFoundText()
    {
        var view = Assert.IsType<TextBlock>(_locator.Build("data"));

        Assert.Equal("Not Found: System.String", view.Text);
    }

    /* Null shows an empty "Not Found" text */
    [AvaloniaFact]
    public void Build_Null_ReturnsNotFoundText()
    {
        var view = Assert.IsType<TextBlock>(_locator.Build(null));

        Assert.Equal("Not Found: ", view.Text);
    }

    /* View models are handled by the locator */
    [Fact]
    public void Match_ViewModel_IsTrue()
    {
        Assert.True(_locator.Match(new MainViewModel()));
    }

    /* Other data is left to other templates */
    [Fact]
    public void Match_OtherObject_IsFalse()
    {
        Assert.False(_locator.Match("data"));
    }

    /* Null is not a view model */
    [Fact]
    public void Match_Null_IsFalse()
    {
        Assert.False(_locator.Match(null));
    }
}
