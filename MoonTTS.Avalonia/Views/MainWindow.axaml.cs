using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using MoonTTS.Core.Services;
using MoonTTS.Domain.Entities;
using MoonTTS.Domain.Interfaces;

namespace MoonTTS.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        Width = 770;
        Height = 50;
    }

    public bool IsExiting { get; set; } = false;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    /* Positions the window at the bottom center of the designated monitor */
    /* Uses working area to respect desktop docks and panels */
    public void PositionAtBottomCenter(int bottomMargin = 12, int? monitorIndex = 0)
    {
        Screen? targetScreen = null;

        if (monitorIndex.HasValue && monitorIndex.Value > 0 && monitorIndex.Value < Screens.All.Count)
        {
            targetScreen = Screens.All[monitorIndex.Value];
        }
        else
        {
            targetScreen = Screens.Primary ?? Screens.ScreenFromWindow(this) ?? Screens.All.FirstOrDefault();
        }
        
        if (targetScreen == null)
            return;

        var workingArea = targetScreen.WorkingArea;
        double scaling = RenderScaling > 0 ? RenderScaling : targetScreen.Scaling;

        int windowWidthPx = (int)Math.Round(Width * scaling);
        int windowHeightPx = (int)Math.Round(Height * scaling);
        int marginPx = (int)Math.Round(bottomMargin * scaling);

        int x = workingArea.X + (workingArea.Width - windowWidthPx) / 2;
        int y = workingArea.Y + workingArea.Height - windowHeightPx - marginPx;

        Position = new PixelPoint(x, y);
    }
    

    public void DragButton_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (sender is Border b) b.Classes.Add("Pressed");
            BeginMoveDrag(e);
        }
    }
    
    public void DragButton_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Border b) b.Classes.Remove("Pressed");
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        InputBox.Focus();
    }
}