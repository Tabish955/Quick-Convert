using System.Globalization;
using System.Windows.Data;
using QuickConvert.Core.Models;

namespace QuickConvert.UI.Converters;

public class TargetFormatDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            if (str == ConversionTargets.Extract) return "Extract to folder";
            return str.ToUpperInvariant();
        }
        return value ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str && str.Equals("Extract to folder", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionTargets.Extract;
        }
        return value ?? string.Empty;
    }
}
