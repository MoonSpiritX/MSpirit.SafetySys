// C 层：PersonnelOrderingService 测试（人员收集/排序/过滤，B 层 A 层共用逻辑）

using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using Record = MS_SafetyManagementSystem.Models.Record;

namespace MSpirit.SafetySys.Tests;

public class PersonnelOrderingServiceTests : IDisposable
{
    private readonly TestProject _tp = new();
    private readonly string _unitId;
    private readonly string _deptId;

    public PersonnelOrderingServiceTests()
    {
        // 建单位 → 部门 → 人员（在场/退场、三类/特种 组合）
        var unit = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Personnel",
            Name = "星舰建设",
            CreatedDate = "2026-08-01"
        };
        _tp.Service.AddGroup(unit);
        _unitId = unit.Id;

        var dept = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Personnel",
            ParentId = unit.Id,
            Name = "一号班组",
            CreatedDate = "2026-08-01"
        };
        _tp.Service.AddGroup(dept);
        _deptId = dept.Id;

        AddPersonnel(dept.Id, "在场三类", exitTime: null, threeCategory: true, specialOps: false, displayIndex: 1);
        AddPersonnel(dept.Id, "退场老员工", exitTime: "2026-01-01", threeCategory: false, specialOps: true, displayIndex: 0);
        AddPersonnel(dept.Id, "在场特种", exitTime: null, threeCategory: false, specialOps: true, displayIndex: 2);
    }

    private void AddPersonnel(string groupId, string name, string? exitTime, bool threeCategory, bool specialOps, int displayIndex)
    {
        _tp.Service.AddPersonnelRecord(new PersonnelRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            Title = name,
            ExitTime = exitTime,
            ThreeCategoryPersonnel = threeCategory,
            SpecialOperationsPersonnel = specialOps,
            DisplayIndex = displayIndex
        });
    }

    public void Dispose() => _tp.Dispose();

    // ===== CollectRecords =====

    [Fact]
    public void CollectRecords_单位下_收集全部人员_含部门信息()
    {
        var records = PersonnelOrderingService.CollectRecords(_tp.Service, _unitId, null, "兜底单位");
        Assert.Equal(3, records.Count);
        Assert.All(records, r => Assert.Equal("星舰建设", r.Unit));
        Assert.All(records, r => Assert.Equal("一号班组", r.Department));
    }

    [Fact]
    public void CollectRecords_指定部门_只收集该部门()
    {
        var records = PersonnelOrderingService.CollectRecords(_tp.Service, _unitId, _deptId, "兜底");
        Assert.Equal(3, records.Count); // 所有人员都在该部门
    }

    [Fact]
    public void CollectRecords_单位不存在_用兜底名称()
    {
        var records = PersonnelOrderingService.CollectRecords(_tp.Service, "不存在的单位", null, "兜底单位名");
        Assert.Empty(records); // 无人员，单位名兜底不产生记录
    }

    // ===== SortByTree =====

    [Fact]
    public void SortByTree_在场优先_退场按时间倒序()
    {
        var records = PersonnelOrderingService.CollectRecords(_tp.Service, _unitId, null, "");
        var sorted = PersonnelOrderingService.SortByTree(records);

        // 前两个为在场人员（按 DisplayIndex 1、2），最后一个为退场人员
        Assert.Equal("在场三类", sorted[0].Record.Title);
        Assert.Equal("在场特种", sorted[1].Record.Title);
        Assert.Equal("退场老员工", sorted[2].Record.Title);
    }

    [Fact]
    public void SortByTree_多退场人员_按退场时间倒序()
    {
        // 追加两个退场人员（退场时间不同）
        AddPersonnel(_deptId, "早退场", "2025-06-01", false, false, 3);
        AddPersonnel(_deptId, "晚退场", "2026-06-01", false, false, 4);

        var sorted = PersonnelOrderingService.SortByTree(
            PersonnelOrderingService.CollectRecords(_tp.Service, _unitId, null, ""));
        var exitOnes = sorted.Where(r => !string.IsNullOrEmpty(r.Record.ExitTime)).ToList();
        Assert.Equal("晚退场", exitOnes[0].Record.Title); // 退场时间倒序
        Assert.Equal("早退场", exitOnes[^1].Record.Title);
    }

    // ===== FilterByStatus =====

    [Fact]
    public void FilterByStatus_在场与退场过滤()
    {
        var records = PersonnelOrderingService.CollectRecords(_tp.Service, _unitId, null, "");
        Assert.Equal(3, PersonnelOrderingService.FilterByStatus(records, "全部人员").Count);
        Assert.Equal(2, PersonnelOrderingService.FilterByStatus(records, "在场人员").Count);
        Assert.Single(PersonnelOrderingService.FilterByStatus(records, "退场人员"));
        Assert.Equal(3, PersonnelOrderingService.FilterByStatus(records, "").Count);
    }

    // ===== FilterByCategory =====

    [Fact]
    public void FilterByCategory_三类与特种过滤()
    {
        var records = PersonnelOrderingService.CollectRecords(_tp.Service, _unitId, null, "");
        Assert.Equal(3, PersonnelOrderingService.FilterByCategory(records, "全部人员").Count);
        Assert.Single(PersonnelOrderingService.FilterByCategory(records, "三类人员"));
        Assert.Equal(2, PersonnelOrderingService.FilterByCategory(records, "特种作业人员").Count);
    }

    // ===== 纯判定方法 =====

    [Theory]
    [InlineData("", "全部人员", true)]
    [InlineData("2026-01-01", "全部人员", true)]
    [InlineData(null, "在场人员", true)]
    [InlineData("", "在场人员", true)]
    [InlineData("2026-01-01", "在场人员", false)]
    [InlineData(null, "退场人员", false)]
    [InlineData("2026-01-01", "退场人员", true)]
    [InlineData("2026-01-01", "未知状态", true)] // 未知状态默认放行
    public void MatchesStatus_状态判定(string? exitTime, string status, bool expected)
    {
        Assert.Equal(expected, PersonnelOrderingService.MatchesStatus(exitTime, status));
    }

    [Theory]
    [InlineData("是", "是", "三类人员", true)]
    [InlineData("否", "是", "三类人员", false)]
    [InlineData("否", "是", "特种作业人员", true)]
    [InlineData("是", "否", "特种作业人员", false)]
    [InlineData("是", "是", "全部人员", true)]
    [InlineData("是", "是", "", true)]
    [InlineData("是", "是", "未知类别", true)]
    public void MatchesCategory_类别判定(string three, string special, string category, bool expected)
    {
        Assert.Equal(expected, PersonnelOrderingService.MatchesCategory(three, special, category));
    }
}
