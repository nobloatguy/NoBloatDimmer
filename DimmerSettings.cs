using System;
using System.Collections.Generic;

namespace NoBloatDimmer;

public sealed class DimmerSettings
{
    public Dictionary<string, int> DisplayDimming { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> DisplayNames { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string ThemeName { get; set; } = "Lime";

    public string WindowSizePreset { get; set; } = "Standard";
}