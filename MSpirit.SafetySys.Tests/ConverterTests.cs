// WPF 转换器测试：
// InverseBooleanConverter / TextToBrushConverter / ValidityDateToBrushConverter
// 注意：WPF Freezable（Brush）允许在任意线程创建与读取，无需 STA 线程

using System.Globalization;
using System.Windows.Media;
using MS_SafetyManagementSystem.Converters;
using MS_SafetyManagementSystem.ViewModels;

namespace MSpirit.SafetySys.Tests;

public class ConverterTests
{
    // ───────── InverseBooleanConverter ─────────

    [Fact]
    public void InverseBoolean_反转布尔值()
    {
        var c = new InverseBooleanConverter();
        Assert.Equal(false, c.Convert(true, typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(true, c.Convert(false, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InverseBoolean_非布尔输入_返回true()
    {
        var c = new InverseBooleanConverter();
        Assert.Equal(true, c.Convert("不是bool", typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(true, c.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void InverseBoolean_ConvertBack_抛异常()
    {
        var c = new InverseBooleanConverter();
        Assert.Throws<NotImplementedException>(() =>
            c.ConvertBack(true, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    // ───────── TextToBrushConverter ─────────

    [Fact]
    public void TextToBrush_文本相等匹配_返回对应颜色()
    {
        var c = new TextToBrushConverter();
        var brush = c.Convert("进行", typeof(Brush), "完成|Black,进行|#4CAF50,超期|#E91E63", CultureInfo.InvariantCulture);
        Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#4CAF50"), ((SolidColorBrush)brush).Color);
    }

    [Fact]
    public void TextToBrush_数值阈值匹配()
    {
        var c = new TextToBrushConverter();
        var brush = c.Convert("75", typeof(Brush), ">=61|#E91E63", CultureInfo.InvariantCulture);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#E91E63"), ((SolidColorBrush)brush).Color);

        var below = c.Convert("50", typeof(Brush), ">=61|#E91E63", CultureInfo.InvariantCulture);
        Assert.Equal(Brushes.Black, below);
    }

    [Fact]
    public void TextToBrush_无匹配_返回黑色()
    {
        var c = new TextToBrushConverter();
        var brush = c.Convert("不存在", typeof(Brush), "完成|Black,进行|#4CAF50", CultureInfo.InvariantCulture);
        Assert.Equal(Brushes.Black, brush);

        // 空规则或空值也返回黑色
        Assert.Equal(Brushes.Black, c.Convert("x", typeof(Brush), "", CultureInfo.InvariantCulture));
        Assert.Equal(Brushes.Black, c.Convert(null, typeof(Brush), "完成|Black", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TextToBrush_优先返回首个匹配规则()
    {
        var c = new TextToBrushConverter();
        var brush = c.Convert("完成", typeof(Brush), "完成|#FF0000,完成|#00FF00", CultureInfo.InvariantCulture);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#FF0000"), ((SolidColorBrush)brush).Color);
    }

    // ───────── ValidityDateToBrushConverter ─────────

    [Fact]
    public void ValidityToBrush_已过期_返回玫红()
    {
        var c = new ValidityDateToBrushConverter();
        var past = DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd");
        var brush = c.Convert($"2026-01-01 至 {past}", typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#E91E63"), ((SolidColorBrush)brush).Color);
    }

    [Fact]
    public void ValidityToBrush_30天内到期_返回绿色()
    {
        var c = new ValidityDateToBrushConverter();
        var soon = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd");
        var brush = c.Convert($"2026-01-01 至 {soon}", typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#4CAF50"), ((SolidColorBrush)brush).Color);
    }

    [Fact]
    public void ValidityToBrush_正常有效期_返回黑色()
    {
        var c = new ValidityDateToBrushConverter();
        var far = DateTime.Today.AddDays(90).ToString("yyyy-MM-dd");
        var brush = c.Convert($"2026-01-01 至 {far}", typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal(Brushes.Black, brush);
    }

    [Fact]
    public void ValidityToBrush_支持波浪号分隔()
    {
        var c = new ValidityDateToBrushConverter();
        var past = DateTime.Today.AddDays(-5).ToString("yyyy-MM-dd");
        var brush = c.Convert($"2026-01-01~{past}", typeof(Brush), null, CultureInfo.InvariantCulture);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#E91E63"), ((SolidColorBrush)brush).Color);
    }

    [Fact]
    public void ValidityToBrush_无法解析结束日期_返回黑色()
    {
        var c = new ValidityDateToBrushConverter();
        Assert.Equal(Brushes.Black, c.Convert("长期有效", typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Brushes.Black, c.Convert("", typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Brushes.Black, c.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Brushes.Black, c.Convert("2026-01-01 至 乱写的", typeof(Brush), null, CultureInfo.InvariantCulture));
    }
}
