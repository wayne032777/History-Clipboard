using System.Globalization;
using System.Windows.Data;

namespace ClipboardHistory;

public sealed class BooleanToPinTextConverter : IValueConverter
{
    public static BooleanToPinTextConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "取消置顶" : "置顶";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
