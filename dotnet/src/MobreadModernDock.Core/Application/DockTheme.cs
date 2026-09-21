namespace MobreadModernDock.Core.Application;

/// <summary>The dock bar's visual identity: colour, transparency and corner rounding.</summary>
public sealed record DockTheme(string ColorRgb, double Transparency, int BorderRounding);
