// C 层：PersonnelHelper 测试（FormatValidPeriod 格式化 + BuildPersonnelRow 行构建 + 与 TryParseValidPeriod 互逆）

using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Models;
using Record = MS_SafetyManagementSystem.Models.Record;

namespace MSpirit.SafetySys.Tests;

public class PersonnelHelperTests : IDisposable
{
    private readonly TestProject _tp = new();

    public void Dispose() => _tp.Dispose();

    // ===== FormatValidPeriod =====

    [Fact]
    public void FormatValidPeriod_双日期与长期()
    {
        Assert.Equal("2021-01-01 至 2041-01-01", PersonnelHelper.FormatValidPeriod("2021-01-01", "2041-01-01"));
        Assert.Equal("2021-01-01 至 长期", PersonnelHelper.FormatValidPeriod("2021-01-01", "长期"));
        Assert.Equal("长期", PersonnelHelper.FormatValidPeriod("", "长期"));
        Assert.Equal("2021-01-01 至 2025-01-01", PersonnelHelper.FormatValidPeriod(" 2021-01-01 ", " 2025-01-01 ")); // 去空格
    }

    [Fact]
    public void FormatValidPeriod_空值_返回空串()
    {
        Assert.Equal("", PersonnelHelper.FormatValidPeriod("", ""));
        Assert.Equal("", PersonnelHelper.FormatValidPeriod(null, null));
    }

    [Fact]
    public void 互逆_格式化后解析_还原起止日期()
    {
        string formatted = PersonnelHelper.FormatValidPeriod("2021-01-01", "2041-01-01");
        Assert.True(PersonnelHelper.TryParseValidPeriod(formatted, out var start, out var end));
        Assert.Equal("2021-01-01", start);
        Assert.Equal("2041-01-01", end);
    }

    // ===== BuildPersonnelRow =====

    [Fact]
    public void BuildPersonnelRow_字段完整映射()
    {
        var g = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Personnel",
            Name = "测试单位",
            CreatedDate = "2026-08-01"
        };
        _tp.Service.AddGroup(g);
        var p = new PersonnelRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = g.Id,
            Title = "章北海",
            Gender = "男",
            Age = "35",
            Position = "安全员",
            EmployeeID = "E001",
            NativePlace = "海南",
            PhoneNumber = "13800000000",
            ThreeCategoryPersonnel = true,
            SpecialOperationsPersonnel = false,
            IDCardStart = "2021-01-01",
            IDCardEnd = "2041-01-01",
            Remark = "备注"
        };
        _tp.Service.AddPersonnelRecord(p);

        var row = PersonnelHelper.BuildPersonnelRow(p, 1, "测试单位", "一号班组", _tp.Service);

        Assert.Equal(1, row.RowNumber);
        Assert.Equal("章北海", row.Title);
        Assert.Equal("测试单位", row.Unit);
        Assert.Equal("一号班组", row.Department);
        Assert.Equal("男", row.Gender);
        Assert.Equal("是", row.ThreeCategoryPersonnelDisplay);
        Assert.Equal("否", row.SpecialOperationsPersonnelDisplay);
        Assert.Equal("2021-01-01 至 2041-01-01", row.IDCardValidPeriod);
        Assert.Equal("备注", row.Remark);
    }

    [Fact]
    public void BuildPersonnelRow_证书附件_读取到证书信息()
    {
        var g = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _tp.ProjectId,
            Module = "Personnel",
            Name = "测试单位",
            CreatedDate = "2026-08-01"
        };
        _tp.Service.AddGroup(g);
        var p = new PersonnelRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = g.Id,
            Title = "丁仪"
        };
        _tp.Service.AddPersonnelRecord(p);

        // 证书附件（槽位 Cert1）
        _tp.Service.AddAttachment(new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = p.Id,
            FileName = "cert.jpg",
            RelativePath = $"Project_{_tp.ProjectId}/Personnel_{g.Id}/Card_{p.Id}/cert.jpg",
            AttachmentType = "Certificate",
            DisplayOrder = 1,
            CertName = "特种作业操作证",
            CertNumber = "京A-001",
            CertStart = "2025-01-01",
            CertEnd = "2028-01-01"
        });

        var row = PersonnelHelper.BuildPersonnelRow(p, 1, "单位", "部门", _tp.Service);
        Assert.Equal("特种作业操作证", row.CertName);
        Assert.Equal("京A-001", row.CertNumber);
        Assert.Equal("2025-01-01 至 2028-01-01", row.CertValidPeriod);
    }
}
