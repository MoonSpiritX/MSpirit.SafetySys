// ArchiveService 测试：.msarch 存档导出/检查/导入（B 层数据安全，端到端）
// 覆盖：导出打包（manifest/data.db/附件/Cache 排除）、检查（manifest + 本地匹配 + 兼容包兜底）、
// 四策略导入（Skip/Duplicate/Overwrite/Merge）、zip slip 防护、Groups.ProjectId 重写

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using System.IO;
using System.IO.Compression;
using Record = MS_SafetyManagementSystem.Models.Record; // 避免与 Xunit.Record 冲突

namespace MSpirit.SafetySys.Tests;

public class ArchiveServiceTests : IDisposable
{
    private readonly string _rootA; // 源数据根
    private readonly string _rootB; // 目标数据根
    private readonly string _archive;

    public ArchiveServiceTests()
    {
        _rootA = Path.Combine(Path.GetTempPath(), "MSpiritTests", "Archive", Guid.NewGuid().ToString("N"), "A");
        _rootB = Path.Combine(Path.GetTempPath(), "MSpiritTests", "Archive", Guid.NewGuid().ToString("N"), "B");
        Directory.CreateDirectory(_rootA);
        Directory.CreateDirectory(_rootB);
        _archive = Path.Combine(Path.GetTempPath(), "MSpiritTests", "Archive", Guid.NewGuid().ToString("N") + ".msarch");
    }

    public void Dispose()
    {
        try
        {
            foreach (var dir in new[] { _rootA, _rootB, Path.GetDirectoryName(_archive)! })
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            if (File.Exists(_archive)) File.Delete(_archive);
        }
        catch { /* 清理失败不影响测试结论 */ }
    }

    /// <summary>在指定数据根创建"项目甲"+ 年月/日期分组 + 1 条隐患记录 + 1 张附件图片</summary>
    private string SeedProject(string root, string name = "项目甲")
    {
        var config = new ConfigManager(root);
        config.AddProject(name);
        var project = config.GetProjectList()[0];

        using (var service = new ProjectDataService(project.Id, config.GetProjectFolder(project.Id)))
        {
            var ym = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = project.Id,
                Module = "Hazard",
                Name = "2026年08月",
                CreatedDate = "2026-08-01"
            };
            service.AddGroup(ym);
            var date = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = project.Id,
                Module = "Hazard",
                ParentId = ym.Id,
                Name = "08月15日",
                CreatedDate = "2026-08-15"
            };
            service.AddGroup(date);
            service.AddRecord(new Record
            {
                Id = Guid.NewGuid().ToString("N"),
                GroupId = date.Id,
                Title = "脚手架缺少防护网",
                YearMonth = "2026-08",
                Description = "测试隐患描述",
                DisplayIndex = 1
            });

