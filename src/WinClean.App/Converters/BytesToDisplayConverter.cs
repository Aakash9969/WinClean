using System.Globalization;
using System.Windows.Data;

namespace WinClean.App.Converters;

/// <summary>Converts a byte count into a human-readable size string.</summary>
public sealed class BytesToDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        long bytes = value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            _ => 0,
        };

        return ByteFormat.Bytes(bytes);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
