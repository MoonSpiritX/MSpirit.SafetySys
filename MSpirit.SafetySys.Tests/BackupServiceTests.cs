// BackupService 测试：自动备份内容完整性 + 轮换策略（30 天 / 100 份）（B 层数据安全）
// 注意：备份验证程序打开备份库必须 Pooling=False（否则句柄被池持有导致轮换删除失败）

using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataPathService _dataPath;

    public BackupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "Backup", Guid.NewGuid().ToString("N"));
        _dataPath = new DataPathService(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch { /* 清理失败不影响测试结论 */ }
    }

    private string BackupRoot => Path.Combine(_root, BackupService.BackupRootName);

    /// <summary>造一套完整数据：master.db + 4 个 json + 一个项目 data.db</summary>
    private void SeedData()
    {
        var master = new MasterDbContext(_root);
        master.AddProject(new ProjectInfo { Id = "p1", Name = "备份项目", Created = "2026-08-01" });

        foreach (var name in new[] { "MappingDictionary.json", "CommonHazardTypes.json", "FavoriteLinks.json", "ViewState.json" })
            File.WriteAllText(Path.Combine(_root, name), "{}");

        string projectDir = Path.Combine(_root, "ProjectsDate", "Project_p1");
        Directory.CreateDirectory(projectDir);
        // 项目库（用 ProjectDataService 建真实库）
        using var service = new ProjectDataService("p1", projectDir);
        _ = service.GetGroups("Hazard");
    }

    private int CountBackups() => Directory.Exists(BackupRoot)
        ? Directory.GetDirectories(BackupRoot, "Backup_*").Length
        : 0;

    private string CreateBackupDir(DateTime time)
    {
        string dir = Path.Combine(BackupRoot, $"Backup_{time:yyyyMMdd_HHmmssfff}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ===== 备份内容 =====

    [Fact]
    public void Shutdown备份_内容完整_相对结构保持()
    {
        SeedData();
        BackupService.RunShutdownBackup(_dataPath);

        var backups = Directory.GetDirectories(BackupRoot, "Backup_*");
        var backup = Assert.Single(backups);

        Assert.True(File.Exists(Path.Combine(backup, "master.db")));
        Assert.True(File.Exists(Path.Combine(backup, "MappingDictionary.json")));
        Assert.True(File.Exists(Path.Combine(backup, "CommonHazardTypes.json")));
        Assert.True(File.Exists(Path.Combine(backup, "FavoriteLinks.json")));
        Assert.True(File.Exists(Path.Combine(backup, "ViewState.json")));
        Assert.True(File.Exists(Path.Combine(backup, "Project_p1", "data.db")));

        // 备份库可打开查询（Pooling=False 防句柄占用）
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(backup, "Project_p1", "data.db")};Pooling=False"))
        {
            conn.Open();
        }
    }

    [Fact]
    public void 无可备份数据_不创建备份目录()
    {
        // 空数据根（无 master.db/json/项目）
        BackupService.RunShutdownBackup(_dataPath);
        Assert.Equal(0, CountBackups());
    }

    [Fact]
    public void 多次备份_时间戳含毫秒_不撞目录()
    {
        SeedData();
        BackupService.RunShutdownBackup(_dataPath);
        BackupService.RunShutdownBackup(_dataPath);
        Assert.Equal(2, CountBackups()); // 毫秒级时间戳保证两次不覆盖
    }

    // ===== 轮换 =====

    [Fact]
    public void 轮换_超期备份删除_近期保留()
    {
        SeedData();
        string oldDir = CreateBackupDir(DateTime.Now.AddDays(-31)); // 31 天前 → 应删
        string recentDir = CreateBackupDir(DateTime.Now.AddDays(-29)); // 29 天前 → 保留

        BackupService.RunShutdownBackup(_dataPath);

        Assert.False(Directory.Exists(oldDir), "31 天前的备份应被删除");
        Assert.True(Directory.Exists(recentDir), "29 天前的备份应保留");
        Assert.Equal(2, CountBackups()); // 29 天前 + 本次新备份
    }

    [Fact]
    public void 轮换_份数封顶_保留最新100份()
    {
        SeedData();
        for (int i = 0; i < 105; i++)
        {
            CreateBackupDir(DateTime.Now.AddMinutes(-(i + 1))); // 105 份旧备份
        }

        BackupService.RunShutdownBackup(_dataPath); // 再产生 1 份 → 106 份 → 封顶 100

        Assert.Equal(100, CountBackups());
    }
}
