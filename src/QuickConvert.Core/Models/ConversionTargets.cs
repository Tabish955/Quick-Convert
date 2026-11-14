namespace QuickConvert.Core.Models;

/// <summary>Reserved target tokens (D2). The ConversionMatrix must never contain these.</summary>
public static class ConversionTargets
{
    public const string Extract = "#extract";
    public static bool IsReserved(string target) => target.StartsWith('#');
}
