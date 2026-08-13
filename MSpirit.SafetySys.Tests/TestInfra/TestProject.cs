// 测试基础设施：创建隔离的临时项目环境（绝不触碰真实数据目录）
// 目录结构模拟生产环境：{root}\ProjectsDate\Project_{id}\data.db
// 原因：ProjectDataService.GetProjectsDateRoot() 依赖 dbPath 上溯两级推导附件根目录

using System.IO;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using Record = MS_SafetyManagementSystem.Models.Record; // 避免与 Xunit.Record 冲突

namespace MSpirit.SafetySys.Tests;

/// <summary>
/// 每个测试独立创建临时根目录 + 真实 SQLite 项目库，Dispose 时整体清理。
/// </summary>
public sealed class TestProject : IDisposable
{
    public string RootPath { get; }
    public string ProjectId { get; }
    public string ProjectFolder { get; }
    public ProjectDataService Service { get; }

    public TestProject()
    {
        ProjectId = Guid.NewGuid().ToString("N");
        RootPath = Path.Combine(Path.GetTempPath(), "MSpiritTests", Guid.NewGuid().ToString("N"));
        ProjectFolder = Path.Combine(RootPath, "ProjectsDate", $"Project_{ProjectId}");
        Directory.CreateDirectory(ProjectFolder);
        Service = new ProjectDataService(ProjectId, ProjectFolder);
    }

    /// <summary>创建一个隐患年月顶级分组（如 "2026年08月"），返回分组</summary>
    public Group AddYearMonthGroup(string name)
    {
        var g = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = ProjectId,
            Module = "Hazard",
            Name = name,
            CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
        };
        Assert.True(Service.AddGroup(g), $"添加年月分组失败: {name}");
        return g;
    }

    /// <summary>创建日期子分组（如 "08月05日"），返回分组</summary>
    public Group AddDateGroup(string parentId, string name, string? tag = null)
    {
        var g = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = ProjectId,
            Module = "Hazard",
            ParentId = parentId,
            Name = name,
            Tag = tag,
            CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
        };
        Assert.True(Service.AddGroup(g), $"添加日期分组失败: {name}");
        return g;
    }

    /// <summary>创建人员单位顶级分组（Personnel 模块）</summary>
    public Group AddPersonnelUnitGroup(string name)
    {
        var g = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = ProjectId,
            Module = "Personnel",
            Name = name,
            CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
        };
        Assert.True(Service.AddGroup(g), $"添加人员单位分组失败: {name}");
        return g;
    }

    /// <summary>在指定分组下添加一条隐患记录，返回记录</summary>
    public Record AddRecord(string groupId, string title, int displayIndex = 0)
    {
        var r = new Record
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            Title = title,
            YearMonth = "2026-08",
            DisplayIndex = displayIndex
        };
        Assert.True(Service.AddRecord(r), $"添加记录失败: {title}");
        return r;
    }

    /// <summary>在指定分组下添加一条人员记录，返回记录</summary>
    public PersonnelRecord AddPersonnel(string groupId, string name)
    {
        var p = new PersonnelRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = groupId,
            Title = name
        };
        Assert.True(Service.AddPersonnelRecord(p), $"添加人员失败: {name}");
        return p;
    }

    public void Dispose()
    {
        Service.Dispose();
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, true);
        }
        catch
        {
            // 清理失败不影响测试结论，忽略
        }
    }
}