            // 附件文件（绕过 ImageHelper 直接写文件，模拟图片附件）
            string cardDir = Path.Combine(service.GetProjectsDateRoot(), $"Project_{project.Id}", "Hazard_2026-08", "Card_seed1");
            Directory.CreateDirectory(cardDir);
            File.WriteAllText(Path.Combine(cardDir, "photo.jpg"), "fake-image-bytes");
            Directory.CreateDirectory(Path.Combine(cardDir, "thumb"));
            File.WriteAllText(Path.Combine(cardDir, "thumb", "photo_thumb.jpg"), "thumb-bytes");
        }
        return project.Id;
    }

    /// <summary>导出 rootA 的全部项目到 _archive</summary>
    private void ExportAll(string root)
    {
        var config = new ConfigManager(root);
        ArchiveService.ExportProjects(root, config.GetProjectList(), _archive);
    }

    // ===== 导出 =====

    [Fact]
    public void Export_打包_含manifest与data_db与附件_排除Cache()
    {
        SeedProject(_rootA);
        ExportAll(_rootA);

        using var zip = ZipFile.OpenRead(_archive);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("manifest.json", names);
        Assert.Contains(names, n => n.StartsWith("Project_") && n.EndsWith("/data.db"));
        Assert.Contains(names, n => n.Contains("/Hazard_2026-08/Card_seed1/photo.jpg"));
        Assert.Contains(names, n => n.Contains("/Hazard_2026-08/Card_seed1/thumb/photo_thumb.jpg"));
        Assert.DoesNotContain(names, n => n.Contains("/Cache/"));
    }

    [Fact]
    public void Export_空项目列表_抛异常()
    {
        Assert.Throws<ArgumentException>(() => ArchiveService.ExportProjects(_rootA, new List<ProjectInfo>(), _archive));
        Assert.Throws<ArgumentException>(() => ArchiveService.ExportProjects(_rootA, null!, _archive));
    }

    [Fact]
    public void Export_manifest_包含项目信息()
    {
        SeedProject(_rootA);
        ExportAll(_rootA);

        var inspection = ArchiveService.InspectArchive(_archive, _rootB);
        Assert.True(inspection.HasManifest);
        var item = Assert.Single(inspection.Items);
        Assert.Equal("项目甲", item.Name);
        Assert.StartsWith("Project_", item.Folder);
    }

    // ===== 检查 =====

    [Fact]
    public void Inspect_本地同名项目_匹配到LocalProject()
    {
        SeedProject(_rootA);
        ExportAll(_rootA);

        // 目标根也有同名项目
        SeedProject(_rootB);

        var inspection = ArchiveService.InspectArchive(_archive, _rootB);
        var item = Assert.Single(inspection.Items);
        Assert.NotNull(item.LocalProject);
        Assert.Equal("项目甲", item.LocalProject!.Name);
    }

    [Fact]
    public void Inspect_无manifest兼容包_文件夹名兜底()
    {
        // 手工构造无 manifest 的 zip（只有 Project_xxx 条目）
        string compat = Path.Combine(Path.GetTempPath(), "MSpiritTests", "Archive", Guid.NewGuid().ToString("N") + "_compat.msarch");
        try
        {
            using (var zip = ZipFile.Open(compat, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("Project_legacy1/data.db");
                using var sw = new StreamWriter(entry.Open());
                sw.Write("fake");
            }
            var inspection = ArchiveService.InspectArchive(compat, _rootB);
            Assert.False(inspection.HasManifest);
            var item = Assert.Single(inspection.Items);
            Assert.Equal("Project_legacy1", item.Folder);
            Assert.Equal("Project_legacy1", item.Name); // 名称兜底 = 文件夹名
        }
        finally
        {
            try { File.Delete(compat); } catch { }
        }
    }

    // ===== 导入四策略 =====

    [Fact]
    public void Import_Skip_不导入任何项目()
    {
        SeedProject(_rootA);
        ExportAll(_rootA);
        var inspection = ArchiveService.InspectArchive(_archive, _rootB);
        var plans = inspection.Items.Select(i => new ArchiveImportPlan
        {
            Folder = i.Folder,
            Name = i.Name,
            Created = i.Created,
            Strategy = ImportStrategy.Skip,
            TargetLocalProject = i.LocalProject
        }).ToList();

        var results = ArchiveService.ImportArchive(_rootB, _archive, plans);

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Contains("跳过", result.Message);
        Assert.Empty(new ConfigManager(_rootB).GetProjectList());
    }

    [Fact]
    public void Import_Duplicate_新ID副本_数据完整可查()
    {
        SeedProject(_rootA);
        ExportAll(_rootA);
        string localId = SeedProject(_rootB); // 目标根已有同名"项目甲" → 副本需加"(副本)"后缀
        var inspection = ArchiveService.InspectArchive(_archive, _rootB);
        var plans = inspection.Items.Select(i => new ArchiveImportPlan
        {
            Folder = i.Folder,
            Name = i.Name,
            Created = i.Created,
            Strategy = ImportStrategy.Duplicate,
            TargetLocalProject = i.LocalProject
        }).ToList();

        var results = ArchiveService.ImportArchive(_rootB, _archive, plans);
        Assert.True(Assert.Single(results).Success);

        // 副本项目已注册（新 ID + 副本名），本地原项目保留
        var configB = new ConfigManager(_rootB);
        var projects = configB.GetProjectList();
        Assert.Equal(2, projects.Count);
        var copy = projects.Single(p => p.Id != localId);
        Assert.Contains("副本", copy.Name);
        Assert.NotEqual(inspection.Items[0].Folder, $"Project_{copy.Id}");

        // 数据可查：分组/记录/附件完整（Groups.ProjectId 已重写为新 ID）
        using var service = new ProjectDataService(copy.Id, configB.GetProjectFolder(copy.Id));
        var ym = service.GetGroups("Hazard");
        var dateGroup = Assert.Single(ym.SelectMany(g => service.GetGroups("Hazard", g.Id)));
        Assert.Equal("2026年08月", ym.Single().Name);
        var record = Assert.Single(service.GetRecordsByGroup(dateGroup.Id));
        Assert.Equal("脚手架缺少防护网", record.Title);
        Assert.Equal(copy.Id, ym.Single().ProjectId); // ProjectId 重写验证
    }

    [Fact]
    public void Import_Overwrite_同名覆盖_数据可查()
    {
        SeedProject(_rootA);
        ExportAll(_rootA);
        SeedProject(_rootB); // 目标根已有同名"项目甲"
        var inspection = ArchiveService.InspectArchive(_archive, _rootB);
        Assert.NotNull(inspection.Items[0].LocalProject);

        var plans = inspection.Items.Select(i => new ArchiveImportPlan
        {
            Folder = i.Folder,
            Name = i.Name,
            Created = i.Created,
            Strategy = ImportStrategy.Overwrite,
            TargetLocalProject = i.LocalProject
        }).ToList();

        var results = ArchiveService.ImportArchive(_rootB, _archive, plans);
        Assert.True(Assert.Single(results).Success);

        // 覆盖后仍是同一个本地项目（ID 不变），数据来自包内
        var configB = new ConfigManager(_rootB);
        var project = Assert.Single(configB.GetProjectList());
        Assert.Equal("项目甲", project.Name);
        using var service = new ProjectDataService(project.Id, configB.GetProjectFolder(project.Id));
        var ym = service.GetGroups("Hazard");
        Assert.Single(ym);
        Assert.Equal(project.Id, ym.Single().ProjectId);
        var dateGroup = service.GetGroups("Hazard", ym.Single().Id).Single();
        var record = Assert.Single(service.GetRecordsByGroup(dateGroup.Id));
        Assert.Equal("脚手架缺少防护网", record.Title);
    }

    [Fact]
    public void Import_Merge_合并到本地_主键去重_附件补齐()
    {
        SeedProject(_rootA);
        ExportAll(_rootA);

        // 目标根先有同名项目 + 自己的记录
        string targetId = SeedProject(_rootB);
        var configB = new ConfigManager(_rootB);
        string targetFolder = configB.GetProjectFolder(targetId);
        using (var service = new ProjectDataService(targetId, targetFolder))
        {
            var ym = service.GetGroups("Hazard").Single();
            var date = service.GetGroups("Hazard", ym.Id).Single();
            service.AddRecord(new Record
            {
                Id = Guid.NewGuid().ToString("N"),
                GroupId = date.Id,
                Title = "本地已有记录",
                YearMonth = "2026-08",
                DisplayIndex = 2
            });
        }

        var inspection = ArchiveService.InspectArchive(_archive, _rootB);
        var plans = inspection.Items.Select(i => new ArchiveImportPlan
        {
            Folder = i.Folder,
            Name = i.Name,
            Created = i.Created,
            Strategy = ImportStrategy.Merge,
            TargetLocalProject = i.LocalProject
        }).ToList();

        var results = ArchiveService.ImportArchive(_rootB, _archive, plans);
        Assert.True(Assert.Single(results).Success);
        Assert.Contains("合并", Assert.Single(results).Message);

        // 合并后：本地记录 + 包内记录都在（本地与包内分组 ID 不同 → 并存）
        using var serviceB = new ProjectDataService(targetId, configB.GetProjectFolder(targetId));
        var ymB = serviceB.GetGroups("Hazard");
        var allTitles = ymB
            .SelectMany(g => serviceB.GetGroups("Hazard", g.Id))
            .SelectMany(g => serviceB.GetRecordsByGroup(g.Id))
            .Select(r => r.Title)
            .ToList();
        Assert.Contains("本地已有记录", allTitles);
        Assert.Contains("脚手架缺少防护网", allTitles);
        // 包内分组的 ProjectId 已重写为本地项目 ID
        Assert.All(ymB, g => Assert.Equal(targetId, g.ProjectId));
    }

    // ===== zip slip 防护 =====

    [Fact]
    public void Import_恶意路径条目_中止导入()
    {
        // 构造含 ../ 路径穿越条目的存档
        string evil = Path.Combine(Path.GetTempPath(), "MSpiritTests", "Archive", Guid.NewGuid().ToString("N") + "_evil.msarch");
        try
        {
            using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
            {
                zip.CreateEntry("../evil.txt");
            }
            var plans = new List<ArchiveImportPlan>
            {
                new() { Folder = "Project_x", Name = "x", Strategy = ImportStrategy.Duplicate }
            };
            Assert.Throws<InvalidDataException>(() => ArchiveService.ImportArchive(_rootB, evil, plans));
        }
        finally
        {
            try { File.Delete(evil); } catch { }
        }
    }

    [Fact]
    public void Import_空计划_抛异常()
    {
        Assert.Throws<ArgumentException>(() => ArchiveService.ImportArchive(_rootB, _archive, new List<ArchiveImportPlan>()));
    }
}
