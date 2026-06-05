using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using System.Globalization;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace TempWidget.Converters;

/// <summary>
/// 温度值转颜色: 三档报警
///  &lt; 70°C 绿 (正常) | 70-85°C 橙 (警告) | ≥ 85°C 红 (危险)
/// </summary>
public class TempToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not float temp)
            return new SolidColorBrush(Color.FromRgb(150, 150, 150));

        Color color;
        if (temp < 70)      color = Color.FromRgb(120, 220, 140);  // 绿
        else if (temp < 85) color = Color.FromRgb(255, 175, 60);   // 橙
        else                color = Color.FromRgb(255, 70, 70);    // 红

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 温度值转数字字符串 (保留 0 位小数或显示 "—")
/// </summary>
public class TempToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not float temp)
            return "—";

        return $"{temp:F0}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
