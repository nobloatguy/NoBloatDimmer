using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Forms = System.Windows.Forms;

namespace NoBloatDimmer;

public sealed class DisplayState : INotifyPropertyChanged
{
    private int _dimPercent;
    private string _displayName;
    private bool _usesDefaultName;
    private Forms.Screen _screen;

    public DisplayState(
        Forms.Screen screen,
        int dimPercent,
        string? displayName)
    {
        _screen = screen;
        DeviceName = screen.DeviceName;
        _dimPercent = Math.Clamp(dimPercent, 0, 100);

        var defaultName = DefaultNameFor(screen);

        _displayName = string.IsNullOrWhiteSpace(displayName)
            ? defaultName
            : displayName.Trim();

        _usesDefaultName = string.Equals(
            _displayName,
            defaultName,
            StringComparison.OrdinalIgnoreCase);
    }

    public string DeviceName { get; }

    public Forms.Screen Screen => _screen;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            var defaultName = DefaultNameFor(_screen);

            var normalized = string.IsNullOrWhiteSpace(value)
                ? defaultName
                : value.Trim();

            var usesDefault = string.Equals(
                normalized,
                defaultName,
                StringComparison.OrdinalIgnoreCase);

            if (_displayName == normalized &&
                _usesDefaultName == usesDefault)
            {
                return;
            }

            _displayName = normalized;
            _usesDefaultName = usesDefault;

            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));

            NameChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Title => DisplayName;

    public string Detail => _screen.Primary
        ? "Primary display"
        : "External display";

    public int DimPercent
    {
        get => _dimPercent;
        set
        {
            var safeValue = Math.Clamp(value, 0, 100);

            if (_dimPercent == safeValue)
            {
                return;
            }

            _dimPercent = safeValue;

            OnPropertyChanged();

            DimChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? DimChanged;
    public event EventHandler? NameChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateScreen(Forms.Screen screen)
    {
        _screen = screen;

        if (_usesDefaultName)
        {
            _displayName = DefaultNameFor(screen);

            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Title));

            NameChanged?.Invoke(this, EventArgs.Empty);
        }

        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(Screen));
    }

    public static string DefaultNameFor(Forms.Screen screen)
    {
        return screen.Primary
            ? "Primary display"
            : "External display";
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
