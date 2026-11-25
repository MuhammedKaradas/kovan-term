using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using KovanTerm.Models;
using KovanTerm.Views;

namespace KovanTerm.Views.Converters;

public sealed class NodeStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        StatusBrushes.For(value is NodeStatus status ? status : NodeStatus.Idle);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
