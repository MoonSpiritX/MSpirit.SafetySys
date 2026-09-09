// D 层：MonthlyStatisticsViewModel 测试（月度台账行构建：状态计算/照片计数/序号/日期排序）
// 注：LoadDataCore 直接赋值不依赖 Dispatcher，测试环境可正常运行

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.ViewModels;
using System.IO;
using Record = MS_SafetyManagementSystem.Models.Record;

namespace MSpirit.SafetySys.Tests;

public class MonthlyStatisticsViewModelTests : IDisposable
{
    private readonly TestProject _tp = new();
    private readonly string _ymGroupId;
    private readonly string _date15Id;
    private readonly string _date20Id;

    public MonthlyStatisticsViewModelTests()
    {
        // 年月分组 + 两个有记录日期组 + 一个空日期组
        var ym = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            Name = "2026年08月",
            CreatedDate = "2026-08-01"
        };
        _tp.Service.AddGroup(ym);
        _ymGroupId = ym.Id;

        var d15 = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            ParentId = ym.Id,
            Name = "08月15日",
            CreatedDate = "2026-08-15"
        };
        _tp.Service.AddGroup(d15);
        _date15Id = d15.Id;

        var d20 = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            ParentId = ym.Id,
            Name = "08月20日",
            CreatedDate = "2026-08-20"
        };
        _tp.Service.AddGroup(d20);
        _date20Id = d20.Id;

        // 空日期组（无记录，不应出现在行中）
        _tp.Service.AddGroup(new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            ParentId = ym.Id,
            Name = "08月25日",
            CreatedDate = "2026-08-25"
        });
    }

    public void Dispose() => _tp.Dispose();

    private string AddRecord(string groupId, string? description, string? hazardType, DateTime? deadline, DateTime? completedDate, string? measure, int displayIndex = 0, string? responsibleUnit = "施工单位", string? hazardLevel = "一般")
    {
        var r = new Record
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            Title = "记录",
            YearMonth = "2026-08",
            Description = description,
            HazardType = hazardType,
            HazardLevel = hazardLevel,
            ResponsibleUnit = responsibleUnit,
            Deadline = deadline,
            CompletedDate = completedDate,
            Measure = measure,
            DisplayIndex = displayIndex
        };
        _tp.Service.AddRecord(r);
        return r.Id;
    }

    private void AddAttachment(string recordId, string type, int order)
    {
        _tp.Service.AddAttachment(new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = recordId,
            FileName = "p.jpg",
            RelativePath = $"Project_{_tp.ProjectId}/Hazard_2026-08/Card_x/p.jpg",
            AttachmentType = type,
            DisplayOrder = order
        });
    }

    private async Task<MonthlyStatisticsViewModel> LoadAsync()
    {
        var vm = new MonthlyStatisticsViewModel(_tp.Service, _ymGroupId, "2026年08月");
        await vm.LoadDataAsync();
        return vm;
    }

    [Fact]
    public async Task 行构建_记录行数与序号递增_日期排序()
    {
        AddRecord(_date15Id, "描述A", "临边防护", null, null, null);
        AddRecord(_date15Id, "描述B", "消防", null, null, null);
        AddRecord(_date20Id, "描述C", "触电", null, null, null);

        var vm = await LoadAsync();

        Assert.Equal(3, vm.Rows.Count);
        // 08月15日 的记录在前（序号 1、2），08月20日 在后（序号 3）
        Assert.Equal(new[] { 1, 2, 3 }, vm.Rows.Select(r => r.RowNumber).ToArray());
        Assert.Equal("08月15日", vm.Rows[0].DateName);
        Assert.Equal("08月20日", vm.Rows[2].DateName);
        // 记录日期从分组名解析
        Assert.Equal(new DateTime(2026, 8, 15), vm.Rows[0].RecordDate);
    }

    [Fact]
    public async Task 状态计算_完成超期进行无效空_五种状态()
    {
        var yesterday = DateTime.Now.AddDays(-1);
        var tomorrow = DateTime.Now.AddDays(1);

        AddRecord(_date15Id, "已完成隐患", "消防", null, yesterday, "整改措施", 0);     // 完成
        string overdueId = AddRecord(_date15Id, "超期隐患", "触电", yesterday, null, null, 1); // 超期
        AddRecord(_date15Id, "进行隐患", "临边防护", tomorrow, null, null, 2);           // 进行
        AddRecord(_date15Id, null, "临边防护", null, null, null, 3);                     // 无效（无内容有关键信息）
        AddRecord(_date15Id, null, null, null, null, null, 4, null, null);               // 空（无内容无信息）

        // 诊断：验证 Deadline 落库读回
        var stored = _tp.Service.GetRecordById(overdueId)!;
        Assert.NotNull(stored.Deadline);
        Assert.Equal(yesterday.Date, stored.Deadline!.Value.Date);

        var vm = await LoadAsync();

        Assert.Equal(5, vm.Rows.Count);
        Assert.Equal("完成", vm.Rows[0].Status);
        Assert.Equal("超期", vm.Rows[1].Status);
        Assert.Equal("进行", vm.Rows[2].Status);
        Assert.Equal("无效", vm.Rows[3].Status);
        Assert.Equal("", vm.Rows[4].Status);
    }

    [Fact]
    public async Task 状态计算_无期限自动按记录日期加三天()
    {
        // 无 Deadline：默认期限 = 记录日期 + 3 天。构造 10 天前的日期分组 → +3 天仍已过 → 超期
        var past = DateTime.Now.AddDays(-10);
        string pastDateName = $"{past.Month:D2}月{past.Day:D2}日";
        var pastGroup = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Hazard",
            ParentId = _ymGroupId,
            Name = pastDateName,
            CreatedDate = past.ToString("yyyy-MM-dd")
        };
        _tp.Service.AddGroup(pastGroup);
        AddRecord(pastGroup.Id, "无期限但日期已过", "消防", null, null, null, 0);

        var vm = await LoadAsync();
        Assert.Equal("超期", vm.Rows[0].Status);
    }

    [Fact]
    public async Task 照片计数_按附件类型统计()
    {
        string r1 = AddRecord(_date15Id, "有照片隐患", "消防", null, null, null);
        AddAttachment(r1, "Hazard", 1);
        AddAttachment(r1, "Hazard", 2);
        AddAttachment(r1, "Remedy", 1);

        var vm = await LoadAsync();

        Assert.Equal(2, vm.Rows[0].HazardPhotoCount);
        Assert.Equal(1, vm.Rows[0].RemedyPhotoCount);
    }

    [Fact]
    public async Task 字段映射_记录字段完整入行()
    {
        AddRecord(_date15Id, "描述", "临边防护", null, null, "措施");
        var vm = await LoadAsync();

        var row = vm.Rows[0];
        Assert.Equal("描述", row.Description);
        Assert.Equal("临边防护", row.HazardType);
        Assert.Equal("施工单位", row.ResponsibleUnit);
        Assert.Equal("一般", row.HazardLevel);
    }

    [Fact]
    public async Task 空数据_行列表为空()
    {
        var vm = await LoadAsync();
        Assert.Empty(vm.Rows);
    }
}
