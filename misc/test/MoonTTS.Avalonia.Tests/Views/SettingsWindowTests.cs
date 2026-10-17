using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MoonTTS.Avalonia.Tests.TestSupport;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Avalonia.Views;

namespace MoonTTS.Avalonia.Tests.Views;

/* Verifies the settings window code-behind on the headless windowing system (one 1920x1280 screen).
   With the HUD (770x50) at (575, 1218):
     Quick (320x550)    -> x = 575 + 120 = 695,           y = 1218 - (550 + 8) = 660
     Advanced (760x540) -> x = 575 + 385 - 380 = 580,      y = 1218 - (540 + 8) = 670 */
public sealed class SettingsWindowTests
{
    private static readonly PixelPoint HudPosition = new(575, 1218);
    private static readonly PixelPoint QuickPosition = new(695, 660);
    private static readonly PixelPoint AdvancedPosition = new(580, 670);

    /* Shows a HUD window at `position` */
    private static MainWindow ShowHud(PixelPoint position)
    {
        var hud = new MainWindow();
        hud.Show();
        hud.Position = position;
        return hud;
    }

    /* Shows a settings window owned by a HUD at `hudPosition`, bound to `vm` */
    private static SettingsWindow ShowSettings(SettingsViewModel vm, PixelPoint? hudPosition = null)
    {
        var settings = new SettingsWindow { DataContext = vm };
        settings.SetOwnerWindow(ShowHud(hudPosition ?? HudPosition));
        settings.Show();
        GuiOs.Pump();
        return settings;
    }

    /* Presses a key with modifiers on the window, as the keyboard driver would */
    private static void Press(SettingsWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);

    /* ---------- Construction ---------- */

    /* A new settings window has the Quick view size */
    [AvaloniaFact]
    public void Ctor_StartsWithQuickSize()
    {
        var settings = new SettingsWindow();

        Assert.Equal((SettingsWindow.QuickWidth, SettingsWindow.QuickHeight), (settings.Width, settings.Height));
    }

    /* A new settings window is not exiting */
    [AvaloniaFact]
    public void IsExiting_Default_IsFalse()
    {
        var settings = new SettingsWindow();

        Assert.False(settings.IsExiting);
    }

    /* ---------- ResetToQuickView & placement ---------- */

    /* Resetting switches the view model back to the Quick view */
    [AvaloniaFact]
    public void ResetToQuickView_AdvancedView_SwitchesToQuickView()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.IsAdvancedView = true;

        settings.ResetToQuickView();

