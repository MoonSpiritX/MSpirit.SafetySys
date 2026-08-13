// 附件（Attachments 表）核心行为测试：
// 增删改查、按类型过滤、批量查询、复制卡片（路径重建 + 物理文件复制）

using System.IO;
using MS_SafetyManagementSystem.Models;

namespace MSpirit.SafetySys.Tests;

public class AttachmentTests
{
    /// <summary>在记录下挂一个真实存在的附件文件，返回 Attachment 与完整路径</summary>
    private static Attachment CreateRealAttachment(
        TestProject tp, string recordId, string relPath, string attachmentType, int order, string fileName)
    {
        var fullPath = Path.Combine(tp.RootPath, "ProjectsDate", relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "fake-image-content");

        var att = new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = recordId,
            FileName = fileName,
            RelativePath = relPath,
            FileSize = 100,
            MimeType = "image/jpeg",
            AttachmentType = attachmentType,
            DisplayOrder = order
        };
        Assert.True(tp.Service.AddAttachment(att));
        return att;
    }

    [Fact]
    public void AddAttachment_新增附件_字段完整落库()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");
        var r = tp.AddRecord(d.Id, "带图隐患");

        var att = new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = r.Id,
            FileName = "Hazard-1_abc.jpg",
            RelativePath = $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r.Id}\\Hazard-1_abc.jpg",
            FileSize = 2048,
            MimeType = "image/jpeg",
            AttachmentType = "Hazard",
            DisplayOrder = 1
        };
        Assert.True(tp.Service.AddAttachment(att));

        var loaded = Assert.Single(tp.Service.GetAttachmentsByRecord(r.Id));
        Assert.Equal("Hazard-1_abc.jpg", loaded.FileName);
        Assert.Equal("Hazard", loaded.AttachmentType);
        Assert.Equal(1, loaded.DisplayOrder);
        Assert.False(string.IsNullOrEmpty(loaded.UploadedAt));
    }

    [Fact]
    public void GetAttachmentsByRecordAndType_按类型过滤且按槽位排序()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");
        var r = tp.AddRecord(d.Id, "带图隐患");

        // 打乱插入顺序：整改图2、隐患图1、整改图1
        CreateRealAttachment(tp, r.Id, $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r.Id}\\Remedy-2.jpg", "Remedy", 2, "Remedy-2.jpg");
        CreateRealAttachment(tp, r.Id, $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r.Id}\\Hazard-1.jpg", "Hazard", 1, "Hazard-1.jpg");
        CreateRealAttachment(tp, r.Id, $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r.Id}\\Remedy-1.jpg", "Remedy", 1, "Remedy-1.jpg");

        var hazards = tp.Service.GetAttachmentsByRecordAndType(r.Id, "Hazard");
        Assert.Single(hazards);
        Assert.Equal("Hazard-1.jpg", hazards[0].FileName);

        var remedies = tp.Service.GetAttachmentsByRecordAndType(r.Id, "Remedy");
        Assert.Equal(2, remedies.Count);
        Assert.Equal(1, remedies[0].DisplayOrder); // 按 DisplayOrder 升序
        Assert.Equal(2, remedies[1].DisplayOrder);
    }

    [Fact]
    public void UpdateAttachment_更新槽位与证书信息()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");
        var r = tp.AddRecord(d.Id, "带证人员");

        var att = CreateRealAttachment(tp, r.Id, $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r.Id}\\Cert-1.jpg", "Certificate", 1, "Cert-1.jpg");
        att.DisplayOrder = 3;
        att.CertName = "特种作业操作证";
        att.CertNumber = "T110101";
        Assert.True(tp.Service.UpdateAttachment(att));

        var loaded = Assert.Single(tp.Service.GetAttachmentsByRecord(r.Id));
        Assert.Equal(3, loaded.DisplayOrder);
        Assert.Equal("特种作业操作证", loaded.CertName);
        Assert.Equal("T110101", loaded.CertNumber);
    }

    [Fact]
    public void GetAttachmentsByRecordIds_批量查询()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");
        var r1 = tp.AddRecord(d.Id, "记录一");
        var r2 = tp.AddRecord(d.Id, "记录二");

        var a1 = CreateRealAttachment(tp, r1.Id, $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r1.Id}\\Hazard-1.jpg", "Hazard", 1, "Hazard-1.jpg");
        var a2 = CreateRealAttachment(tp, r2.Id, $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r2.Id}\\Hazard-1.jpg", "Hazard", 1, "Hazard-1.jpg");

        var result = tp.Service.GetAttachmentsByRecordIds(new List<string> { r1.Id, r2.Id });
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Id == a1.Id);
        Assert.Contains(result, x => x.Id == a2.Id);

        // 空列表不查询
        Assert.Empty(tp.Service.GetAttachmentsByRecordIds(new List<string>()));
    }

    [Fact]
    public void DeleteAttachment_删除单个附件()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");
        var r = tp.AddRecord(d.Id, "带附件");

        var att = CreateRealAttachment(tp, r.Id, $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{r.Id}\\Hazard-1.jpg", "Hazard", 1, "Hazard-1.jpg");

        Assert.True(tp.Service.DeleteAttachment(att.Id));
        Assert.Empty(tp.Service.GetAttachmentsByRecord(r.Id));
    }

    [Fact]
    public void CopyAttachmentsForNewRecord_跨年月复制_路径重建正确()
    {
        using var tp = new TestProject();
        // 源记录：2026-08 月
        var srcYm = tp.AddYearMonthGroup("2026年08月");
        var srcDate = tp.AddDateGroup(srcYm.Id, "08月05日");
        var srcRecord = tp.AddRecord(srcDate.Id, "源记录");
        srcRecord.YearMonth = "2026-08";
        tp.Service.UpdateRecord(srcRecord);

        // 源附件：隐患图 + 整改图 + 缩略图
        var rel = $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{srcRecord.Id}";
        CreateRealAttachment(tp, srcRecord.Id, $"{rel}\\Hazard-1_src.jpg", "Hazard", 1, "Hazard-1_src.jpg");
        CreateRealAttachment(tp, srcRecord.Id, $"{rel}\\Remedy-1_src.jpg", "Remedy", 1, "Remedy-1_src.jpg");
        var thumbRel = $"{rel}\\thumb\\Hazard-1_src_thumb.jpg";
        var thumbFull = Path.Combine(tp.RootPath, "ProjectsDate", thumbRel);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbFull)!);
        File.WriteAllText(thumbFull, "thumb-data");
        // 给 Hazard 附件补缩略图路径
        var hazardAtt = tp.Service.GetAttachmentsByRecordAndType(srcRecord.Id, "Hazard")[0];
        hazardAtt.ThumbnailPath = thumbRel;
        tp.Service.UpdateAttachment(hazardAtt);

        // 目标记录：2026-09 月（跨年月）
        var dstYm = tp.AddYearMonthGroup("2026年09月");
        var dstDate = tp.AddDateGroup(dstYm.Id, "09月10日");
        var dstRecord = tp.AddRecord(dstDate.Id, "目标记录");
        dstRecord.YearMonth = "2026-09";
        tp.Service.UpdateRecord(dstRecord);

        var failures = tp.Service.CopyAttachmentsForNewRecord(srcRecord.Id, dstRecord.Id, dstDate.Id, "2026-08");

        // 全部复制成功
        Assert.Empty(failures);

        var copied = tp.Service.GetAttachmentsByRecord(dstRecord.Id);
        Assert.Equal(2, copied.Count);

        foreach (var att in copied)
        {
            // 路径年月段已替换为目标年月，Card_ 已替换为目标记录
            Assert.StartsWith($"Project_{tp.ProjectId}\\Hazard_2026-09\\Card_{dstRecord.Id}", att.RelativePath.Replace("\\thumb\\", "\\"));
            // 物理文件真实存在
            var full = Path.Combine(tp.RootPath, "ProjectsDate", att.RelativePath);
            Assert.True(File.Exists(full), $"复制后的文件应存在: {full}");
        }

        // 缩略图也被复制
        var hazardCopied = tp.Service.GetAttachmentsByRecordAndType(dstRecord.Id, "Hazard")[0];
        Assert.False(string.IsNullOrEmpty(hazardCopied.ThumbnailPath));
        var thumbCopiedFull = Path.Combine(tp.RootPath, "ProjectsDate", hazardCopied.ThumbnailPath!);
        Assert.True(File.Exists(thumbCopiedFull), "缩略图应被复制");
        Assert.Contains($"Card_{dstRecord.Id}", hazardCopied.ThumbnailPath!);

        // 源附件不受影响（仍在 2026-08）
        var srcAfter = tp.Service.GetAttachmentsByRecord(srcRecord.Id);
        Assert.Equal(2, srcAfter.Count);
        Assert.All(srcAfter, a => Assert.Contains("Hazard_2026-08", a.RelativePath));
    }

    [Fact]
    public void CopyAttachmentsForNewRecord_源文件缺失_返回失败说明但不中断()
    {
        using var tp = new TestProject();
        var srcYm = tp.AddYearMonthGroup("2026年08月");
        var srcDate = tp.AddDateGroup(srcYm.Id, "08月05日");
        var srcRecord = tp.AddRecord(srcDate.Id, "源记录");

        // 数据库有附件记录，但物理文件不存在
        var att = new Attachment
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordId = srcRecord.Id,
            FileName = "Hazard-1_lost.jpg",
            RelativePath = $"Project_{tp.ProjectId}\\Hazard_2026-08\\Card_{srcRecord.Id}\\Hazard-1_lost.jpg",
            AttachmentType = "Hazard",
            DisplayOrder = 1
        };
        tp.Service.AddAttachment(att);

        var dstYm = tp.AddYearMonthGroup("2026年09月");
        var dstDate = tp.AddDateGroup(dstYm.Id, "09月10日");
        var dstRecord = tp.AddRecord(dstDate.Id, "目标记录");

        var failures = tp.Service.CopyAttachmentsForNewRecord(srcRecord.Id, dstRecord.Id, dstDate.Id, "2026-08");

        // 返回失败说明（"图片文件缺失"），且目标记录无任何附件
        Assert.NotEmpty(failures);
        Assert.Contains(failures, f => f.Contains("图片文件缺失"));
        Assert.Empty(tp.Service.GetAttachmentsByRecord(dstRecord.Id));
    }

    [Fact]
    public void CopyAttachmentsForNewRecord_源记录无附件_直接成功()
    {
        using var tp = new TestProject();
        var ym = tp.AddYearMonthGroup("2026年08月");
        var d = tp.AddDateGroup(ym.Id, "08月05日");
        var src = tp.AddRecord(d.Id, "无附件源");
        var dst = tp.AddRecord(d.Id, "无附件目标");

        var failures = tp.Service.CopyAttachmentsForNewRecord(src.Id, dst.Id, d.Id, "2026-08");
        Assert.Empty(failures);
    }
}
