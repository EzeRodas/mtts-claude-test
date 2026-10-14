using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Avalonia.Views;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Avalonia.Services;

public class WindowService : IWindowService
{
    private readonly MainWindow _mainWindow;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly SettingsManager _settingsManager;
    private PixelPoint _lastPos;
    private SettingsWindow? _settingsWindow;
    private readonly IAudioPlayer _audioPlayer;
    private readonly DownloadManager _downloadManager;
    private readonly DictionaryManager _dictionaryManager;
    private readonly HotkeyManager _hotkeyManager;
    private readonly HistoryManager _historyManager;
    private readonly ITtsEngineProvider _engineProvider;

    /* Initializes window service with main window, desktop lifetime, and settings */
    public WindowService(
        MainWindow mainWindow,
        IClassicDesktopStyleApplicationLifetime desktop,
        SettingsManager settingsManager,
        IAudioPlayer audioPlayer,
        DownloadManager downloadManager,
        DictionaryManager dictionaryManager,
        HotkeyManager hotkeyManager,
        HistoryManager historyManager,
        ITtsEngineProvider engineProvider)
    {
        _mainWindow = mainWindow;
        _desktop = desktop;
        _settingsManager = settingsManager;
        _audioPlayer = audioPlayer;
        _downloadManager = downloadManager;
        _dictionaryManager = dictionaryManager;
        _hotkeyManager = hotkeyManager;
        _historyManager = historyManager;
        _engineProvider = engineProvider;

        _mainWindow.Opened += OnMainWindowOpened;
        _mainWindow.PositionChanged += OnMainWindowPositionChanged;
    }

    /* Handles initial window positioning from remembered coordinates or bottom-center default */
    private void OnMainWindowOpened(object? sender, System.EventArgs e)
    {
        var config = _settingsManager.CurrentAppConfig;

        if (config.RememberPosition && (config.PosX != 0 || config.PosY != 0))
        {
            var savedPos = new PixelPoint(config.PosX, config.PosY);

            /* Ensures the saved coords are available on an active display */
            var screen = _mainWindow.Screens.ScreenFromPoint(savedPos);
            if (screen != null)
            {
                _mainWindow.Position = savedPos;
                _lastPos = savedPos;
                return;
            }
        }
        /* Fallback in case it's disconnected or off */
        _mainWindow.PositionAtBottomCenter(monitorIndex: config.DefaultMonitor);
        _lastPos = _mainWindow.Position;
    }
    
    /* Updates cached coordinate state and auto-persists when RememberPosition is enabled */ 
    private void OnMainWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_mainWindow.IsVisible)
        {
            _lastPos = e.Point;
            var config = _settingsManager.CurrentAppConfig;
            if (!config.RememberPosition) return;
            config.PosX = e.Point.X;
            config.PosY = e.Point.Y;
            _settingsManager.SaveAppConfig(config);
        }
    }

    /* Shows and activates the main window, restoring to previous screen position */
    public void ShowMainWindow()
    {
        _desktop.MainWindow ??= _mainWindow;
        
        if (!_mainWindow.IsActive)
        {
            _mainWindow.Show();
            var config = _settingsManager.CurrentAppConfig;
            if (config.RememberPosition && _lastPos != default)
            {
                _mainWindow.Position = _lastPos;
            } 
            else if (!config.RememberPosition)
            {
                _mainWindow.PositionAtBottomCenter(monitorIndex: config.DefaultMonitor);
            }
            _mainWindow.Activate();
        }
        else
        {
            /* Block so clicking on the tray icon also hides the window if active */
            HideMainWindow();
        }
    }

    /* Caches the current screen position and hides the main window */
    public void HideMainWindow()
    {
        /* Hides settings window if open when main window hides */
        HideSettingsWindow();

        /* Saves pos before hiding */
        _lastPos = _mainWindow.Position;
        _mainWindow.Hide();
    }

    /* Hides the main window when synthesis starts if HideOnSynth is configured */
    public void HideOnSynth()
    {
        var config = _settingsManager.CurrentAppConfig;
        if (config.HideOnSynth)
        {
            HideMainWindow();
        }
    }

    /* Saves window coordinates and shuts down the desktop application */
    public void ExitApplication()
    {
        var config = _settingsManager.CurrentAppConfig;
        _mainWindow.IsExiting = true;
        if (_settingsManager != null)
        {
            _settingsWindow.IsExiting = true;
            _settingsWindow.Close();
        }
        if (config.RememberPosition)
        {
            /* Uses cached pos if window is hidden */
            var finalPos = _mainWindow.IsVisible ? _mainWindow.Position : _lastPos;
            if (finalPos != default)
            {
                config.PosX = finalPos.X;
                config.PosY = finalPos.Y;
                _settingsManager.SaveAppConfig(config);
            }
        }
        
        _desktop.Shutdown();
    }

    /* Toggles the settings window visibility anchored above the HUD settings button */
    public void ToggleSettingsWindow()
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            HideSettingsWindow();
            return;
        }

        if (_settingsWindow == null)
        {
            var viewModel = new SettingsViewModel(
                _settingsManager,
                _audioPlayer,
                this,
                _downloadManager,
                _dictionaryManager,
                _hotkeyManager,
                _historyManager,
                _engineProvider);
            _settingsWindow = new SettingsWindow
            {
                DataContext = viewModel
            };
            _settingsWindow.SetOwnerWindow(_mainWindow);
        }

        /* Always reset to Quick Settings on open and calculate absolute position */
        _settingsWindow.ResetToQuickView();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
    
    /* Hides the settings window if currently visible */
    public void HideSettingsWindow()
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Hide();
        }
    }
    
    /* Positions the settings window directly above the HUD Settings button */
    private void PositionSettingsWindow()
    {
        if (_settingsWindow == null) return;

        double scaling = _mainWindow.RenderScaling > 0 ? _mainWindow.RenderScaling : 1.0;

        /* Settings button horizontal offset in MainWindow: Hyperlink(48) + Spacing(12) + Drag(48) + Spacing(12) = 120px */
        int btnOffsetX = (int)Math.Round(120 * scaling);
        int targetX = _mainWindow.Position.X + btnOffsetX;

        /* Position directly above the HUD with 8px margin */
        int targetY = _mainWindow.Position.Y - (int)Math.Round((_settingsWindow.Height + 8) * scaling);

        var screen = _mainWindow.Screens.ScreenFromWindow(_mainWindow) ?? _mainWindow.Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            int winWidthPx = (int)Math.Round(_settingsWindow.Width * scaling);

            /* Clamps X within screen working area boundaries */
            if (targetX + winWidthPx > workingArea.Right)
            {
                targetX = workingArea.Right - winWidthPx - (int)Math.Round(12 * scaling);
            }
            if (targetX < workingArea.BottomLeft.X)
            {
                targetX = workingArea.BottomLeft.X + (int)Math.Round(12 * scaling);
            }

            /* If not enough room above HUD, flips to below the HUD */
            if (targetY < workingArea.TopLeft.Y)
            {
                targetY = _mainWindow.Position.Y + (int)Math.Round((_mainWindow.Height + 8) * scaling);
            }
        }

        _settingsWindow.Position = new PixelPoint(targetX, targetY);
    }
}