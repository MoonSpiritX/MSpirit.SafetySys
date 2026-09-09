// D 层：AnalysisViewModel 测试（统计聚合：统计卡片/类型分布/完成率/摘要/记录列表）
// 注意：构造即自动 AnalyzeData（分析当月），测试动态构造当月分组数据

using MS_SafetyManagementSystem.Analysis;
using MS_SafetyManagementSystem.Models;
using Record = MS_SafetyManagementSystem.Models.Record;

namespace MSpirit.SafetySys.Tests;

public class AnalysisViewModelTests : IDisposable
{
    private readonly TestProject _tp = new();
    private readonly string _ymGroupId;
    private readonly string _dateGroupId;
    private readonly string _ymName;
    private readonly string _dateName;

    public AnalysisViewModelTests()
    {
        var now = DateTime.Now;
        _ymName = $"{now.Year}年{now.Month:D2}月";
        _dateName = $"{now.Month:D2}月15日";

        var ym = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            Name = _ymName,
            CreatedDate = now.ToString("yyyy-MM-dd")
        };
        _tp.Service.AddGroup(ym);
        _ymGroupId = ym.Id;

        var date = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            ParentId = ym.Id,
            Name = _dateName,
            CreatedDate = now.ToString("yyyy-MM-dd")
        };
        _tp.Service.AddGroup(date);
        _dateGroupId = date.Id;
    }

    public void Dispose() => _tp.Dispose();

    private int _nextIndex = 1;

    private void AddRecord(string hazardType, string hazardLevel, string? measure, DateTime? completedDate, DateTime? deadline)
    {
        _tp.Service.AddRecord(new Record
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = _dateGroupId,
            Title = "记录",
            YearMonth = $"{DateTime.Now.Year}-{DateTime.Now.Month:D2}",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Description = "隐患描述",
            HazardType = hazardType,
            HazardLevel = hazardLevel,
            Measure = measure,
            CompletedDate = completedDate,
            Deadline = deadline,
            DisplayIndex = _nextIndex++  // 保证同组内顺序稳定
        });
    }

    private AnalysisViewModel NewViewModel() =>
        new(_tp.Service, "测试项目", "隐患管理模块");

    [Fact]
    public void 统计卡片_总数有效数类型数完成率()
    {
        var yesterday = DateTime.Now.AddDays(-1);
        var today = DateTime.Now;
        AddRecord("临边防护", "一般", "整改措施", yesterday, null);      // 完成（有措施+完成日期）
        AddRecord("临边防护", "较大", "整改措施", null, null);            // 进行（有措施无完成）
        AddRecord("消防", "一般", "", today, null);                        // 无措施

        var vm = NewViewModel();

        Assert.Equal(3, vm.TotalRecords);
        Assert.Equal(3, vm.ValidRecordCount);        // 都有类型+级别
        Assert.Equal(2, vm.HazardTypeCount);         // 临边防护/消防
        // 完成率：completed=1（有措施且完成）/ hasMeasure=2 → 50%
        Assert.Equal("50.0%", vm.CompletionRateText);
        Assert.Contains("分析完成，共 3 条", vm.StatusText);
        Assert.Contains("测试项目", vm.WindowTitle);
    }

    [Fact]
    public void 类型分布_按数量降序_百分比正确()
    {
        AddRecord("临边防护", "一般", "", null, null);
        AddRecord("临边防护", "较大", "", null, null);
        AddRecord("消防", "一般", "", null, null);

        var vm = NewViewModel();

        Assert.Equal(2, vm.TypeDistribution.Count);
        Assert.Equal("临边防护", vm.TypeDistribution[0].HazardType);
        Assert.Equal(2, vm.TypeDistribution[0].Count);
        Assert.Equal(66.7, vm.TypeDistribution[0].Percentage, 1);
        Assert.Equal(1, vm.TypeDistribution[0].SequenceNumber);
        Assert.Equal("消防", vm.TypeDistribution[1].HazardType);
        Assert.Equal(33.3, vm.TypeDistribution[1].Percentage, 1);
    }

    [Fact]
    public void 摘要文本_包含时间与完成率与主要类型()
    {
        AddRecord("临边防护", "一般", "措施", DateTime.Now.AddDays(-1), null);
        AddRecord("消防", "一般", "措施", null, null);

        var vm = NewViewModel();

        Assert.Contains($"{DateTime.Now.Month}月份", vm.SummaryText);
        Assert.Contains("共2条", vm.SummaryText);
        Assert.Contains("整改完成率50.0%", vm.SummaryText);
        Assert.Contains("临边防护", vm.SummaryText);
    }

    [Fact]
    public void 记录列表_字段完整_状态计算()
    {
        var yesterday = DateTime.Now.AddDays(-1);
        AddRecord("临边防护", "一般", "措施", yesterday, null);            // 完成
        AddRecord("消防", "一般", null, null, yesterday);                  // 超期（期限已过）

        var vm = NewViewModel();

        Assert.Equal(2, vm.FilteredRecords.Count);
        Assert.Equal("完成", vm.FilteredRecords[0].Status);
        Assert.Equal("超期", vm.FilteredRecords[1].Status);
        Assert.Equal(_dateName, vm.FilteredRecords[0].DateName);
        Assert.Equal("临边防护", vm.FilteredRecords[0].HazardType);
        Assert.Equal(1, vm.FilteredRecords[0].DisplayIndex);
        Assert.Equal(2, vm.FilteredRecords[1].DisplayIndex);
    }

    [Fact]
    public void 年月分组加载_含当月分组与日期子组()
    {
        AddRecord("临边防护", "一般", "", null, null);
        var vm = NewViewModel();

        var ym = Assert.Single(vm.YearMonthGroups);
        Assert.Equal(_ymName, ym.Name);
        var date = Assert.Single(ym.DateGroups);
        Assert.Equal(_dateName, date.Name);
    }

    [Fact]
    public void 月度趋势_按创建时间分组_累计递增()
    {
        AddRecord("临边防护", "一般", "", null, null);
        AddRecord("消防", "一般", "", null, null);
        var vm = NewViewModel();

        var item = Assert.Single(vm.MonthlyTrend);
        Assert.Equal($"{DateTime.Now.Year:D4}-{DateTime.Now.Month:D2}", item.Month);
        Assert.Equal(2, item.NewCount);
        Assert.Equal(2, item.CumulativeCount);
    }

    [Fact]
    public void 图表数据_构建完成()
    {
        AddRecord("临边防护", "一般", "", null, null);
        var vm = NewViewModel();

        Assert.NotEmpty(vm.BarSeries);
        Assert.NotEmpty(vm.PieSeries);
        Assert.NotEmpty(vm.DailySeries);
        Assert.NotEmpty(vm.MonthlySeries);
    }

    [Fact]
    public void 无数据_统计归零_不抛异常()
    {
        var vm = NewViewModel();
        Assert.Equal(0, vm.TotalRecords);
        Assert.Equal("", vm.SummaryText);
        Assert.Empty(vm.TypeDistribution);
        Assert.Equal("0%", vm.CompletionRateText);
        Assert.Contains("分析完成，共 0 条", vm.StatusText);
    }
}
