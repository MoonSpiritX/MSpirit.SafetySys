// D 层：PersonnelStatisticsViewModel 测试（单位收集/排序/过滤/证书槽位）
// 2026-08-15 修复回归：LoadDataCore 在 dispatcher 为 null（无 UI 环境）时不再静默丢弃数据

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.ViewModels;
using Record = MS_SafetyManagementSystem.Models.Record;

namespace MSpirit.SafetySys.Tests;

public class PersonnelStatisticsViewModelTests : IDisposable
{
    private readonly TestProject _tp = new();
    private readonly string _unitId;
    private readonly string _deptId;

    public PersonnelStatisticsViewModelTests()
    {
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

        AddPersonnel(dept.Id, "在场三类", exitTime: null, three: true, special: false, index: 1);
        AddPersonnel(dept.Id, "退场特种", exitTime: "2026-01-01", three: false, special: true, index: 0);
        AddPersonnel(dept.Id, "在场普通", exitTime: null, three: false, special: false, index: 2);
    }

    private void AddPersonnel(string groupId, string name, string? exitTime, bool three, bool special, int index)
    {
        _tp.Service.AddPersonnelRecord(new PersonnelRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            Title = name,
            ExitTime = exitTime,
            ThreeCategoryPersonnel = three,
            SpecialOperationsPersonnel = special,
            DisplayIndex = index
        });
    }

    public void Dispose() => _tp.Dispose();

    private async Task<PersonnelStatisticsViewModel> LoadAsync()
    {
        var vm = new PersonnelStatisticsViewModel(_tp.Service, _unitId, "星舰建设");
        await vm.LoadDataAsync();
        return vm;
    }

    [Fact]
    public void 构造_单位下拉_含项目所有单位选项与单位列表()
    {
        var vm = new PersonnelStatisticsViewModel(_tp.Service, _unitId, "星舰建设");
        Assert.Equal(PersonnelStatisticsViewModel.AllUnitsOption, vm.UnitGroups[0]);
        Assert.Contains("星舰建设", vm.UnitGroups);
        Assert.Equal("星舰建设", vm.GroupName);
    }

    [Fact]
    public async Task 加载_无UI环境数据也落Rows_排序在场优先()
    {
        // 修复回归：dispatcher 为 null（测试环境无 Application）时数据不再被静默丢弃
        var vm = await LoadAsync();

        Assert.Equal(3, vm.Rows.Count);
        // 在场优先 → 在场三类(index1)、在场普通(index2)在前，退场特种最后
        Assert.Equal("在场三类", vm.Rows[0].Title);
        Assert.Equal("在场普通", vm.Rows[1].Title);
        Assert.Equal("退场特种", vm.Rows[2].Title);
        Assert.Equal(new[] { 1, 2, 3 }, vm.Rows.Select(r => r.RowNumber).ToArray());
        Assert.All(vm.Rows, r => Assert.Equal("星舰建设", r.Unit));
    }

    [Fact]
    public async Task 过滤_状态在场退场_重新编号()
    {
        var vm = await LoadAsync();
        Assert.Equal(3, vm.FilteredCount);

        vm.PersonnelStatus = "在场人员";
        Assert.Equal(2, vm.FilteredCount);
        Assert.Equal(new[] { 1, 2 }, vm.Rows.Select(r => r.RowNumber).ToArray());

        vm.PersonnelStatus = "退场人员";
        Assert.Equal(1, vm.FilteredCount);
        Assert.Equal("退场特种", vm.Rows[0].Title);
    }

    [Fact]
    public async Task 过滤_类别三类与特种()
    {
        var vm = await LoadAsync();

        vm.Category = "三类人员";
        Assert.Equal(1, vm.FilteredCount);
        Assert.Equal("在场三类", vm.Rows[0].Title);

        vm.Category = "特种作业人员";
        Assert.Equal(1, vm.FilteredCount);
        Assert.Equal("退场特种", vm.Rows[0].Title);
    }

    [Fact]
    public async Task 空范围_无人员_数据为空不抛异常()
    {
        // 指向无人员的部门
        var vm = new PersonnelStatisticsViewModel(_tp.Service, _deptId, "一号班组");
        // 清空部门下人员
        foreach (var r in _tp.Service.GetPersonnelRecordsByGroup(_deptId))
            _tp.Service.DeletePersonnelRecord(r.Id);

        await vm.LoadDataAsync();
        Assert.Empty(vm.Rows);
        Assert.Equal(0, vm.FilteredCount);
    }

    [Fact]
    public async Task 证书槽位_首个非空优先()
    {
        var dept = _tp.Service.GetGroups("Personnel", _unitId).Single();
        var p = new PersonnelRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = dept.Id,
            Title = "带证人员",
            Cert2Name = "二级证书",
            Cert2DocNumber = "证-002",
            Cert2Start = "2025-01-01",
            Cert2End = "长期",
            Cert1Name = "一级证书",   // Cert1 非空 → 优先取 Cert1
            Cert1DocNumber = "证-001",
            Cert1Start = "2024-01-01",
            Cert1End = "2027-01-01"
        };
        _tp.Service.AddPersonnelRecord(p);

        var vm = await LoadAsync();
        var row = vm.Rows.Single(r => r.Title == "带证人员");
        Assert.Equal("一级证书", row.CertName);
        Assert.Equal("证-001", row.CertNumber);
        Assert.Equal("2024-01-01 至 2027-01-01", row.CertValidPeriod);
    }
}
