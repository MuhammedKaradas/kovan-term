using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using KovanTerm.Models;

namespace KovanTerm.Views.Converters;

public sealed class NodeStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        NodeStatus.Working => Brushes.LimeGreen,
        NodeStatus.Waiting => Brushes.Gold,
        NodeStatus.Done => Brushes.SteelBlue,
        NodeStatus.Error => Brushes.IndianRed,
        _ => Brushes.Gray,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
