namespace NoBloatDimmer;

internal sealed class OverlayManager
{
    private readonly Dictionary<string, OverlayWindow> _overlays = new(StringComparer.OrdinalIgnoreCase);

    public void Apply(DisplayState state)
    {
        if (!_overlays.TryGetValue(state.DeviceName, out var overlay))
        {
            overlay = new OverlayWindow();
            _overlays[state.DeviceName] = overlay;
        }

        overlay.Apply(state.Screen, state.DimPercent);
    }

    public void Remove(string deviceName)
    {
        if (_overlays.Remove(deviceName, out var overlay))
        {
            overlay.Remove();
        }
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Remove();
        }

        _overlays.Clear();
    }
}

