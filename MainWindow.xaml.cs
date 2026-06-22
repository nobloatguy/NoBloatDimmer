using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace NoBloatDimmer;

public partial class MainWindow : Window
{
    private const int DimHotkeyId = 1001;
    private const int BrightenHotkeyId = 1002;
    private const int EmergencyClearHotkeyId = 1003;

    private readonly OverlayManager _overlayManager = new();
    private readonly DimmerSettings _settings;

    private Forms.NotifyIcon? _trayIcon;
    private HwndSource? _windowSource;

    private bool _isSynchronizing;
    private bool _allowExit;
    private bool _dimHotkeyRegistered;
    private bool _brightenHotkeyRegistered;
    private bool _emergencyClearHotkeyRegistered;
    private bool _showingCustomizePanel;

    private static readonly Dictionary<string, ThemeDefinition> Themes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lime"] = new(
                "Lime",
                Media.Color.FromRgb(210, 255, 62),
                Media.Color.FromRgb(38, 50, 19),
                Media.Color.FromRgb(99, 126, 35)),

            ["Cyan"] = new(
                "Cyan",
                Media.Color.FromRgb(110, 231, 255),
                Media.Color.FromRgb(18, 49, 60),
                Media.Color.FromRgb(40, 114, 133)),

            ["Violet"] = new(
                "Violet",
                Media.Color.FromRgb(196, 167, 255),
                Media.Color.FromRgb(47, 34, 76),
                Media.Color.FromRgb(101, 75, 151)),

            ["Amber"] = new(
                "Amber",
                Media.Color.FromRgb(252, 211, 77),
                Media.Color.FromRgb(69, 49, 19),
                Media.Color.FromRgb(137, 97, 30))
        };

    private static readonly Dictionary<string, WindowSizePreset> WindowSizes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Compact"] = new("Compact", 700, 570),
            ["Standard"] = new("Standard", 790, 650),
            ["Spacious"] = new("Spacious", 920, 740)
        };

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsService.Load();

        DataContext = this;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    public ObservableCollection<DisplayState> Displays { get; } = new();

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RegisterShortcuts();
        ApplyTheme(_settings.ThemeName, save: false);
        ApplyWindowSize(_settings.WindowSizePreset, save: false);
        RefreshDisplays();
        CreateTrayIcon();
        ShowDisplaysPanel();

        SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Window_Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(RefreshDisplays);
    }

    private void RefreshDisplays()
    {
        var byName = Displays.ToDictionary(
            display => display.DeviceName,
            StringComparer.OrdinalIgnoreCase);

        var connected = Forms.Screen.AllScreens;

        var connectedNames = connected
            .Select(screen => screen.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var removed in Displays
                     .Where(display => !connectedNames.Contains(display.DeviceName))
                     .ToList())
        {
            removed.DimChanged -= Display_DimChanged;
            removed.NameChanged -= Display_NameChanged;

            Displays.Remove(removed);
            _overlayManager.Remove(removed.DeviceName);
        }

        foreach (var screen in connected)
        {
            if (byName.TryGetValue(screen.DeviceName, out var existing))
            {
                existing.UpdateScreen(screen);
                _overlayManager.Apply(existing);
                continue;
            }

            var restoredDim = _settings.DisplayDimming.TryGetValue(
                screen.DeviceName,
                out var savedDim)
                ? savedDim
                : 0;

            if (restoredDim >= 100)
            {
                restoredDim = 95;
            }

            var restoredName = _settings.DisplayNames.TryGetValue(
                screen.DeviceName,
                out var savedName)
                ? savedName
                : null;

            var display = new DisplayState(
                screen,
                restoredDim,
                restoredName);

            display.DimChanged += Display_DimChanged;
            display.NameChanged += Display_NameChanged;

            Displays.Add(display);
            _overlayManager.Apply(display);
        }

        SyncAllDisplaysValue();
        UpdateStatus();
    }

    private void Display_DimChanged(object? sender, EventArgs e)
    {
        if (sender is not DisplayState display)
        {
            return;
        }

        if (display.DimPercent >= 100 && !_emergencyClearHotkeyRegistered)
        {
            display.DimPercent = 95;

            UpdateStatus("Blackout disabled: Alt + Shift + 0 is unavailable.");

            return;
        }

        _settings.DisplayDimming[display.DeviceName] = display.DimPercent;

        SettingsService.Save(_settings);
        _overlayManager.Apply(display);

        SyncAllDisplaysValue();
    }

    private void Display_NameChanged(object? sender, EventArgs e)
    {
        if (sender is not DisplayState display)
        {
            return;
        }

        _settings.DisplayNames[display.DeviceName] = display.DisplayName;

        SettingsService.Save(_settings);
    }

    private void AllDisplaysSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        var requestedValue = (int)Math.Round(e.NewValue);

        if (requestedValue >= 100 && !_emergencyClearHotkeyRegistered)
        {
            _isSynchronizing = true;
            AllDisplaysSlider.Value = 95;
            _isSynchronizing = false;

            AllPercentText.Text = "95";

            UpdateStatus("Blackout disabled: Alt + Shift + 0 is unavailable.");

            return;
        }

        AllPercentText.Text = requestedValue.ToString();

        if (_isSynchronizing || !IsLoaded)
        {
            return;
        }

        SetAllDisplays(requestedValue);
    }

    private void SetAllDisplays(int value)
    {
        var maximum = _emergencyClearHotkeyRegistered ? 100 : 95;
        var safeValue = Math.Clamp(value, 0, maximum);

        foreach (var display in Displays)
        {
            display.DimPercent = safeValue;
        }

        SyncAllDisplaysValue();
    }

    private void SyncAllDisplaysValue()
    {
        var overallValue = Displays.Count == 0
            ? 0
            : Displays.Max(display => display.DimPercent);

        _isSynchronizing = true;
        AllDisplaysSlider.Value = overallValue;
        AllPercentText.Text = overallValue.ToString();
        _isSynchronizing = false;
    }

    private void RestoreBrightness()
    {
        _isSynchronizing = true;
        AllDisplaysSlider.Value = 0;
        AllPercentText.Text = "0";
        _isSynchronizing = false;

        SetAllDisplays(0);

        UpdateStatus("Brightness restored.");
    }

    private void TurnOff_Click(object sender, RoutedEventArgs e)
    {
        RestoreBrightness();
    }

    private void Blackout_Click(object sender, RoutedEventArgs e)
    {
        if (!_emergencyClearHotkeyRegistered)
        {
            UpdateStatus("Blackout disabled: Alt + Shift + 0 is unavailable.");
            return;
        }

        SetAllDisplays(100);

        UpdateStatus("Blackout active. Press Alt + Shift + 0 to restore.");
    }

    private void DisplaysTab_Click(object sender, RoutedEventArgs e)
    {
        ShowDisplaysPanel();
    }

    private void CustomizeTab_Click(object sender, RoutedEventArgs e)
    {
        ShowCustomizePanel();
    }

    private void ShowDisplaysPanel()
    {
        _showingCustomizePanel = false;

        DisplaysPanel.Visibility = Visibility.Visible;
        CustomizePanel.Visibility = Visibility.Collapsed;

        UpdateNavigationVisuals();
    }

    private void ShowCustomizePanel()
    {
        _showingCustomizePanel = true;

        DisplaysPanel.Visibility = Visibility.Collapsed;
        CustomizePanel.Visibility = Visibility.Visible;

        UpdateNavigationVisuals();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string themeName)
        {
            return;
        }

        ApplyTheme(themeName, save: true);
    }

    private void WindowSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string sizeName)
        {
            return;
        }

        ApplyWindowSize(sizeName, save: true);
    }

    private void ApplyTheme(string? requestedTheme, bool save)
    {
        var theme = ResolveTheme(requestedTheme);

        Resources["Accent"] = new Media.SolidColorBrush(theme.Accent);
        Resources["AccentSoft"] = new Media.SolidColorBrush(theme.AccentSoft);
        Resources["AccentBorder"] = new Media.SolidColorBrush(theme.AccentBorder);

        _settings.ThemeName = theme.Name;

        if (save)
        {
            SettingsService.Save(_settings);
        }

        ThemeStatusText.Text = $"{theme.Name} accent selected";

        UpdateThemeButtonVisuals(theme);
        UpdateNavigationVisuals();
    }

    private void ApplyWindowSize(string? requestedSize, bool save)
    {
        var preset = ResolveWindowSize(requestedSize);

        Width = preset.Width;
        Height = preset.Height;

        _settings.WindowSizePreset = preset.Name;

        if (save)
        {
            SettingsService.Save(_settings);
        }

        WindowSizeStatusText.Text =
            $"{preset.Name}: {preset.Width} × {preset.Height}";

        UpdateWindowSizeButtonVisuals(preset);
    }

    private static ThemeDefinition ResolveTheme(string? requestedTheme)
    {
        if (requestedTheme is not null &&
            Themes.TryGetValue(requestedTheme, out var theme))
        {
            return theme;
        }

        return Themes["Lime"];
    }

    private static WindowSizePreset ResolveWindowSize(string? requestedSize)
    {
        if (requestedSize is not null &&
            WindowSizes.TryGetValue(requestedSize, out var preset))
        {
            return preset;
        }

        return WindowSizes["Standard"];
    }

    private void UpdateThemeButtonVisuals(ThemeDefinition selectedTheme)
    {
        var buttons = new[]
        {
            (Name: "Lime", Button: LimeThemeButton),
            (Name: "Cyan", Button: CyanThemeButton),
            (Name: "Violet", Button: VioletThemeButton),
            (Name: "Amber", Button: AmberThemeButton)
        };

        foreach (var item in buttons)
        {
            var selected = string.Equals(
                item.Name,
                selectedTheme.Name,
                StringComparison.OrdinalIgnoreCase);

            item.Button.Background = new Media.SolidColorBrush(
                selected
                    ? selectedTheme.AccentSoft
                    : Media.Color.FromRgb(26, 34, 45));

            item.Button.BorderBrush = new Media.SolidColorBrush(
                selected
                    ? selectedTheme.AccentBorder
                    : Media.Color.FromRgb(52, 65, 84));
        }
    }

    private void UpdateWindowSizeButtonVisuals(WindowSizePreset selectedPreset)
    {
        var buttons = new[]
        {
            (Name: "Compact", Button: CompactSizeButton),
            (Name: "Standard", Button: StandardSizeButton),
            (Name: "Spacious", Button: SpaciousSizeButton)
        };

        var theme = ResolveTheme(_settings.ThemeName);

        foreach (var item in buttons)
        {
            var selected = string.Equals(
                item.Name,
                selectedPreset.Name,
                StringComparison.OrdinalIgnoreCase);

            item.Button.Background = new Media.SolidColorBrush(
                selected
                    ? theme.AccentSoft
                    : Media.Color.FromRgb(26, 34, 45));

            item.Button.BorderBrush = new Media.SolidColorBrush(
                selected
                    ? theme.AccentBorder
                    : Media.Color.FromRgb(52, 65, 84));
        }
    }

    private void UpdateNavigationVisuals()
    {
        var theme = ResolveTheme(_settings.ThemeName);

        SetNavigationButtonState(
            DisplaysNavButton,
            !_showingCustomizePanel,
            theme);

        SetNavigationButtonState(
            CustomizeNavButton,
            _showingCustomizePanel,
            theme);
    }

    private static void SetNavigationButtonState(
        System.Windows.Controls.Button button,
        bool active,
        ThemeDefinition theme)
    {
        button.Background = new Media.SolidColorBrush(
            active ? theme.AccentSoft : Media.Colors.Transparent);

        button.BorderBrush = new Media.SolidColorBrush(
            active ? theme.AccentBorder : Media.Colors.Transparent);

        button.Foreground = new Media.SolidColorBrush(
            active ? theme.Accent : Media.Color.FromRgb(140, 152, 167));
    }

    private void ResetDisplayNames_Click(object sender, RoutedEventArgs e)
    {
        foreach (var display in Displays)
        {
            display.DisplayName =
                DisplayState.DefaultNameFor(display.Screen);
        }

        _settings.DisplayNames.Clear();

        foreach (var display in Displays)
        {
            _settings.DisplayNames[display.DeviceName] =
                display.DisplayName;
        }

        SettingsService.Save(_settings);

        UpdateStatus("Display names reset.");
    }

    private void WebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        OpenWebsite();
    }

    private void WebsiteLink_Click(object sender, MouseButtonEventArgs e)
    {
        OpenWebsite();
        e.Handled = true;
    }

    private void OpenWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://nobloattools.com",
                UseShellExecute = true
            });
        }
        catch
        {
            UpdateStatus("Could not open NoBloatTools.com.");
        }
    }

    private void RegisterShortcuts()
    {
        var handle = new WindowInteropHelper(this).Handle;

        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);

        _dimHotkeyRegistered = NativeMethods.RegisterHotKey(
            handle,
            DimHotkeyId,
            NativeMethods.ModAlt | NativeMethods.ModShift,
            NativeMethods.VkDown);

        _brightenHotkeyRegistered = NativeMethods.RegisterHotKey(
            handle,
            BrightenHotkeyId,
            NativeMethods.ModAlt | NativeMethods.ModShift,
            NativeMethods.VkUp);

        _emergencyClearHotkeyRegistered = NativeMethods.RegisterHotKey(
            handle,
            EmergencyClearHotkeyId,
            NativeMethods.ModAlt | NativeMethods.ModShift,
            NativeMethods.Vk0);

        BlackoutButton.IsEnabled = _emergencyClearHotkeyRegistered;
    }

    private nint WindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message != NativeMethods.WmHotkey)
        {
            return nint.Zero;
        }

        var id = wParam.ToInt32();

        if (id == DimHotkeyId)
        {
            ChangeAllBy(5);
            handled = true;
        }
        else if (id == BrightenHotkeyId)
        {
            ChangeAllBy(-5);
            handled = true;
        }
        else if (id == EmergencyClearHotkeyId)
        {
            RestoreBrightness();
            handled = true;
        }

        return nint.Zero;
    }

    private void ChangeAllBy(int adjustment)
    {
        var maximum = _emergencyClearHotkeyRegistered ? 100 : 95;

        var nextValue = Math.Clamp(
            (int)Math.Round(AllDisplaysSlider.Value) + adjustment,
            0,
            maximum);

        _isSynchronizing = true;
        AllDisplaysSlider.Value = nextValue;
        AllPercentText.Text = nextValue.ToString();
        _isSynchronizing = false;

        SetAllDisplays(nextValue);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add(
            "Open",
            null,
            (_, _) => Dispatcher.Invoke(ShowWindowFromTray));

        menu.Items.Add(
            "Restore brightness",
            null,
            (_, _) => Dispatcher.Invoke(RestoreBrightness));

        menu.Items.Add(
            "Blackout all displays",
            null,
            (_, _) => Dispatcher.Invoke(
                () => Blackout_Click(this, new RoutedEventArgs())));

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(
            "Exit",
            null,
            (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "NoBloat Dimmer",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick +=
            (_, _) => Dispatcher.Invoke(ShowWindowFromTray);
    }

    private void ShowWindowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void UpdateStatus(string? message = null)
    {
        StatusText.Text = !string.IsNullOrWhiteSpace(message)
            ? message
            : $"{Displays.Count} display{(Displays.Count == 1 ? string.Empty : "s")} detected";
    }

    private void ExitApplication()
    {
        _allowExit = true;

        SystemEvents.DisplaySettingsChanged -=
            SystemEvents_DisplaySettingsChanged;

        var handle = new WindowInteropHelper(this).Handle;

        if (_dimHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(handle, DimHotkeyId);
        }

        if (_brightenHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(handle, BrightenHotkeyId);
        }

        if (_emergencyClearHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(handle, EmergencyClearHotkeyId);
        }

        _windowSource?.RemoveHook(WindowMessageHook);
        _trayIcon?.Dispose();
        _overlayManager.Dispose();

        Close();

        System.Windows.Application.Current.Shutdown();
    }

    private sealed record ThemeDefinition(
        string Name,
        Media.Color Accent,
        Media.Color AccentSoft,
        Media.Color AccentBorder);

    private sealed record WindowSizePreset(
        string Name,
        double Width,
        double Height);
}