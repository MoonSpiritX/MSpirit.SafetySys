// 人员模块日期解析器（DateParser）测试：
// 完整年份/简写格式解析、无效输入容错、有效期专用解析

using MS_SafetyManagementSystem.Helpers;

namespace MSpirit.SafetySys.Tests;

public class DateParserTests
{
    [Theory]
    [InlineData("2026-07-01", "2026-07-01")]
    [InlineData("2026-7-01", "2026-07-01")]
    [InlineData("2026.07.01", "2026-07-01")]
    [InlineData("2026.7.6", "2026-07-06")]
    [InlineData("2026年7月6日", "2026-07-06")]
    [InlineData("2026年07月06日", "2026-07-06")]
    public void TryParse_完整年份格式_解析为标准格式(string input, string expected)
    {
        var (success, formatted, parsed) = DateParser.TryParse(input);
        Assert.True(success);
        Assert.Equal(expected, formatted);
        Assert.NotNull(parsed);
        Assert.Equal(expected, parsed.Value.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData("07-01")]
    [InlineData("7-1")]
    [InlineData("07.01")]
    [InlineData("6.2")]
    [InlineData("6月2日")]
    [InlineData("06月02日")]
    public void TryParse_简写格式_自动补当年(string input)
    {
        var (success, formatted, parsed) = DateParser.TryParse(input);
        Assert.True(success);
        Assert.NotNull(formatted);

        int expectedYear = DateTime.Now.Year;
        Assert.StartsWith($"{expectedYear}-", formatted!);
        Assert.Equal(expectedYear, parsed!.Value.Year);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_空输入_返回失败(string? input)
    {
        var (success, formatted, parsed) = DateParser.TryParse(input);
        Assert.False(success);
        Assert.Null(formatted);
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2026-13-01")]  // 非法月份
    [InlineData("2026-02-30")]  // 非法日期
    [InlineData("随便写的")]
    public void TryParse_无效输入_返回失败(string input)
    {
        var (success, formatted, _) = DateParser.TryParse(input);
        Assert.False(success);
        Assert.Null(formatted);
    }

    [Fact]
    public void Format_有效输入_返回标准格式()
    {
        Assert.Equal("2026-07-01", DateParser.Format("2026年7月1日"));
    }

    [Fact]
    public void Format_无效输入_返回原值()
    {
        Assert.Equal("乱写的", DateParser.Format("乱写的"));
        Assert.Equal("", DateParser.Format(""));
    }

    [Theory]
    [InlineData("2026-07-01", "2026-07-01")]
    [InlineData("2026.7.6", "2026-07-06")]
    [InlineData("2026年7月6日", "2026-07-06")]
    public void TryParseValidityDate_完整年份_正常解析(string input, string expected)
    {
        var (success, formatted) = DateParser.TryParseValidityDate(input);
        Assert.True(success);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void TryParseValidityDate_简写格式_年份补全为202x()
    {
        // 当前年份前 3 位 + "x"（如 2026 年 → "202x"）
        string yearPrefix = DateTime.Now.Year.ToString()[..3];

        var (success1, f1) = DateParser.TryParseValidityDate("6-2");
        Assert.True(success1);
        Assert.Equal($"{yearPrefix}x-06-02", f1);

        var (success2, f2) = DateParser.TryParseValidityDate("03.05");
        Assert.True(success2);
        Assert.Equal($"{yearPrefix}x-03-05", f2);

        var (success3, f3) = DateParser.TryParseValidityDate("6月2日");
        Assert.True(success3);
        Assert.Equal($"{yearPrefix}x-06-02", f3);
    }

    [Fact]
    public void TryParseValidityDate_空与无效_返回失败()
    {
        var (s1, f1) = DateParser.TryParseValidityDate("");
        Assert.False(s1);
        Assert.Null(f1);

        var (s2, f2) = DateParser.TryParseValidityDate(null);
        Assert.False(s2);
        Assert.Null(f2);

        var (s3, f3) = DateParser.TryParseValidityDate("乱七八糟");
        Assert.False(s3);
        Assert.Null(f3);
    }
}
