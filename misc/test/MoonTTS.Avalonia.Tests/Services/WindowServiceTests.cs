using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using MoonTTS.Avalonia.Services;
using MoonTTS.Avalonia.Tests.TestSupport;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Avalonia.Views;

namespace MoonTTS.Avalonia.Tests.Services;

/* Verifies WindowService on the headless windowing system (one 1920x1280 screen at scaling 1).
   HUD bottom-center on that screen is (575, 1218): x = (1920 - 770) / 2, y = 1280 - 50 - 12 */
public sealed class WindowServiceTests : IDisposable
{
    private static readonly PixelPoint BottomCenter = new(575, 1218);

    private readonly GuiServices _services = new();
    private readonly OpenedWindowRecorder _opened = new();
    private readonly (IClassicDesktopStyleApplicationLifetime Lifetime, FakeDesktopLifetime State) _desktop = FakeDesktopLifetime.Create();
    private MainWindow? _main;
    private WindowService? _service;

    /* The HUD window handed to the service */
    private MainWindow Main => _main ??= new MainWindow();

    /* The service under test, built exactly as App does */
    private WindowService Service => _service ??= new WindowService(
        Main, _desktop.Lifetime, _services.Settings, _services.Player, _services.Downloads,
        _services.Dictionary, _services.Hotkeys, _services.History, _services.Provider);

    /* The settings window the service created lazily */
    private SettingsWindow Settings => _opened.Single<SettingsWindow>();

    /* Shows the HUD through the service and lets the OS report its activation */
    private void ShowHud()
    {
        Service.ShowMainWindow();
        GuiOs.Pump();
    }

    /* Hides the HUD through the service and lets the OS report that it lost focus */
    private void HideHud()
    {
        Service.HideMainWindow();
        GuiOs.Pump();
    }

    /* Releases the recorder and the services */
    public void Dispose()
    {
        _opened.Dispose();
        _services.Dispose();
    }

    /* ---------- ShowMainWindow ---------- */

    /* The first show registers the HUD as the desktop's main window */
    [AvaloniaFact]
    public void ShowMainWindow_NoMainWindow_RegistersHud()
    {
        Service.ShowMainWindow();

        Assert.Same(Main, _desktop.Lifetime.MainWindow);
    }

    /* An existing desktop main window is kept */
    [AvaloniaFact]
    public void ShowMainWindow_DesktopHasMainWindow_KeepsIt()
    {
        var other = new Window();
        _desktop.Lifetime.MainWindow = other;

        Service.ShowMainWindow();

        Assert.Same(other, _desktop.Lifetime.MainWindow);
    }

    /* A hidden HUD is shown */
    [AvaloniaFact]
    public void ShowMainWindow_Hidden_ShowsHud()
    {
        Service.ShowMainWindow();

        Assert.True(Main.IsVisible);
    }

    /* A shown HUD is activated (focused) */
    [AvaloniaFact]
    public void ShowMainWindow_Hidden_ActivatesHud()
    {
        ShowHud();

        Assert.True(Main.IsActive);
    }

    /* Showing an already active HUD hides it (tray click toggles) */
    [AvaloniaFact]
    public void ShowMainWindow_Active_HidesHud()
    {
        ShowHud();

        Service.ShowMainWindow();

        Assert.False(Main.IsVisible);
    }

    /* By default the HUD opens at the bottom center of the screen */
    [AvaloniaFact]
    public void ShowMainWindow_DefaultConfig_PlacesHudAtBottomCenter()
    {
        ShowHud();

        Assert.Equal(BottomCenter, Main.Position);
    }

    /* A remembered on-screen position is restored when the HUD opens */
    [AvaloniaFact]
    public void ShowMainWindow_RememberedPositionOnScreen_RestoresIt()
    {
        _services.Config.RememberPosition = true;
        _services.Config.PosX = 100;
        _services.Config.PosY = 200;

        ShowHud();

        Assert.Equal(new PixelPoint(100, 200), Main.Position);
    }

    /* A remembered position on a disconnected monitor falls back to bottom center */
    [AvaloniaFact]
    public void ShowMainWindow_RememberedPositionOffScreen_PlacesHudAtBottomCenter()
    {
        _services.Config.RememberPosition = true;
        _services.Config.PosX = 5000;
        _services.Config.PosY = 5000;

        ShowHud();

        Assert.Equal(BottomCenter, Main.Position);
    }

    /* A remembered position that was never set (0, 0) falls back to bottom center */
    [AvaloniaFact]
    public void ShowMainWindow_RememberedPositionUnset_PlacesHudAtBottomCenter()
    {
        _services.Config.RememberPosition = true;

        ShowHud();

        Assert.Equal(BottomCenter, Main.Position);
    }

