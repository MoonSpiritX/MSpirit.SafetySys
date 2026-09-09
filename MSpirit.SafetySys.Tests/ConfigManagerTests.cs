// ConfigManager / MasterDbContext 测试：项目生命周期管理（B 层数据安全）
// 覆盖：项目 CRUD、重名拒绝、排序、当前项目切换、ValidateCurrentProject 文件夹丢失恢复、
// 存档注册 AddProjectRecord、Order→SortOrder 旧列迁移

using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class ConfigManagerTests : IDisposable
{
    private readonly string _root;

    public ConfigManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "Config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch { /* 清理失败不影响测试结论 */ }
    }

    private ConfigManager NewConfig() => new(_root);

    // ===== 项目 CRUD =====

    [Fact]
    public void AddProject_成功_创建记录与文件夹()
    {
        var config = NewConfig();
        Assert.True(config.AddProject("一号项目"));

        var projects = config.GetProjectList();
        var project = Assert.Single(projects);
        Assert.Equal("一号项目", project.Name);
        Assert.True(Directory.Exists(config.GetProjectFolder(project.Id)));
        Assert.True(File.Exists(Path.Combine(config.GetProjectFolder(project.Id), "data.db"))); // 空库已建
    }

    [Fact]
    public void AddProject_重名_拒绝()
    {
        var config = NewConfig();
        Assert.True(config.AddProject("同名项目"));
        Assert.False(config.AddProject("同名项目"));       // 完全同名
        Assert.False(config.AddProject("同名项目 "));       // 去空格后同名
        Assert.False(config.AddProject("同名项目"));        // 大小写不敏感（中文无大小写，用英文验证）
    }

    [Fact]
    public void AddProject_重名大小写不敏感_拒绝()
    {
        var config = NewConfig();
        Assert.True(config.AddProject("ABC项目"));
        Assert.False(config.AddProject("abc项目"));
    }

    [Fact]
    public void AddProject_空白名_拒绝()
    {
        var config = NewConfig();
        Assert.False(config.AddProject(""));
        Assert.False(config.AddProject("   "));
        Assert.False(config.AddProject(null!));
    }

    [Fact]
    public void RenameProject_成功与重名拒绝()
    {
        var config = NewConfig();
        config.AddProject("原名");
        string id = config.GetProjectList()[0].Id;

        Assert.True(config.RenameProject(id, "新名"));
        Assert.Equal("新名", config.GetProjectById(id)!.Name);

        config.AddProject("另一个");
        Assert.False(config.RenameProject(id, "另一个")); // 与其他项目重名
        Assert.False(config.RenameProject("不存在的id", "随便"));
    }

    [Fact]
    public void DeleteProject_删除记录与文件夹()
    {
        var config = NewConfig();
        config.AddProject("待删项目");
        string id = config.GetProjectList()[0].Id;
        string folder = config.GetProjectFolder(id);

        Assert.True(config.DeleteProject(id));
        Assert.Empty(config.GetProjectList());

        // DeleteProject 内部已 ClearPool + checkpoint + Sleep(50)，正常情况下文件夹应已删除。
        // 兜底：池连接可能仍持有 data.db 句柄（Windows 文件锁），手动清池后再验证文件夹删除。
        using (var probe = new SqliteConnection($"Data Source={Path.Combine(folder, "data.db")}"))
        {
            SqliteConnection.ClearPool(probe);
        }
        Assert.False(Directory.Exists(folder), "清池后项目文件夹应已被删除");
    }

    [Fact]
    public void DeleteProject_不存在的ID_返回false()
    {
        var config = NewConfig();
        Assert.False(config.DeleteProject("不存在的id"));
    }

    [Fact]
    public void DeleteProject_删除当前项目_自动切换到相邻项目()
    {
        var config = NewConfig();
        config.AddProject("项目A");
        config.AddProject("项目B");
        config.AddProject("项目C");
        var projects = config.GetProjectList();
        config.SetCurrentProjectId(projects[1].Id); // 当前=B（中间）

        Assert.True(config.DeleteProject(projects[1].Id));

        // 删除中间项后，新当前项目 = 原位置的项目（remaining[min(原索引, 剩余数-1)] = C）
        string currentId = config.GetCurrentProjectId();
        Assert.Equal(projects[2].Id, currentId);
    }

    [Fact]
    public void DeleteProject_删除唯一项目_清空当前项目ID()
    {
        var config = NewConfig();
        config.AddProject("唯一项目");
        string id = config.GetProjectList()[0].Id;
        config.SetCurrentProjectId(id);

        Assert.True(config.DeleteProject(id));
        Assert.Equal("", config.GetCurrentProjectId());
    }

    [Fact]
    public void DeleteProject_当前项目_触发事件()
    {
        var config = NewConfig();
        config.AddProject("触发项目");
        string id = config.GetProjectList()[0].Id;
        config.SetCurrentProjectId(id);

        string? fired = null;
        config.CurrentProjectWillBeDeleted += pid => fired = pid;
        config.DeleteProject(id);

        Assert.Equal(id, fired);
    }

    // ===== 排序 =====

    [Fact]
    public void MoveProject_上下移动_排序变化()
    {
        var config = NewConfig();
        config.AddProject("A");
        config.AddProject("B");
        config.AddProject("C");
        var projects = config.GetProjectList();

        config.MoveProject(projects[0].Id, +1); // A 下移一位
        var after = config.GetProjectList();
        Assert.Equal("B", after[0].Name);
        Assert.Equal("A", after[1].Name);
        Assert.Equal("C", after[2].Name);

        config.MoveProject(after[2].Id, -1); // C 上移一位
        var after2 = config.GetProjectList();
        Assert.Equal("B", after2[0].Name);
        Assert.Equal("C", after2[1].Name);
        Assert.Equal("A", after2[2].Name);
    }

    [Fact]
    public void MoveProject_越界_不变()
    {
        var config = NewConfig();
        config.AddProject("A");
        config.AddProject("B");
        var projects = config.GetProjectList();

        config.MoveProject(projects[0].Id, -1); // 已在顶部
        config.MoveProject(projects[1].Id, +1); // 已在底部
        Assert.Equal(2, config.GetProjectList().Count);
    }

    [Fact]
    public void MoveToTop_与_MoveToBottom()
    {
        var config = NewConfig();
        config.AddProject("A");
        config.AddProject("B");
        config.AddProject("C");
        var projects = config.GetProjectList();

        config.MoveToTop(projects[2].Id); // C 置顶
        Assert.Equal("C", config.GetProjectList()[0].Name);

        config.MoveToBottom(config.GetProjectList()[0].Id); // C 置底
        Assert.Equal("C", config.GetProjectList()[2].Name);
    }

    // ===== 当前项目 =====

    [Fact]
    public void SetCurrentProjectId_有效与无效()
    {
        var config = NewConfig();
        config.AddProject("项目");
        string id = config.GetProjectList()[0].Id;

        config.SetCurrentProjectId(id);
        Assert.Equal(id, config.GetCurrentProjectId());

        config.SetCurrentProjectId("不存在的id");
        Assert.Equal("", config.GetCurrentProjectId());
    }

    // ===== ValidateCurrentProject =====

    [Fact]
    public void ValidateCurrentProject_无项目_返回false()
    {
        var config = NewConfig();
        Assert.False(config.ValidateCurrentProject());
    }

    [Fact]
    public void ValidateCurrentProject_无当前项目_自动选第一个()
    {
        var config = NewConfig();
        config.AddProject("第一个项目");
        string firstId = config.GetProjectList()[0].Id;

        Assert.True(config.ValidateCurrentProject());
        Assert.Equal(firstId, config.GetCurrentProjectId());
    }

    [Fact]
    public void ValidateCurrentProject_项目文件夹丢失_删记录并切换()
    {
        var config = NewConfig();
        config.AddProject("丢失项目");
        config.AddProject("幸存项目");
        var projects = config.GetProjectList();
        config.SetCurrentProjectId(projects[0].Id);

        // 模拟数据盘损坏/误删：清池释放句柄后删除物理文件夹
        string folder = config.GetProjectFolder(projects[0].Id);
        using (var probe = new SqliteConnection($"Data Source={Path.Combine(folder, "data.db")}"))
        {
            SqliteConnection.ClearPool(probe);
        }
        Directory.Delete(folder, true);

        Assert.True(config.ValidateCurrentProject());
        Assert.Single(config.GetProjectList());
        Assert.Equal(projects[1].Id, config.GetCurrentProjectId());
    }

    [Fact]
    public void ValidateCurrentProject_全部丢失_返回false()
    {
        var config = NewConfig();
        config.AddProject("唯一项目");
        string id = config.GetProjectList()[0].Id;
        config.SetCurrentProjectId(id);

        string folder = config.GetProjectFolder(id);
        using (var probe = new SqliteConnection($"Data Source={Path.Combine(folder, "data.db")}"))
        {
            SqliteConnection.ClearPool(probe);
        }
        Directory.Delete(folder, true);

        Assert.False(config.ValidateCurrentProject());
        Assert.Equal("", config.GetCurrentProjectId());
    }

    // ===== AddProjectRecord（存档导入注册） =====

    [Fact]
    public void AddProjectRecord_注册成功_不建空库()
    {
        var config = NewConfig();
        Assert.True(config.AddProjectRecord("fixed-id-1", "存档项目", "2026-08-01"));

        var project = config.GetProjectById("fixed-id-1");
        Assert.NotNull(project);
        Assert.Equal("存档项目", project.Name);
        Assert.True(Directory.Exists(config.GetProjectFolder("fixed-id-1")));
        // 仅建记录与文件夹，不建空库（导入方会用包内 data.db 覆盖）
        Assert.False(File.Exists(Path.Combine(config.GetProjectFolder("fixed-id-1"), "data.db")));
    }

    [Fact]
    public void AddProjectRecord_ID或名称重复_拒绝()
    {
        var config = NewConfig();
        Assert.True(config.AddProjectRecord("fixed-id-1", "存档项目", ""));
        Assert.False(config.AddProjectRecord("fixed-id-1", "另一个名", "")); // ID 重复
        Assert.False(config.AddProjectRecord("fixed-id-2", "存档项目", "")); // 名称重复
        Assert.False(config.AddProjectRecord("", "空ID", ""));
    }
}
