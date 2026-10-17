using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MoonTTS.Avalonia.Tests.TestSupport;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Avalonia.Views;
using MoonTTS.Core.Services;

namespace MoonTTS.Avalonia.Tests.Views;

/* Verifies the HUD window code-behind and its key bindings on the headless windowing system
   (one 1920x1280 screen; the 770x50 HUD centered 12 px above the bottom edge sits at (575, 1218)) */
public sealed class MainWindowTests
{
    /* Shows a HUD window and lets the OS report its activation */
    private static MainWindow ShowHud(object? dataContext = null)
    {
        var window = new MainWindow { DataContext = dataContext };
        window.Show();
        GuiOs.Pump();
        return window;
    }

    /* The drag handle of the HUD */
    private static Border DragButton(MainWindow window) => window.GetControl<Border>("DragBtn");

    /* Center of a control in window coordinates */
    private static Point CenterOf(Control control, MainWindow window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /* Builds a HUD view model over the fake pipeline and window service */
    private static MainViewModel NewViewModel(FakeSpeechPipeline pipeline, FakeWindowService windows) =>
        new(pipeline, windows, new HotkeyManager(pipeline, new FakeAudioPlayer(), new FakeConfigurationRepository()));

    /* ---------- Construction & closing ---------- */

    /* The HUD is a 770x50 pill */
    [AvaloniaFact]
    public void Ctor_SetsHudSize()
    {
        var window = new MainWindow();

        Assert.Equal((770.0, 50.0), (window.Width, window.Height));
    }

    /* A new HUD is not exiting */
    [AvaloniaFact]
    public void IsExiting_Default_IsFalse()
    {
        var window = new MainWindow();

        Assert.False(window.IsExiting);
    }

    /* Closing the HUD (Alt+F4, window manager) hides it to the tray */
    [AvaloniaFact]
    public void Close_NotExiting_HidesWindow()
    {
        var window = ShowHud();
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        Assert.Equal((false, false), (window.IsVisible, closed));
    }

    /* A HUD hidden by close can be shown again */
    [AvaloniaFact]
    public void Close_NotExiting_KeepsWindowReusable()
    {
        var window = ShowHud();
        window.Close();

        window.Show();

        Assert.True(window.IsVisible);
    }

    /* While the application exits, closing really closes the HUD */
    [AvaloniaFact]
    public void Close_Exiting_ClosesWindow()
    {
        var window = ShowHud();
        window.IsExiting = true;
        bool closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        Assert.True(closed);
    }

    /* ---------- PositionAtBottomCenter ---------- */

    /* The HUD is centered horizontally 12 px above the bottom of the working area */
    [AvaloniaFact]
    public void PositionAtBottomCenter_Defaults_CentersAboveBottomEdge()
    {
        var window = ShowHud();

        window.PositionAtBottomCenter();

        Assert.Equal(new PixelPoint(575, 1218), window.Position);
    }

    /* A custom bottom margin is respected */
    [AvaloniaFact]
    public void PositionAtBottomCenter_CustomMargin_UsesMargin()
    {
        var window = ShowHud();

        window.PositionAtBottomCenter(bottomMargin: 40);

        Assert.Equal(new PixelPoint(575, 1190), window.Position);
    }

    /* A monitor index that does not exist falls back to the primary screen */
    [AvaloniaFact]
    public void PositionAtBottomCenter_UnknownMonitor_UsesPrimaryScreen()
    {
        var window = ShowHud();

        window.PositionAtBottomCenter(monitorIndex: 3);

        Assert.Equal(new PixelPoint(575, 1218), window.Position);
    }

    /* Without a monitor index the primary screen is used */
    [AvaloniaFact]
    public void PositionAtBottomCenter_NoMonitorIndex_UsesPrimaryScreen()
    {
        var window = ShowHud();

        window.PositionAtBottomCenter(monitorIndex: null);

        Assert.Equal(new PixelPoint(575, 1218), window.Position);
    }

    /* On a 200% display the size and margin are converted to physical pixels:
       x = (1920 - 1540) / 2, y = 1280 - 100 - 24 */
    [AvaloniaFact]
    public void PositionAtBottomCenter_HighDpi_ScalesSizeAndMargin()
    {
        var window = ShowHud();
        window.SetRenderScaling(2);

        window.PositionAtBottomCenter();

        Assert.Equal(new PixelPoint(190, 1156), window.Position);
    }

    /* ---------- Drag handle ---------- */

    /* Pressing the drag handle with the left button shows its pressed state */
    [AvaloniaFact]
    public void DragButton_LeftPressed_ShowsPressedState()
    {
        var window = ShowHud();
        var drag = DragButton(window);

        window.MouseDown(CenterOf(drag, window), MouseButton.Left);

        Assert.Contains("Pressed", drag.Classes);
    }

    /* Pressing the drag handle with another button does not start a drag */
    [AvaloniaFact]
    public void DragButton_RightPressed_KeepsNormalState()
    {
        var window = ShowHud();
        var drag = DragButton(window);

        window.MouseDown(CenterOf(drag, window), MouseButton.Right);

        Assert.DoesNotContain("Pressed", drag.Classes);
    }

    /* Releasing the drag handle clears its pressed state */
    [AvaloniaFact]
    public void DragButton_Released_ClearsPressedState()
    {
        var window = ShowHud();
        var drag = DragButton(window);
        var center = CenterOf(drag, window);
        window.MouseDown(center, MouseButton.Left);

        window.MouseUp(center, MouseButton.Left);

        Assert.DoesNotContain("Pressed", drag.Classes);
    }

    /* A release reported by something other than a Border is ignored */
    [AvaloniaFact]
    public void DragButton_ReleasedByNonBorder_DoesNothing()
    {
        var window = ShowHud();

        var error = Record.Exception(() => window.DragButton_PointerReleased(new Button(), null!));

        Assert.Null(error);
    }

    /* ---------- Loading & keys ---------- */

    /* Opening the HUD puts the caret in the text box so the user can type right away */
    [AvaloniaFact]
    public void OnLoaded_Shown_FocusesInputBox()
    {
        var window = ShowHud();

        Assert.Same(window.GetControl<TextBox>("InputBox"), window.FocusManager?.GetFocusedElement());
    }

    /* Escape hides the HUD through the close command */
    [AvaloniaFact]
    public void EscapeKey_Pressed_HidesHud()
    {
        var windows = new FakeWindowService();
        var window = ShowHud(NewViewModel(new FakeSpeechPipeline(), windows));

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.Equal(1, windows.HideMainWindowCount);
    }

    /* Enter in the text box speaks the typed text */
    [AvaloniaFact]
    public void EnterKey_InInputBox_SpeaksText()
    {
        var pipeline = new FakeSpeechPipeline();
        var vm = NewViewModel(pipeline, new FakeWindowService());
        var window = ShowHud(vm);
        vm.TextToSpeak = "hello";

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        GuiOs.Pump();

        Assert.Equal("hello", Assert.Single(pipeline.Requests).Text);
    }
}
