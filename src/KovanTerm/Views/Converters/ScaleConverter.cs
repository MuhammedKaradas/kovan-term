using System.Globalization;
using System.Windows.Data;

namespace KovanTerm.Views.Converters;

/// <summary>A fraction (0–1) times the length in the parameter: a bar's width or height.</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fraction = value is double d ? d : 0;
        var length = double.Parse((string)parameter, CultureInfo.InvariantCulture);
        // A bar that's there at all stays visible
        return fraction <= 0 ? 0.0 : Math.Max(2, fraction * length);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
