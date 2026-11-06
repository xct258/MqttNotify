using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MqttNotify.Windows.Converters;

/// <summary>把服务通知状态（ok/warn/error/info）映射成徽章颜色。</summary>
public sealed class AlertStatusToBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Freeze("#22C55E");
    private static readonly Brush Warn = Freeze("#F59E0B");
    private static readonly Brush Error = Freeze("#EF4444");
    private static readonly Brush Info = Freeze("#38BDF8");
    private static readonly Brush Unknown = Freeze("#94A3B8");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.Trim().ToLowerInvariant() switch
        {
            "ok" => Ok,
            "warn" => Warn,
            "error" => Error,
            "info" => Info,
            _ => Unknown,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
