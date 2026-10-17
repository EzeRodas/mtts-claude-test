using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

namespace MoonTTS.Avalonia.Tests.TestSupport;

/* File Structure:
        - FakeDesktopLifetime
            - Create() : (IClassicDesktopStyleApplicationLifetime, FakeDesktopLifetime)
            - Invoke(MethodInfo? targetMethod, object?[]? args) : object?
        - OpenedWindowRecorder
            - OpenedWindowRecorder()
            - Single<T>() : T
            - Dispose() : void
        - GuiOs
            - Pump() : void
            - Deactivate(Window window) : void
*/

/* Desktop session standing in for the OS: remembers the main window and counts shutdown requests instead of
   stopping the dispatcher (the real ClassicDesktopStyleApplicationLifetime shuts the dispatcher down, which
   leaves Avalonia's static state unusable for the following headless tests). Avalonia's reference assembly
   forbids implementing the lifetime interface in code, so the dummy is generated at runtime by DispatchProxy */
public class FakeDesktopLifetime : DispatchProxy
{
    private Window? _mainWindow;
    private ShutdownMode _shutdownMode;

    public int ShutdownCount { get; private set; }

    /* Builds the lifetime handed to the code under test together with its recorded state */
    public static (IClassicDesktopStyleApplicationLifetime Lifetime, FakeDesktopLifetime State) Create()
    {
        var lifetime = Create<IClassicDesktopStyleApplicationLifetime, FakeDesktopLifetime>();
        return (lifetime, (FakeDesktopLifetime)(object)lifetime);
    }

    /* Answers the lifetime members the way a desktop session would */
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "get_MainWindow":
                return _mainWindow;
            case "set_MainWindow":
                _mainWindow = (Window?)args![0];
                return null;
            case "get_ShutdownMode":
                return _shutdownMode;
            case "set_ShutdownMode":
                _shutdownMode = (ShutdownMode)args![0]!;
                return null;
            case "get_Windows":
                return Array.Empty<Window>();
            case "get_Args":
                return Array.Empty<string>();
            case "Shutdown":
                ShutdownCount++;
                return null;
            case "TryShutdown":
                ShutdownCount++;
                return true;
            case { } name when name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal):
                return null;
            default:
                throw new NotSupportedException($"FakeDesktopLifetime does not implement {targetMethod?.Name}");
        }
    }
}

/* Captures every window opened while it is alive (e.g. the SettingsWindow WindowService creates lazily) */
public sealed class OpenedWindowRecorder : IDisposable
{
    private readonly IDisposable _subscription;
    private readonly List<Window> _windows = new();

    /* Subscribes to the global WindowOpened event */
    public OpenedWindowRecorder()
    {
        _subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => _windows.Add(window), RoutingStrategies.Direct);
    }

    /* The only distinct window of type T opened so far */
    public T Single<T>() where T : Window => _windows.OfType<T>().Distinct().Single();

    /* Stops recording */
    public void Dispose() => _subscription.Dispose();
}

/* Drives the headless windowing system the way the OS would */
public static class GuiOs
{
    /* Runs every job posted to the dispatcher (activation/deactivation notifications, Task.Yield continuations) */
    public static void Pump() => Dispatcher.UIThread.RunJobs();

    /* Reports that the window lost focus, as the OS does when the user clicks outside it. Avalonia hides the
       platform callback from user code, so it is read through reflection (tests are not held to the AOT rules) */
    public static void Deactivate(Window window)
    {
        var callback = typeof(IWindowBaseImpl).GetProperty("Deactivated")!.GetValue(window.PlatformImpl) as Action;
        Assert.NotNull(callback);
        callback();
    }
}
