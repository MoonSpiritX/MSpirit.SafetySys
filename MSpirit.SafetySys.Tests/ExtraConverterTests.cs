// C 层：剩余转换器测试（BooleanToGrayBrush/ExistsButtonStyle/LevelMargin/NullToVisibility/BooleanToBrush/BooleanToVisibility）
// 注：WPF Style 是 DispatcherObject，需 STA 线程创建（ExistsButtonStyleConverter 相关用例）

using System.Globalization;
using System.Threading;
using System.Windows;
using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.ViewModels;

namespace MSpirit.SafetySys.Tests;

public class ExtraConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // ===== BooleanToGrayBrushConverter（Export） =====

    [Fact]
    public void BooleanToGrayBrush_真返回黑色_假返回灰色()
    {
        var conv = new BooleanToGrayBrushConverter();
        var black = conv.Convert(true, typeof(object), null, Culture);
        var gray = conv.Convert(false, typeof(object), null, Culture);
        var notBool = conv.Convert("字符串", typeof(object), null, Culture);
        Assert.Equal(System.Windows.Media.Brushes.Black, black);
        Assert.Equal(System.Windows.Media.Brushes.Gray, gray);
        Assert.Equal(System.Windows.Media.Brushes.Gray, notBool);
    }

    [Fact]
    public void BooleanToGrayBrush_ConvertBack_抛异常()
    {
        Assert.Throws<NotImplementedException>(() =>
            new BooleanToGrayBrushConverter().ConvertBack(true, typeof(object), null, Culture));
    }

    // ===== LevelMarginConverter =====

    [Fact]
    public void LevelMargin_有父节点_返回缩进边距()
    {
        var conv = new LevelMarginConverter { IndentSize = 20 };
        var result = conv.Convert("parent-id", typeof(object), null, Culture);
        Assert.Equal(new Thickness(20, 0, 0, 0), result);
    }

    [Fact]
    public void LevelMargin_无父节点_返回零边距()
    {
        var conv = new LevelMarginConverter { IndentSize = 20 };
        Assert.Equal(new Thickness(0), conv.Convert("", typeof(object), null, Culture));
        Assert.Equal(new Thickness(0), conv.Convert(null, typeof(object), null, Culture));
    }

    [Fact]
    public void LevelMargin_ConvertBack_抛异常()
    {
        Assert.Throws<NotImplementedException>(() =>
            new LevelMarginConverter().ConvertBack(new Thickness(1), typeof(object), null, Culture));
    }

    // ===== NullToVisibilityConverter =====

    [Theory]
    [InlineData(null, Visibility.Collapsed)]
    [InlineData("", Visibility.Collapsed)]
    [InlineData("   ", Visibility.Collapsed)]
    [InlineData("有内容", Visibility.Visible)]
    [InlineData(123, Visibility.Visible)]
    public void NullToVisibility_空值隐藏_有值显示(object? input, Visibility expected)
    {
        var conv = new NullToVisibilityConverter();
        Assert.Equal(expected, conv.Convert(input, typeof(object), null, Culture));
    }

    // ===== BooleanToVisibilityConverter（ViewModels） =====

    [Fact]
    public void BooleanToVisibility_真可见_假折叠()
    {
        var conv = new BooleanToVisibilityConverter();
        Assert.Equal(Visibility.Visible, conv.Convert(true, typeof(object), null, Culture));
        Assert.Equal(Visibility.Collapsed, conv.Convert(false, typeof(object), null, Culture));
    }

    // ===== BooleanToBrushConverter（ViewModels，SolidColorBrush 为 Freezable，MTA 可用） =====

    [Fact]
    public void BooleanToBrush_真返回TrueBrush_假返回FalseBrush()
    {
        var conv = new BooleanToBrushConverter();
        var trueBrush = conv.Convert(true, typeof(object), null, Culture);
        var falseBrush = conv.Convert(false, typeof(object), null, Culture);
        Assert.Same(conv.TrueBrush, trueBrush);
        Assert.Same(conv.FalseBrush, falseBrush);
    }

    // ===== ExistsButtonStyleConverter（Style 需 STA） =====

    [Fact]
    public void ExistsButtonStyle_按布尔返回对应Style()
    {
        RunOnSta(() =>
        {
            var existsStyle = new Style();
            var normalStyle = new Style();
            var conv = new ExistsButtonStyleConverter { ExistsStyle = existsStyle, NormalStyle = normalStyle };

            Assert.Same(existsStyle, conv.Convert(true, typeof(object), null, Culture));
            Assert.Same(normalStyle, conv.Convert(false, typeof(object), null, Culture));
        });
    }

    [Fact]
    public void ExistsButtonStyle_未设置Style_抛异常()
    {
        RunOnSta(() =>
        {
            var conv = new ExistsButtonStyleConverter();
            Assert.Throws<InvalidOperationException>(() => conv.Convert(true, typeof(object), null, Culture));
            Assert.Throws<InvalidOperationException>(() => conv.Convert(false, typeof(object), null, Culture));
        });
    }

    /// <summary>在 STA 线程执行动作（WPF DispatcherObject 如 Style 需要）</summary>
    private static void RunOnSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captured).Throw();
    }
}
