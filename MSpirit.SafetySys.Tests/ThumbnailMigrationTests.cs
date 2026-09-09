// C 层：ThumbnailMigration 测试（旧版缩略图 Card_{id}/ → Card_{id}/thumb/ + 数据库路径更新）

using Dapper;
using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using System.IO;
using Record = MS_SafetyManagementSystem.Models.Record;

namespace MSpirit.SafetySys.Tests;

public class ThumbnailMigrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _projectId;
    private readonly string _cardDir;

    public ThumbnailMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "ThumbMig", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "ProjectsDate"));

        // 建 master + 项目
        var master = new MasterDbContext(_root);
        master.AddProject(new ProjectInfo { Id = "p1", Name = "迁移项目", Created = "2026-08-01" });
        _projectId = "p1";

        // 建项目库 + 记录（MasterDbContext.AddProject 只插记录不建文件夹，需手动建）
        string projectDir = Path.Combine(_root, "ProjectsDate", "Project_p1");
        Directory.CreateDirectory(projectDir);
        using var service = new ProjectDataService(_projectId, projectDir);
        var ym = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _projectId,
            Module = "Hazard",
            Name = "2026年08月",
            CreatedDate = "2026-08-01"
        };
        service.AddGroup(ym);
        var date = new Group
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = _projectId,
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
            Title = "记录",
            YearMonth = "2026-08"
        });
        var record = service.GetRecordsByGroup(date.Id).Single();

        // 物理文件（旧版：缩略图直接放卡片目录，无 thumb/ 子目录）
        _cardDir = Path.Combine(service.GetProjectsDateRoot(), "Project_p1", "Hazard_2026-08", $"Card_{record.Id}");
        Directory.CreateDirectory(_cardDir);
        File.WriteAllText(Path.Combine(_cardDir, "photo.jpg"), "原图");
        File.WriteAllText(Path.Combine(_cardDir, "photo_thumb.jpg"), "旧缩略图");

        // 附件记录：RelativePath 正常，ThumbnailPath 用旧格式（卡片目录下，无 thumb/）
        string relBase = $"Project_p1/Hazard_2026-08/Card_{record.Id}";
        service.AddAttachment(new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = record.Id,
            FileName = "photo.jpg",
            RelativePath = $"{relBase}/photo.jpg",
            ThumbnailPath = $"{relBase}/photo_thumb.jpg", // 旧格式
            AttachmentType = "Hazard",
            DisplayOrder = 1
        });
        // 一条已迁移的（含 thumb/，应跳过）
        service.AddAttachment(new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = record.Id,
            FileName = "photo2.jpg",
            RelativePath = $"{relBase}/photo2.jpg",
            ThumbnailPath = $"{relBase}/thumb/photo2_thumb.jpg",
            AttachmentType = "Hazard",
            DisplayOrder = 2
        });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch { /* 清理失败不影响测试结论 */ }
    }

    private string GetThumbnailPath(string attachmentFileName)
    {
        string db = Path.Combine(_root, "ProjectsDate", "Project_p1", "data.db");
        using var conn = new SqliteConnection($"Data Source={db}");
        conn.Open();
        return conn.QueryFirst<string>("SELECT ThumbnailPath FROM Attachments WHERE FileName = @Name", new { Name = attachmentFileName });
    }

    [Fact]
    public void 迁移_旧缩略图移入thumb子目录_数据库路径更新()
    {
        ThumbnailMigration.Run(_root);

        // 物理文件已移动
        Assert.False(File.Exists(Path.Combine(_cardDir, "photo_thumb.jpg")), "旧位置文件应被移走");
        Assert.True(File.Exists(Path.Combine(_cardDir, "thumb", "photo_thumb.jpg")), "新位置文件应存在");

        // 数据库路径已更新为含 thumb/
        string newPath = GetThumbnailPath("photo.jpg");
        Assert.Contains("/thumb/", newPath);
        Assert.EndsWith("photo_thumb.jpg", newPath);
    }

    [Fact]
    public void 迁移_已迁移的记录_跳过()
    {
        ThumbnailMigration.Run(_root);

        string migrated = GetThumbnailPath("photo2.jpg");
        Assert.Contains("/thumb/", migrated);
        Assert.Equal($"Project_p1/Hazard_2026-08/{Path.GetFileName(_cardDir)}/thumb/photo2_thumb.jpg", migrated);
    }

    [Fact]
    public void 迁移_幂等_二次运行无副作用()
    {
        ThumbnailMigration.Run(_root);
        ThumbnailMigration.Run(_root); // 不抛异常，文件不重复移动

        Assert.True(File.Exists(Path.Combine(_cardDir, "thumb", "photo_thumb.jpg")));
        Assert.False(File.Exists(Path.Combine(_cardDir, "photo_thumb.jpg")));
    }

    [Fact]
    public void 迁移_无master库_跳过不抛异常()
    {
        string emptyRoot = Path.Combine(Path.GetTempPath(), "MSpiritTests", "ThumbMig", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);
        try
        {
            ThumbnailMigration.Run(emptyRoot); // 不抛异常
        }
        finally
        {
            try { Directory.Delete(emptyRoot, true); } catch { }
        }
    }
}
