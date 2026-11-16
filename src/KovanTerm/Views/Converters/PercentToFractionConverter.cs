using System.Globalization;
using System.Windows.Data;

namespace KovanTerm.Views.Converters;

/// <summary>0–100 → 0–1, e.g. to scale a usage bar to its share of the full width.</summary>
public sealed class PercentToFractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double p ? Math.Clamp(p, 0, 100) / 100 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
