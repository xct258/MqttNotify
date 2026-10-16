using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MqttNotify.Core;

namespace MqttNotify.Windows.Converters;

/// <summary>把连接状态映射成状态灯的颜色。</summary>
public sealed class ConnectionStatusToBrushConverter : IValueConverter
{
    // 冻结成只读，避免每次绑定都新建 Brush
    private static readonly Brush Stopped = Freeze("#9E9E9E");
    private static readonly Brush Connecting = Freeze("#F9A825");
    private static readonly Brush Connected = Freeze("#2E7D32");
    private static readonly Brush Reconnecting = Freeze("#EF6C00");
    private static readonly Brush Failed = Freeze("#C62828");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ConnectionStatus.Connected => Connected,
            ConnectionStatus.Connecting => Connecting,
            ConnectionStatus.Reconnecting => Reconnecting,
            ConnectionStatus.Failed => Failed,
            _ => Stopped,
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
