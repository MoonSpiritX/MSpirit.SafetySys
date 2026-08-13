// 隐患记录（Records 表）核心行为测试：
// 增删改查、字段完整落库、DisplayIndex、分页、排序、删除级联

using System.IO;
using MS_SafetyManagementSystem.Models;
using Record = MS_SafetyManagementSystem.Models.Record; // 避免与 Xunit.Record 冲突

namespace MSpirit.SafetySys.Tests;

public class RecordTests
{
    private static (TestProject tp, string dateGroupId) CreateEnv(TestProject tp)
    {
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");
        return (tp, d.Id);
    }

    [Fact]
    public void AddRecord_新增记录_字段完整落库()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");

        var r = new Record
        {
            Id = Guid.NewGuid().ToString("N"),
            GroupId = d.Id,
            Title = "配电箱未接地",
            Content = "三级配电箱 PE 线断开",
            Status = "进行",
            HazardType = "临时用电",
            HazardLevel = "一般隐患",
            Location = "3#楼一层",
            ResponsibleUnit = "机电班组",
            ResponsiblePerson = "张三",
            Measure = "重新压接接地线",
            Reviewer = "李四",
            DueDate = "2026-08-20",
            ExtraData = "{\"source\":\"manual\"}",
            YearMonth = "2026-08",
            DisplayIndex = 1
        };
        Assert.True(tp.Service.AddRecord(r));

        var records = tp.Service.GetRecordsByGroup(d.Id);
        var loaded = Assert.Single(records);
        Assert.Equal(r.Id, loaded.Id);
        Assert.Equal("配电箱未接地", loaded.Title);
        Assert.Equal("进行", loaded.Status);
        Assert.Equal("临时用电", loaded.HazardType);
        Assert.Equal("机电班组", loaded.ResponsibleUnit);
        Assert.Equal("张三", loaded.ResponsiblePerson);
        Assert.Equal("{\"source\":\"manual\"}", loaded.ExtraData);
        Assert.Equal(1, loaded.DisplayIndex);
        Assert.False(string.IsNullOrEmpty(loaded.CreatedAt)); // 入库时自动填充
    }

    [Fact]
    public void GetRecordsByGroup_按DisplayIndex升序()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);
        var r2 = tp.AddRecord(groupId, "记录二", displayIndex: 1);
        var r1 = tp.AddRecord(groupId, "记录一", displayIndex: 0);

        var records = tp.Service.GetRecordsByGroup(groupId);
        Assert.Equal(new[] { r1.Id, r2.Id }, records.Select(x => x.Id).ToArray());
    }

    [Fact]
    public void GetRecordsCountByGroup_计数正确()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);
        tp.AddRecord(groupId, "记录一");
        tp.AddRecord(groupId, "记录二");

        Assert.Equal(2, tp.Service.GetRecordsCountByGroup(groupId));
    }

    [Fact]
    public void GetRecordById_查询单个记录()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);
        var r = tp.AddRecord(groupId, "记录一");

        var loaded = tp.Service.GetRecordById(r.Id);
        Assert.NotNull(loaded);
        Assert.Equal("记录一", loaded.Title);

        Assert.Null(tp.Service.GetRecordById(Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void UpdateRecord_修改字段_生效且自动更新UpdatedAt()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);
        var r = tp.AddRecord(groupId, "记录一");

        r.Status = "完成";
        r.CompletedDate = DateTime.Now;
        r.Title = "记录一（已整改）";
        Assert.True(tp.Service.UpdateRecord(r));

        var loaded = tp.Service.GetRecordById(r.Id)!;
        Assert.Equal("完成", loaded.Status);
        Assert.Equal("记录一（已整改）", loaded.Title);
        Assert.NotNull(loaded.CompletedDate);
        Assert.False(string.IsNullOrEmpty(loaded.UpdatedAt)); // 更新时自动填充
    }

    [Fact]
    public void GetMaxDisplayIndex_初始为0_插入后返回最大值()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);

        Assert.Equal(0, tp.Service.GetMaxDisplayIndex(groupId));

        tp.AddRecord(groupId, "记录一", displayIndex: 5);
        tp.AddRecord(groupId, "记录二", displayIndex: 3);
        Assert.Equal(5, tp.Service.GetMaxDisplayIndex(groupId));
    }

    [Fact]
    public void GetRecordsByGroupPaged_分页正确()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);
        for (int i = 0; i < 5; i++)
            tp.AddRecord(groupId, $"记录{i}", displayIndex: i);

        // 第一页 2 条
        var page1 = tp.Service.GetRecordsByGroupPaged(groupId, 0, 2);
        Assert.Equal(2, page1.Count);
        Assert.Equal("记录0", page1[0].Title);

        // 第二页从索引 2 起
        var page2 = tp.Service.GetRecordsByGroupPaged(groupId, 2, 2);
        Assert.Equal(2, page2.Count);
        Assert.Equal("记录2", page2[0].Title);

        // 越界分页返回空
        Assert.Empty(tp.Service.GetRecordsByGroupPaged(groupId, 10, 2));
    }

    [Fact]
    public void DeleteRecord_删除记录_数据库记录消失()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);
        var r = tp.AddRecord(groupId, "待删除记录");

        Assert.True(tp.Service.DeleteRecord(r.Id));
        Assert.Null(tp.Service.GetRecordById(r.Id));
        Assert.Equal(0, tp.Service.GetRecordsCountByGroup(groupId));

        // 重复删除返回 false
        Assert.False(tp.Service.DeleteRecord(r.Id));
    }

    [Fact]
    public void DeleteRecord_级联删除附件数据库记录()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);
        var r = tp.AddRecord(groupId, "带附件记录");

        var att = new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = r.Id,
            FileName = "Hazard-1_test.jpg",
            RelativePath = $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r.Id}\\Hazard-1_test.jpg",
            FileSize = 1024,
            MimeType = "image/jpeg",
            AttachmentType = "Hazard",
            DisplayOrder = 1
        };
        Assert.True(tp.Service.AddAttachment(att));

        Assert.True(tp.Service.DeleteRecord(r.Id));
        Assert.Empty(tp.Service.GetAttachmentsByRecord(r.Id)); // 附件记录级联删除
    }

    [Fact]
    public void DeleteRecord_物理附件文件同步删除()
    {
        using var tp = new TestProject();
        var (_, groupId) = CreateEnv(tp);
        var r = tp.AddRecord(groupId, "带附件记录");

        // 构造真实的附件物理文件
        var relPath = $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r.Id}\\Hazard-1_test.jpg";
        var fullPath = Path.Combine(tp.RootPath, "ProjectsDate", relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "fake-jpeg");

        var att = new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = r.Id,
            FileName = "Hazard-1_test.jpg",
            RelativePath = relPath,
            AttachmentType = "Hazard",
            DisplayOrder = 1
        };
        tp.Service.AddAttachment(att);

        Assert.True(File.Exists(fullPath));
        tp.Service.DeleteRecord(r.Id);

        // 记录删除后物理文件应被清理（含卡片目录）
        Assert.False(File.Exists(fullPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(fullPath)!));
    }
}