    /* With position memory on, re-showing the HUD returns it where the user left it */
    [AvaloniaFact]
    public void ShowMainWindow_RememberEnabled_RestoresLastPosition()
    {
        _services.Config.RememberPosition = true;
        ShowHud();
        Main.Position = new PixelPoint(300, 400);
        HideHud();
        Main.Position = new PixelPoint(10, 10);

        ShowHud();

        Assert.Equal(new PixelPoint(300, 400), Main.Position);
    }

    /* With position memory off, re-showing the HUD centers it again */
    [AvaloniaFact]
    public void ShowMainWindow_RememberDisabled_ReCentersHud()
    {
        ShowHud();
        Main.Position = new PixelPoint(300, 400);
        HideHud();

        ShowHud();

        Assert.Equal(BottomCenter, Main.Position);
    }

    /* ---------- Position memory ---------- */

    /* Dragging the visible HUD saves its position when position memory is on */
    [AvaloniaFact]
    public void HudMoved_RememberEnabled_SavesPosition()
    {
        _services.Config.RememberPosition = true;
        ShowHud();

        Main.Position = new PixelPoint(321, 123);

        Assert.Equal((321, 123), (_services.Config.PosX, _services.Config.PosY));
    }

    /* Dragging the HUD saves nothing when position memory is off */
    [AvaloniaFact]
    public void HudMoved_RememberDisabled_DoesNotSave()
    {
        ShowHud();
        int saves = _services.Repo.AppConfigSaveCount;

        Main.Position = new PixelPoint(321, 123);

        Assert.Equal(saves, _services.Repo.AppConfigSaveCount);
    }

    /* Moves of the hidden HUD are ignored */
    [AvaloniaFact]
    public void HudMoved_WhileHidden_DoesNotSave()
    {
        _services.Config.RememberPosition = true;
        _ = Service;
        int saves = _services.Repo.AppConfigSaveCount;

        Main.Position = new PixelPoint(321, 123);

        Assert.Equal(saves, _services.Repo.AppConfigSaveCount);
    }

    /* ---------- HideMainWindow / HideOnSynth ---------- */

    /* Hiding the HUD hides its window */
    [AvaloniaFact]
    public void HideMainWindow_Visible_HidesHud()
    {
        ShowHud();

        Service.HideMainWindow();

        Assert.False(Main.IsVisible);
    }

    /* Hiding the HUD also hides an open settings window */
    [AvaloniaFact]
    public void HideMainWindow_SettingsOpen_HidesSettings()
    {
        ShowHud();
        Service.ToggleSettingsWindow();

        Service.HideMainWindow();

        Assert.False(Settings.IsVisible);
    }

    /* With "hide on synthesis" on, speaking hides the HUD */
    [AvaloniaFact]
    public void HideOnSynth_Enabled_HidesHud()
    {
        _services.Config.HideOnSynth = true;
        ShowHud();

        Service.HideOnSynth();

        Assert.False(Main.IsVisible);
    }

    /* With "hide on synthesis" off, speaking keeps the HUD visible */
    [AvaloniaFact]
    public void HideOnSynth_Disabled_KeepsHudVisible()
    {
        ShowHud();

        Service.HideOnSynth();

        Assert.True(Main.IsVisible);
    }

    /* ---------- Settings window ---------- */

    /* The first toggle opens the settings window */
    [AvaloniaFact]
    public void ToggleSettingsWindow_FirstCall_OpensSettings()
    {
        ShowHud();

        Service.ToggleSettingsWindow();

        Assert.True(Settings.IsVisible);
    }

    /* The settings window is activated so clicking outside it can dismiss it */
    [AvaloniaFact]
    public void ToggleSettingsWindow_FirstCall_ActivatesSettings()
    {
        ShowHud();

        Service.ToggleSettingsWindow();
        GuiOs.Pump();

        Assert.True(Settings.IsActive);
    }

    /* The settings window is bound to a view model working on the shared services */
    [AvaloniaFact]
    public void ToggleSettingsWindow_FirstCall_BindsViewModelToServices()
    {
        ShowHud();

        Service.ToggleSettingsWindow();
        Assert.IsType<SettingsViewModel>(Settings.DataContext).PrimaryVolume = 5;

        Assert.Equal(5, _services.Config.PrimaryVolume);
    }

    /* The Quick view opens directly above the HUD's settings button: x = 575 + 120, y = 1218 - (550 + 8) */
    [AvaloniaFact]
    public void ToggleSettingsWindow_FirstCall_PlacesAboveSettingsButton()
    {
        ShowHud();

        Service.ToggleSettingsWindow();

        Assert.Equal(new PixelPoint(695, 660), Settings.Position);
    }