        Assert.False(vm.IsAdvancedView);
    }

    /* Resetting places the Quick view directly above the HUD's settings button */
    [AvaloniaFact]
    public void ResetToQuickView_WithOwner_PlacesAboveSettingsButton()
    {
        var settings = ShowSettings(new PreviewSettingsViewModel());

        settings.ResetToQuickView();

        Assert.Equal(QuickPosition, settings.Position);
    }

    /* Without a linked HUD there is nothing to anchor to, so the position is kept */
    [AvaloniaFact]
    public void ResetToQuickView_WithoutOwner_KeepsPosition()
    {
        var settings = new SettingsWindow { DataContext = new PreviewSettingsViewModel() };
        settings.Show();
        settings.Position = new PixelPoint(5, 5);

        settings.ResetToQuickView();

        Assert.Equal(new PixelPoint(5, 5), settings.Position);
    }

    /* Resetting restores the Quick size even without a view model */
    [AvaloniaFact]
    public void ResetToQuickView_WithoutViewModel_RestoresQuickSize()
    {
        var settings = new SettingsWindow { Width = SettingsWindow.AdvancedWidth, Height = SettingsWindow.AdvancedHeight };

        settings.ResetToQuickView();

        Assert.Equal((SettingsWindow.QuickWidth, SettingsWindow.QuickHeight), (settings.Width, settings.Height));
    }

    /* Switching to the Advanced view grows the window */
    [AvaloniaFact]
    public void AdvancedViewEnabled_ResizesToAdvancedSize()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);

        vm.IsAdvancedView = true;

        Assert.Equal((SettingsWindow.AdvancedWidth, SettingsWindow.AdvancedHeight), (settings.Width, settings.Height));
    }

    /* The Advanced view is centered horizontally over the HUD */
    [AvaloniaFact]
    public void AdvancedViewEnabled_CentersOverHud()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);

        vm.IsAdvancedView = true;

        Assert.Equal(AdvancedPosition, settings.Position);
    }

    /* Going back to the Quick view returns above the settings button */
    [AvaloniaFact]
    public void AdvancedViewDisabled_ReturnsAboveSettingsButton()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.IsAdvancedView = true;

        vm.IsAdvancedView = false;

        Assert.Equal(QuickPosition, settings.Position);
    }

    /* Near the right edge the window is pulled back inside the screen: x = 1920 - 320 - 12 */
    [AvaloniaFact]
    public void Reposition_PastRightEdge_ClampsInsideScreen()
    {
        var settings = ShowSettings(new PreviewSettingsViewModel(), new PixelPoint(1700, 1218));

        settings.ResetToQuickView();

        Assert.Equal(new PixelPoint(1588, 660), settings.Position);
    }

    /* Past the left edge the window is pushed back inside the screen with a 12 px margin */
    [AvaloniaFact]
    public void Reposition_PastLeftEdge_ClampsInsideScreen()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm, new PixelPoint(-600, 1218));

        vm.IsAdvancedView = true;

        Assert.Equal(new PixelPoint(12, 670), settings.Position);
    }

    /* With no room above the HUD the window opens below it: y = 100 + 50 + 8 */
    [AvaloniaFact]
    public void Reposition_NoRoomAbove_FlipsBelowHud()
    {
        var settings = ShowSettings(new PreviewSettingsViewModel(), new PixelPoint(575, 100));

        settings.ResetToQuickView();

        Assert.Equal(new PixelPoint(695, 158), settings.Position);
    }

    /* On a 200% display the offsets are converted to physical pixels: x = 575 + 240, y = 1218 - 1116 */
    [AvaloniaFact]
    public void Reposition_HighDpiHud_ScalesOffsets()
    {
        var hud = ShowHud(HudPosition);
        hud.SetRenderScaling(2);
        var settings = new SettingsWindow { DataContext = new PreviewSettingsViewModel() };
        settings.SetOwnerWindow(hud);

        settings.ResetToQuickView();

        Assert.Equal(new PixelPoint(815, 102), settings.Position);
    }

    /* Changes of other view-model properties leave the window where it is */
    [AvaloniaFact]
    public void OtherViewModelProperty_Changed_KeepsPosition()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        settings.Position = new PixelPoint(5, 5);

        vm.PrimaryVolume = 10;

        Assert.Equal(new PixelPoint(5, 5), settings.Position);
    }

    /* A replaced view model drives the window layout */
    [AvaloniaFact]
    public void DataContextReplaced_NewViewModelDrivesLayout()
    {
        var settings = ShowSettings(new PreviewSettingsViewModel());
        var replacement = new PreviewSettingsViewModel();
        settings.DataContext = replacement;

        replacement.IsAdvancedView = true;

        Assert.Equal(SettingsWindow.AdvancedWidth, settings.Width);
    }

    /* ---------- Click outside (Deactivated) ---------- */

    /* Losing focus cancels a hotkey recording in progress */
    [AvaloniaFact]
    public void Deactivated_WhileRecording_CancelsRecording()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        GuiOs.Deactivate(settings);

        Assert.False(vm.IsRecordingToggleHotkey);
    }

    /* Losing focus returns to the compact Quick view */
    [AvaloniaFact]
    public void Deactivated_AdvancedView_ResetsToQuickView()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.IsAdvancedView = true;

        GuiOs.Deactivate(settings);

        Assert.Equal((false, SettingsWindow.QuickWidth), (vm.IsAdvancedView, settings.Width));
    }

    /* Losing focus hides the window */
    [AvaloniaFact]
    public void Deactivated_Visible_HidesWindow()
    {
        var settings = ShowSettings(new PreviewSettingsViewModel());

        GuiOs.Deactivate(settings);

        Assert.False(settings.IsVisible);
    }

    /* ---------- Closing ---------- */

    /* Closing hides the window instead of destroying it */
    [AvaloniaFact]
    public void Close_NotExiting_HidesWindow()
    {
        var settings = ShowSettings(new PreviewSettingsViewModel());
        bool closed = false;
        settings.Closed += (_, _) => closed = true;

        settings.Close();

        Assert.Equal((false, false), (settings.IsVisible, closed));
    }

    /* Closing returns to the Quick view for the next opening */
    [AvaloniaFact]
    public void Close_NotExiting_ResetsToQuickView()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.IsAdvancedView = true;

        settings.Close();

        Assert.False(vm.IsAdvancedView);
    }

    /* While the application exits, closing really closes the window */
    [AvaloniaFact]
    public void Close_Exiting_ClosesWindow()
    {
        var settings = ShowSettings(new PreviewSettingsViewModel());
        settings.IsExiting = true;
        bool closed = false;
        settings.Closed += (_, _) => closed = true;

        settings.Close();

        Assert.True(closed);
    }

    /* ---------- Keys ---------- */

    /* Escape outside recording closes the window through the close command */
    [AvaloniaFact]
    public void EscapeKey_NotRecording_ClosesThroughCommand()
    {
        using var services = new GuiServices();
        var settings = ShowSettings(services.CreateViewModel());

        Press(settings, Key.Escape);

        Assert.Equal(1, services.Windows.HideSettingsWindowCount);
    }

    /* Escape while recording cancels the recording */
    [AvaloniaFact]
    public void EscapeKey_WhileRecording_CancelsRecording()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Press(settings, Key.Escape);

        Assert.False(vm.IsRecordingToggleHotkey);
    }

    /* Escape while recording is consumed, so it does not also close the window */
    [AvaloniaFact]
    public void EscapeKey_WhileRecording_DoesNotCloseWindow()
    {
        using var services = new GuiServices();
        var vm = services.CreateViewModel();
        var settings = ShowSettings(vm);
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Press(settings, Key.Escape);

        Assert.Equal(0, services.Windows.HideSettingsWindowCount);
    }

    /* A modifier on its own keeps waiting for the main key */
    [AvaloniaTheory]
    [InlineData(Key.LeftCtrl, RawInputModifiers.Control)]
    [InlineData(Key.RightCtrl, RawInputModifiers.Control)]
    [InlineData(Key.LeftAlt, RawInputModifiers.Alt)]
    [InlineData(Key.RightAlt, RawInputModifiers.Alt)]
    [InlineData(Key.LeftShift, RawInputModifiers.Shift)]
    [InlineData(Key.RightShift, RawInputModifiers.Shift)]
    [InlineData(Key.LWin, RawInputModifiers.Meta)]
    [InlineData(Key.RWin, RawInputModifiers.Meta)]
    public void ModifierKey_WhileRecording_KeepsRecording(Key key, RawInputModifiers modifiers)
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Press(settings, key, modifiers);

        Assert.Equal((true, "Ctrl+Alt+M"), (vm.IsRecordingToggleHotkey, vm.ToggleAppHotkey));
    }

    /* A key without any modifier cannot be a global shortcut, so recording is cancelled */
    [AvaloniaFact]
    public void PlainKey_WhileRecording_CancelsRecording()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Press(settings, Key.K);

        Assert.Equal((false, "Ctrl+Alt+M"), (vm.IsRecordingToggleHotkey, vm.ToggleAppHotkey));
    }

    /* Modifier + key combinations are recorded in ShortcutCombo's standard form */
    [AvaloniaTheory]
    [InlineData(Key.K, RawInputModifiers.Control | RawInputModifiers.Alt, "Ctrl+Alt+K")]
    [InlineData(Key.D7, RawInputModifiers.Shift, "Shift+7")]
    [InlineData(Key.NumPad3, RawInputModifiers.Control, "Ctrl+3")]
    [InlineData(Key.F12, RawInputModifiers.Alt, "Alt+F12")]
    [InlineData(Key.Space, RawInputModifiers.Control, "Ctrl+SPACE")]
    [InlineData(Key.Enter, RawInputModifiers.Control, "Ctrl+RETURN")]
    [InlineData(Key.Tab, RawInputModifiers.Control, "Ctrl+TAB")]
    [InlineData(Key.Back, RawInputModifiers.Control, "Ctrl+BACKSPACE")]
    [InlineData(Key.Delete, RawInputModifiers.Control, "Ctrl+DELETE")]
    [InlineData(Key.Insert, RawInputModifiers.Control, "Ctrl+INSERT")]
    [InlineData(Key.Home, RawInputModifiers.Control, "Ctrl+HOME")]
    [InlineData(Key.End, RawInputModifiers.Control, "Ctrl+END")]
    [InlineData(Key.PageUp, RawInputModifiers.Control, "Ctrl+PAGEUP")]
    [InlineData(Key.PageDown, RawInputModifiers.Control, "Ctrl+PAGEDOWN")]
    [InlineData(Key.Up, RawInputModifiers.Control, "Ctrl+UP")]
    [InlineData(Key.Down, RawInputModifiers.Control, "Ctrl+DOWN")]
    [InlineData(Key.Left, RawInputModifiers.Control, "Ctrl+LEFT")]
    [InlineData(Key.Right, RawInputModifiers.Control, "Ctrl+RIGHT")]
    [InlineData(Key.M, RawInputModifiers.Meta, "Win+M")]
    public void Combination_WhileRecordingToggle_RecordsToggleHotkey(Key key, RawInputModifiers modifiers, string expected)
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Press(settings, key, modifiers);

        Assert.Equal((expected, false), (vm.ToggleAppHotkey, vm.IsRecordingToggleHotkey));
    }

    /* A key with no shortcut name keeps waiting for a usable key */
    [AvaloniaFact]
    public void UnsupportedKey_WhileRecording_KeepsRecording()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.ToggleRecordingToggleHotkeyCommand.Execute(null);

        Press(settings, Key.OemComma, RawInputModifiers.Control);

        Assert.Equal((true, "Ctrl+Alt+M"), (vm.IsRecordingToggleHotkey, vm.ToggleAppHotkey));
    }

    /* While recording a phrase shortcut the combination fills the new phrase's shortcut */
    [AvaloniaFact]
    public void Combination_WhileRecordingPhrase_RecordsPhraseHotkey()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);
        vm.ToggleRecordingPhraseHotkeyCommand.Execute(null);

        Press(settings, Key.P, RawInputModifiers.Control);

        Assert.Equal(("Ctrl+P", "Ctrl+Alt+M"), (vm.NewHotkeyKey, vm.ToggleAppHotkey));
    }

    /* Outside recording key presses are left to the controls */
    [AvaloniaFact]
    public void Combination_NotRecording_IsIgnored()
    {
        var vm = new PreviewSettingsViewModel();
        var settings = ShowSettings(vm);

        Press(settings, Key.K, RawInputModifiers.Control);

        Assert.Equal(("Ctrl+Alt+M", string.Empty), (vm.ToggleAppHotkey, vm.NewHotkeyKey));
    }
}
