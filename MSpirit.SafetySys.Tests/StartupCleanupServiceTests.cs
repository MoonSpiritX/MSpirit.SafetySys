// StartupCleanupService 测试：启动残留清理（待清理目录 + 空文件夹扫描）（B 层数据安全）

using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class StartupCleanupServiceTests : IDisposable
{
    private readonly string _root;

    public StartupCleanupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "Cleanup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "ProjectsDate"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch { /* 清理失败不影响测试结论 */ }
    }

    private string ProjectsDate => Path.Combine(_root, "ProjectsDate");

    private string CreateProjectDir(string id, bool withDb = false)
    {
        string dir = Path.Combine(ProjectsDate, $"Project_{id}");
        Directory.CreateDirectory(dir);
        // AddProject 已建过 data.db 时（文件被池连接占用）不覆盖；withDb 且不存在才创建
        string dbPath = Path.Combine(dir, "data.db");
        if (withDb && !File.Exists(dbPath))
            File.WriteAllText(dbPath, "");
        return dir;
    }

    // ===== 待清理目录记录 =====

    [Fact]
    public void 待清理目录_存在则删除并移除记录()
    {
        string leftover = Path.Combine(_root, "残留目录");
        Directory.CreateDirectory(leftover);
        var setting = new SettingFileService(_root);
        setting.AddPendingDeleteDir(leftover);

        StartupCleanupService.Run(_root);

        Assert.False(Directory.Exists(leftover));
        var fresh = new SettingFileService(_root);
        Assert.Empty(fresh.PendingDeleteDirs);
    }

    [Fact]
    public void 待清理目录_已不存在_仅清记录()
    {
        string gone = Path.Combine(_root, "已经不存在的目录");
        var setting = new SettingFileService(_root);
        setting.AddPendingDeleteDir(gone);

        StartupCleanupService.Run(_root);

        var fresh = new SettingFileService(_root);
        Assert.Empty(fresh.PendingDeleteDirs);
    }

    // ===== 孤儿空项目目录 =====

    [Fact]
    public void 孤儿空项目目录_删除()
    {
        // 注意：含任何子目录（哪怕空 Cache）的目录 IsDirEmptyRecursive 判定为"非空"，
        // 本测试构造纯空目录（无子目录）验证删除路径
        string orphan = CreateProjectDir("orphan-empty");

        StartupCleanupService.Run(_root);

        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public void 有数据的孤儿目录_不删()
    {
        string orphan = CreateProjectDir("orphan-data");
        File.WriteAllText(Path.Combine(orphan, "readme.txt"), "有内容");

        StartupCleanupService.Run(_root);

        Assert.True(Directory.Exists(orphan));
    }

    // ===== 已注册项目 =====

    [Fact]
    public void 已注册项目目录_即使为空也不删()
    {
        // 注册一个项目（master.db 有记录）
        var config = new ConfigManager(_root);
        config.AddProject("正式项目");
        string id = config.GetProjectList()[0].Id;
        string projectDir = CreateProjectDir(id); // 空目录（无 data.db）

        StartupCleanupService.Run(_root);

        Assert.True(Directory.Exists(projectDir), "已注册项目目录不应被清理");
    }

    [Fact]
    public void 已注册项目内_空卡片目录_删除()
    {
        var config = new ConfigManager(_root);
        config.AddProject("正式项目");
        string id = config.GetProjectList()[0].Id;
        string projectDir = CreateProjectDir(id, withDb: true);

        string emptyCard = Path.Combine(projectDir, "Hazard_2026-08", "Card_empty1");
        Directory.CreateDirectory(emptyCard);
        string fullCard = Path.Combine(projectDir, "Hazard_2026-08", "Card_full1");
        Directory.CreateDirectory(fullCard);
        File.WriteAllText(Path.Combine(fullCard, "photo.jpg"), "x");

        StartupCleanupService.Run(_root);

        Assert.False(Directory.Exists(emptyCard), "空卡片目录应删除");
        Assert.True(Directory.Exists(fullCard), "有内容的卡片目录应保留");
    }

    [Fact]
    public void 无ProjectsDate目录_不抛异常()
    {
        string bare = Path.Combine(_root, "bare");
        Directory.CreateDirectory(bare);
        StartupCleanupService.Run(bare); // 不抛异常
    }

    [Fact]
    public void 删除失败_保留记录下次重试()
    {
        // 目录被占用无法删除的场景难以稳定模拟（文件锁），改用"目录路径非法"验证失败分支：
        // 记录一个会被 Delete 抛异常的路径 → 应保留在记录中
        var setting = new SettingFileService(_root);
        string locked = Path.Combine(_root, "locked-dir");
        Directory.CreateDirectory(locked);
        // 用一个文件锁住目录使其删除失败：打开目录内的文件句柄
        string lockFile = Path.Combine(locked, "locked.txt");
        File.WriteAllText(lockFile, "");
        using (var handle = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            setting.AddPendingDeleteDir(locked);
            StartupCleanupService.Run(_root);
            // 删除失败 → 记录保留
            var fresh = new SettingFileService(_root);
            Assert.Contains(fresh.PendingDeleteDirs, d => d.Equals(locked, StringComparison.OrdinalIgnoreCase));
        }
        // 释放句柄后手动清掉（避免残留影响其他测试）
        var finalSetting = new SettingFileService(_root);
        finalSetting.RemovePendingDeleteDir(locked);
    }
}
