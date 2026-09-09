// HazardDataWriter 测试：按日期自动建组、指定分组、DisplayIndex 递增、标题生成、
// 日期解析、无效行计数、图片附件落库（A 层导入导出链路的写入端）

using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class HazardDataWriterTests : IDisposable
{
    private readonly TestProject _tp = new();

    // 1x1 透明 PNG（有效图片字节，ImageHelper 会解码/重编码）
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public void Dispose() => _tp.Dispose();

    private HazardDataWriter NewWriter() => new();

    private static ImportParseResult SingleRowResult(string description, string? recordDate = null)
    {
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            RowIndex = 0,
            TextValues = new Dictionary<string, string>
            {
                ["Description"] = description,
                ["Measure"] = "整改措施X"
            }
        });
        if (!string.IsNullOrEmpty(recordDate))
            result.Rows[0].TextValues["RecordDate"] = recordDate;
        return result;
    }

    private static ImportParseResult TwoRowResult()
    {
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            RowIndex = 0,
            TextValues = new Dictionary<string, string> { ["Description"] = "隐患一", ["RecordDate"] = "2026-08-15" }
        });
        result.Rows.Add(new ImportRowData
        {
            RowIndex = 1,
            TextValues = new Dictionary<string, string> { ["Description"] = "隐患二", ["RecordDate"] = "2026-08-15" }
        });
        return result;
    }

    // ===== 分组与落库 =====

    [Fact]
    public void Import_按日期自动建组_记录落库()
    {
        var result = SingleRowResult("脚手架缺少防护网", "2026-08-15");
        var (success, failed, errors) = NewWriter().Import(result, _tp.Service, "", "", true);

        Assert.Equal(1, success);
        Assert.Equal(0, failed);
        Assert.Empty(errors);

        // 自动创建的年月/日期分组存在
        var yearMonthGroups = _tp.Service.GetGroups("Hazard");
        var ym = Assert.Single(yearMonthGroups);
        Assert.Equal("2026年08月", ym.Name);
        var dateGroups = _tp.Service.GetGroups("Hazard", ym.Id);
        var date = Assert.Single(dateGroups);
        Assert.Equal("08月15日", date.Name);

        // 记录落库且字段完整
        var records = _tp.Service.GetRecordsByGroup(date.Id);
        var record = Assert.Single(records);
        Assert.Equal("2026-08", record.YearMonth);
        Assert.Equal("脚手架缺少防护网", record.Description);
        Assert.Equal("整改措施X", record.Measure);
    }

    [Fact]
    public void Import_指定分组_写入目标分组()
    {
        var ymGroup = _tp.AddYearMonthGroup("2026年08月");
        var dateGroup = _tp.AddDateGroup(ymGroup.Id, "08月15日");

        var result = SingleRowResult("隐患A");
        var (success, failed, _) = NewWriter().Import(result, _tp.Service, "2026-08", dateGroup.Id, false);

        Assert.Equal(1, success);
        Assert.Equal(0, failed);
        var records = _tp.Service.GetRecordsByGroup(dateGroup.Id);
        Assert.Single(records);
    }

    [Fact]
    public void Import_未指定分组且非自动分配_记录错误()
    {
        var result = SingleRowResult("隐患A");
        var (success, failed, errors) = NewWriter().Import(result, _tp.Service, "2026-08", "", false);

        Assert.Equal(0, success);
        Assert.Equal(1, failed);
        Assert.Contains(errors, e => e.Contains("未指定目标日期分组"));
    }

    [Fact]
    public void Import_自动分配但缺日期_记录错误()
    {
        var result = SingleRowResult("隐患A"); // 无 RecordDate
        var (success, failed, errors) = NewWriter().Import(result, _tp.Service, "", "", true);

        Assert.Equal(0, success);
        Assert.Equal(1, failed);
        Assert.Contains(errors, e => e.Contains("日期无效"));
    }

    [Fact]
    public void Import_无效行_计入失败()
    {
        var result = SingleRowResult("隐患A", "2026-08-15");
        result.Rows[0].IsValid = false;
        var (success, failed, _) = NewWriter().Import(result, _tp.Service, "", "", true);

        Assert.Equal(0, success);
        Assert.Equal(1, failed);
    }

    // ===== DisplayIndex 与标题 =====

    [Fact]
    public void Import_多行_DisplayIndex递增()
    {
        var result = TwoRowResult();
        var (success, failed, _) = NewWriter().Import(result, _tp.Service, "", "", true);

        Assert.Equal(2, success);
        var ym = _tp.Service.GetGroups("Hazard").Single();
        var date = _tp.Service.GetGroups("Hazard", ym.Id).Single();
        var records = _tp.Service.GetRecordsByGroup(date.Id);
        Assert.Equal(2, records.Count);
        Assert.Equal(new[] { 1, 2 }, records.Select(r => r.DisplayIndex).OrderBy(i => i).ToArray());
    }

    [Fact]
    public void Import_标题_位置加类型()
    {
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            RowIndex = 0,
            TextValues = new Dictionary<string, string>
            {
                ["Location"] = "工地A",
                ["HazardType"] = "临边防护",
                ["RecordDate"] = "2026-08-15"
            }
        });
        var (success, _, _) = NewWriter().Import(result, _tp.Service, "", "", true);
        Assert.Equal(1, success);

        var ym = _tp.Service.GetGroups("Hazard").Single();
        var date = _tp.Service.GetGroups("Hazard", ym.Id).Single();
        var rec = _tp.Service.GetRecordsByGroup(date.Id).Single();
        Assert.Equal("工地A-临边防护", rec.Title);
    }

    [Fact]
    public void Import_标题_仅描述超长截断()
    {
        string longDesc = new string('险', 60);
        var result = SingleRowResult(longDesc, "2026-08-15");
        NewWriter().Import(result, _tp.Service, "", "", true);

        var ym = _tp.Service.GetGroups("Hazard").Single();
        var date = _tp.Service.GetGroups("Hazard", ym.Id).Single();
        var rec = _tp.Service.GetRecordsByGroup(date.Id).Single();
        Assert.Equal(50, rec.Title.Length - 1); // 50 字 + "…"
        Assert.EndsWith("…", rec.Title);
    }

    // ===== 日期解析 =====

    [Theory]
    [InlineData("2026/08/15", "2026-08")]
    [InlineData("2026年8月5日", "2026-08")]
    [InlineData("20260815", "2026-08")]
    [InlineData("2026-08-15 14:30:00", "2026-08")]
    public void Import_多种日期格式_自动建组正确(string dateStr, string expectedYearMonth)
    {
        var result = SingleRowResult("隐患A", dateStr);
        var (success, failed, _) = NewWriter().Import(result, _tp.Service, "", "", true);
        Assert.Equal(1, success);
        Assert.Equal(0, failed);

        var ym = _tp.Service.GetGroups("Hazard").Single();
        Assert.Equal(expectedYearMonth.Replace("-", "年") + "月", ym.Name);
        var date = _tp.Service.GetGroups("Hazard", ym.Id).Single();
        Assert.Equal(expectedYearMonth, _tp.Service.GetYearMonthFromGroupId(date.Id));
    }

    // ===== 图片附件 =====

    [Fact]
    public void Import_图片附件_落库且路径正确()
    {
        // 临时真实图片文件
        string photoPath = Path.Combine(Path.GetTempPath(), "MSpiritTests", "HazardWriter", Guid.NewGuid().ToString("N") + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(photoPath)!);
        File.WriteAllBytes(photoPath, OnePixelPng);
        try
        {
            var result = SingleRowResult("隐患A", "2026-08-15");
            result.Rows[0].PhotoPaths["HazardPhoto"] = new List<string> { photoPath };

            var (success, failed, errors) = NewWriter().Import(result, _tp.Service, "", "", true);
            Assert.Equal(1, success);
            Assert.Equal(0, failed);
            Assert.Empty(errors);

            var ym = _tp.Service.GetGroups("Hazard").Single();
            var date = _tp.Service.GetGroups("Hazard", ym.Id).Single();
            var record = _tp.Service.GetRecordsByGroup(date.Id).Single();

            var attachments = _tp.Service.GetAttachmentsByRecord(record.Id);
            var attachment = Assert.Single(attachments);
            Assert.Equal("Hazard", attachment.AttachmentType);
            Assert.Equal(1, attachment.DisplayOrder);
            Assert.StartsWith($"Project_{_tp.ProjectId}\\Hazard_2026-08\\Card_", attachment.RelativePath.Replace('/', '\\'));
            Assert.False(string.IsNullOrEmpty(attachment.ThumbnailPath));
            // 物理文件已写入（相对 ProjectsDate 根）
            string fullPath = Path.Combine(_tp.Service.GetProjectsDateRoot(), attachment.RelativePath);
            Assert.True(File.Exists(fullPath), $"附件文件应存在: {fullPath}");
        }
        finally
        {
            try { if (File.Exists(photoPath)) File.Delete(photoPath); } catch { }
        }
    }

    [Fact]
    public void Import_图片文件不存在_跳过不中断()
    {
        var result = SingleRowResult("隐患A", "2026-08-15");
        result.Rows[0].PhotoPaths["HazardPhoto"] = new List<string> { @"Z:\不存在\missing.jpg" };

        var (success, failed, _) = NewWriter().Import(result, _tp.Service, "", "", true);
        Assert.Equal(1, success); // 记录仍写入，缺失图片跳过
        Assert.Equal(0, failed);
    }
}