    /* Toggling an open settings window hides it */
    [AvaloniaFact]
    public void ToggleSettingsWindow_Visible_HidesSettings()
    {
        ShowHud();
        Service.ToggleSettingsWindow();

        Service.ToggleSettingsWindow();

        Assert.False(Settings.IsVisible);
    }

    /* Re-opening reuses the same settings window */
    [AvaloniaFact]
    public void ToggleSettingsWindow_Reopened_ReusesWindow()
    {
        ShowHud();
        Service.ToggleSettingsWindow();
        Service.ToggleSettingsWindow();

        Service.ToggleSettingsWindow();

        Assert.True(Settings.IsVisible);
    }

    /* Re-opening always starts on the Quick view */
    [AvaloniaFact]
    public void ToggleSettingsWindow_Reopened_StartsOnQuickView()
    {
        ShowHud();
        Service.ToggleSettingsWindow();
        var vm = Assert.IsType<SettingsViewModel>(Settings.DataContext);
        vm.IsAdvancedView = true;
        Service.ToggleSettingsWindow();

        Service.ToggleSettingsWindow();

        Assert.Equal((false, SettingsWindow.QuickWidth), (vm.IsAdvancedView, Settings.Width));
    }

    /* Hiding a settings window that was never opened does nothing */
    [AvaloniaFact]
    public void HideSettingsWindow_NeverOpened_DoesNotThrow()
    {
        var error = Record.Exception(() => Service.HideSettingsWindow());

        Assert.Null(error);
    }

    /* Hiding an open settings window hides it */
    [AvaloniaFact]
    public void HideSettingsWindow_Visible_HidesSettings()
    {
        ShowHud();
        Service.ToggleSettingsWindow();

        Service.HideSettingsWindow();

        Assert.False(Settings.IsVisible);
    }

    /* ---------- ExitApplication ---------- */

    /* Exiting shuts the desktop application down */
    [AvaloniaFact]
    public void ExitApplication_SettingsOpened_ShutsDownDesktop()
    {
        ShowHud();
        Service.ToggleSettingsWindow();

        Service.ExitApplication();

        Assert.Equal(1, _desktop.State.ShutdownCount);
    }

    /* Exiting before the settings window was ever opened still shuts down */
    [AvaloniaFact]
    public void ExitApplication_SettingsNeverOpened_ShutsDownDesktop()
    {
        ShowHud();

        Service.ExitApplication();

        Assert.Equal(1, _desktop.State.ShutdownCount);
    }

    /* Exiting lets the HUD really close instead of hiding */
    [AvaloniaFact]
    public void ExitApplication_Executed_MarksHudExiting()
    {
        ShowHud();

        Service.ExitApplication();

        Assert.True(Main.IsExiting);
    }

    /* Exiting really closes the settings window */
    [AvaloniaFact]
    public void ExitApplication_SettingsOpened_ClosesSettings()
    {
        ShowHud();
        Service.ToggleSettingsWindow();
        bool closed = false;
        Settings.Closed += (_, _) => closed = true;

        Service.ExitApplication();

        Assert.Equal((true, true), (Settings.IsExiting, closed));
    }

    /* With position memory on, exiting saves where the visible HUD is */
    [AvaloniaFact]
    public void ExitApplication_RememberEnabledHudVisible_SavesCurrentPosition()
    {
        ShowHud();
        Main.Position = new PixelPoint(222, 333);
        _services.Config.RememberPosition = true;

        Service.ExitApplication();

        Assert.Equal((222, 333), (_services.Config.PosX, _services.Config.PosY));
    }

    /* With position memory on, exiting from the tray saves where the hidden HUD was last shown */
    [AvaloniaFact]
    public void ExitApplication_RememberEnabledHudHidden_SavesLastShownPosition()
    {
        ShowHud();
        Main.Position = new PixelPoint(222, 333);
        Service.HideMainWindow();
        Main.Position = new PixelPoint(1, 1);
        _services.Config.RememberPosition = true;

        Service.ExitApplication();

        Assert.Equal((222, 333), (_services.Config.PosX, _services.Config.PosY));
    }

    /* With position memory off, exiting saves nothing */
    [AvaloniaFact]
    public void ExitApplication_RememberDisabled_DoesNotSave()
    {
        ShowHud();
        int saves = _services.Repo.AppConfigSaveCount;

        Service.ExitApplication();

        Assert.Equal(saves, _services.Repo.AppConfigSaveCount);
    }

    /* A HUD that was never shown has no position to save */
    [AvaloniaFact]
    public void ExitApplication_HudNeverShown_DoesNotSave()
    {
        _services.Config.RememberPosition = true;
        _ = Service;
        int saves = _services.Repo.AppConfigSaveCount;

        Service.ExitApplication();

        Assert.Equal(saves, _services.Repo.AppConfigSaveCount);
    }
}
