namespace QuickConvert.Core.Models;

/// <summary>
/// Simple screen coordinates struct decoupling Core from WPF's System.Windows.Point.
/// </summary>
public readonly record struct ScreenPoint(double X, double Y);
