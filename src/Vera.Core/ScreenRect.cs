namespace Vera.Core;

/// <summary>A rectangle in physical screen pixels. The adapter converts from DPI-scaled coordinates.</summary>
public sealed record ScreenRect(int X, int Y, int Width, int Height);