// MasterDbContext 测试：主库（项目列表 + 设置）CRUD 与旧列迁移（B 层数据安全）

using Dapper;
using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class MasterDbContextTests : IDisposable
{
    private readonly string _root;

    public MasterDbContextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "MasterDb", Guid.NewGuid().ToString("N"));
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

    private MasterDbContext NewMaster() => new(_root);

    [Fact]
    public void 构造_创建ProjectsDate下的master库()
    {
        NewMaster();
        Assert.True(File.Exists(Path.Combine(_root, "ProjectsDate", "master.db")));
    }

    [Fact]
    public void 项目_初始为空_添加后SortOrder递增()
    {
        var db = NewMaster();
        Assert.Empty(db.GetAllProjects());

        db.AddProject(new ProjectInfo { Id = "p1", Name = "甲", Created = "2026-08-01" });
        db.AddProject(new ProjectInfo { Id = "p2", Name = "乙", Created = "2026-08-02" });

        var list = db.GetAllProjects();
        Assert.Equal(2, list.Count);
        Assert.Equal(0, list[0].SortOrder);
        Assert.Equal(1, list[1].SortOrder);
    }

    [Fact]
    public void 项目_按ID查询与更新()
    {
        var db = NewMaster();
        db.AddProject(new ProjectInfo { Id = "p1", Name = "原名", Created = "2026-08-01" });

        var found = db.GetProjectById("p1");
        Assert.NotNull(found);
        Assert.Equal("原名", found.Name);

        found!.Name = "改名后";
        Assert.True(db.UpdateProject(found));
        Assert.Equal("改名后", db.GetProjectById("p1")!.Name);

        Assert.Null(db.GetProjectById("不存在"));
    }

    [Fact]
    public void 项目_删除()
    {
        var db = NewMaster();
        db.AddProject(new ProjectInfo { Id = "p1", Name = "待删", Created = "" });
        Assert.True(db.DeleteProject("p1"));
        Assert.Empty(db.GetAllProjects());
        Assert.False(db.DeleteProject("p1")); // 已删
    }

    [Fact]
    public void 项目_重排序_事务生效()
    {
        var db = NewMaster();
        db.AddProject(new ProjectInfo { Id = "p1", Name = "甲", Created = "" });
        db.AddProject(new ProjectInfo { Id = "p2", Name = "乙", Created = "" });
        db.AddProject(new ProjectInfo { Id = "p3", Name = "丙", Created = "" });

        db.ReorderProjects(new List<string> { "p3", "p1", "p2" });
        var list = db.GetAllProjects();
        Assert.Equal(new[] { "p3", "p1", "p2" }, list.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void 设置_读写与覆盖()
    {
        var db = NewMaster();
        Assert.Null(db.GetSetting("不存在的键"));

        db.SetSetting("Key1", "值1");
        Assert.Equal("值1", db.GetSetting("Key1"));

        db.SetSetting("Key1", "值2"); // 覆盖
        Assert.Equal("值2", db.GetSetting("Key1"));
        Assert.Empty(db.GetAllProjects()); // 设置写入不影响项目列表（表独立）
    }

    [Fact]
    public void 迁移_旧Order列自动重命名为SortOrder()
    {
        // 手写一个旧结构 master.db（Projects 表用 Order 列），模拟老版本库
        string dbPath = Path.Combine(_root, "ProjectsDate", "master.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            conn.Execute(@"
                CREATE TABLE Projects (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Created TEXT NOT NULL,
                    [Order] INTEGER NOT NULL
                );
                CREATE TABLE Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL
                );
                INSERT INTO Projects (Id, Name, Created, [Order]) VALUES ('old1', '老项目', '2026-01-01', 0);
            ");
        }

        // 打开库触发迁移
        var db = NewMaster();
        var list = db.GetAllProjects();
        var project = Assert.Single(list);
        Assert.Equal("老项目", project.Name);
        Assert.Equal(0, project.SortOrder); // 迁移后 Order 值可读

        // 迁移后新列存在，旧列已删
        using (var verify = new SqliteConnection($"Data Source={dbPath}"))
        {
            verify.Open();
            var cols = verify.Query<string>("SELECT name FROM pragma_table_info('Projects')").ToList();
            Assert.Contains("SortOrder", cols);
            Assert.DoesNotContain("Order", cols);
        }
    }
}
