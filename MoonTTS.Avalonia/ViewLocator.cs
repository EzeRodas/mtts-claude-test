using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Avalonia.Views;

namespace MoonTTS.Avalonia;

/* Strongly typed view locator to ensure AOT compatibility without reflection */

public class ViewLocator : IDataTemplate
{
    /* Maps ViewModel types directly to View instances */
    public Control? Build(object? param) => param switch
    {
        MainViewModel => new MainWindow(),
        SettingsViewModel => new SettingsWindow(),
        _ => new TextBlock { Text = "Not Found: " + param?.GetType().FullName }
    };

    /* Verifies ViewModel compatibility */
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
