// C 层：SolarTermHelper 季节背景键测试（节气数据来自嵌入资源 SolarTerms.json）

using MS_SafetyManagementSystem.Helpers;

namespace MSpirit.SafetySys.Tests;

public class SolarTermHelperTests
{
    // 用远离节气边界的日期断言季节（2026 年节气约：立春 2/4、立夏 5/5、立秋 8/7、立冬 11/7）

    [Fact]
    public void 春季_立春后立夏前_返回SpringBg()
    {
        Assert.Equal("SpringBg", SolarTermHelper.GetSeasonBackgroundKey(new DateTime(2026, 3, 1)));
    }

    [Fact]
    public void 夏季_立夏后立秋前_返回SummerBg()
    {
        Assert.Equal("SummerBg", SolarTermHelper.GetSeasonBackgroundKey(new DateTime(2026, 6, 1)));
    }

    [Fact]
    public void 秋季_立秋后立冬前_返回AutumnBg()
    {
        Assert.Equal("AutumnBg", SolarTermHelper.GetSeasonBackgroundKey(new DateTime(2026, 9, 1)));
    }

    [Fact]
    public void 冬季_立冬后_返回WinterBg()
    {
        Assert.Equal("WinterBg", SolarTermHelper.GetSeasonBackgroundKey(new DateTime(2026, 12, 1)));
    }

    [Fact]
    public void 冬季_立春前_返回WinterBg()
    {
        Assert.Equal("WinterBg", SolarTermHelper.GetSeasonBackgroundKey(new DateTime(2026, 1, 15)));
    }

    [Fact]
    public void GetCalendarSeasonBackgroundKey_季节映射到日历键()
    {
        Assert.Equal("CalendarSpringBg", SolarTermHelper.GetCalendarSeasonBackgroundKey(new DateTime(2026, 3, 1)));
        Assert.Equal("CalendarSummerBg", SolarTermHelper.GetCalendarSeasonBackgroundKey(new DateTime(2026, 6, 1)));
        Assert.Equal("CalendarAutumnBg", SolarTermHelper.GetCalendarSeasonBackgroundKey(new DateTime(2026, 9, 1)));
        Assert.Equal("CalendarWinterBg", SolarTermHelper.GetCalendarSeasonBackgroundKey(new DateTime(2026, 12, 1)));
    }

    [Fact]
    public void 全年日期_季节结果合法()
    {
        // 每月 15 日扫描：结果必须落在四个季节键之一（不抛异常、无空值）
        for (int month = 1; month <= 12; month++)
        {
            string key = SolarTermHelper.GetSeasonBackgroundKey(new DateTime(2026, month, 15));
            Assert.Contains(key, new[] { "SpringBg", "SummerBg", "AutumnBg", "WinterBg" });
        }
    }
}
