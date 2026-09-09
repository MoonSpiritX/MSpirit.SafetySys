// ExcelSmartImporter 回归测试：表头识别、列映射、数据行读取、FindLastDataRow「数据行优先」修复
// 2026-08-14 修复回归：数据单元格以"日期/审核/注："开头不得被误判为底部说明行而截断

using ClosedXML.Excel;
using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class ExcelSmartImporterTests : IDisposable
{
    private readonly string _root;
    private readonly MappingDictionaryService _mapping;

    // 1x1 透明 PNG（有效图片字节，ClosedXML AddPicture 会校验格式）
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public ExcelSmartImporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "ExcelImporter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _mapping = new MappingDictionaryService(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch { /* 清理失败不影响测试结论 */ }
    }

    private ExcelSmartImporter NewImporter(string module = "Hazard") => new(_mapping, module);

    /// <summary>生成含表头（序号/隐患描述/整改措施）的 xlsx，由 fill 填充数据区</summary>
    private string CreateHazardWorkbook(Action<IXLWorksheet> fill)
    {
        string file = Path.Combine(_root, $"{Guid.NewGuid():N}.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "序号";
            ws.Cell(1, 2).Value = "隐患描述";
            ws.Cell(1, 3).Value = "整改措施";
            fill(ws);
            wb.SaveAs(file);
        }
        return file;
    }

    // ===== 基本解析 =====

    [Fact]
    public void Parse_基本解析_三行全部有效且字段映射正确()
    {
        string file = CreateHazardWorkbook(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "脚手架缺少防护网"; ws.Cell(2, 3).Value = "加装防护网";
            ws.Cell(3, 1).Value = 2; ws.Cell(3, 2).Value = "灭火器过期"; ws.Cell(3, 3).Value = "更换灭火器";
            ws.Cell(4, 1).Value = 3; ws.Cell(4, 2).Value = "临边防护缺失"; ws.Cell(4, 3).Value = "补齐防护";
        });

        var result = NewImporter().Parse(file);

        Assert.Equal(3, result.ValidRowCount);
        Assert.Equal(3, result.TotalRowCount);
        Assert.Equal("脚手架缺少防护网", result.Rows[0].TextValues["Description"]);
        Assert.Equal("加装防护网", result.Rows[0].TextValues["Measure"]);
        Assert.Equal("灭火器过期", result.Rows[1].TextValues["Description"]);
    }

    [Fact]
    public void Parse_序号列_不进入文本字段()
    {
        string file = CreateHazardWorkbook(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "隐患A"; ws.Cell(2, 3).Value = "措施A";
        });

        var result = NewImporter().Parse(file);
        Assert.False(result.Rows[0].TextValues.ContainsKey("RowNumber"));
        Assert.True(result.Rows[0].IsValid); // 序号不是数据，但描述/措施是
    }

    [Fact]
    public void Parse_匹配字段统计_正确()
    {
        string file = CreateHazardWorkbook(ws => { });
        var result = NewImporter().Parse(file);
        Assert.Equal(3, result.MatchedFieldCount);
        Assert.True(result.FieldColumnCount.ContainsKey("Description"));
    }

    // ===== FindLastDataRow 回归（2026-08-14 修复）=====

    [Fact]
    public void 回归_数据行以日期开头_不得截断后续行()
    {
        // 描述以"日期"开头（旧逻辑会误判为底部说明行 → break → 后续行全部丢失）
        string file = CreateHazardWorkbook(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "日期牌脱落未更换"; ws.Cell(2, 3).Value = "更换日期牌";
            ws.Cell(3, 1).Value = 2; ws.Cell(3, 2).Value = "灭火器过期"; ws.Cell(3, 3).Value = "更换灭火器";
            ws.Cell(4, 1).Value = 3; ws.Cell(4, 2).Value = "临边防护缺失"; ws.Cell(4, 3).Value = "补齐防护";
        });

        var result = NewImporter().Parse(file);

        Assert.Equal(3, result.ValidRowCount);
        Assert.Equal("日期牌脱落未更换", result.Rows[0].TextValues["Description"]);
        Assert.Equal("临边防护缺失", result.Rows[2].TextValues["Description"]);
    }

    [Fact]
    public void 回归_数据行以审核与注开头_不得截断()
    {
        string file = CreateHazardWorkbook(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "审核资料缺失"; ws.Cell(2, 3).Value = "补齐资料";
            ws.Cell(3, 1).Value = 2; ws.Cell(3, 2).Value = "注：详见现场照片"; ws.Cell(3, 3).Value = "已处理";
            ws.Cell(4, 1).Value = 3; ws.Cell(4, 2).Value = "配电箱未上锁"; ws.Cell(4, 3).Value = "加锁";
        });

        var result = NewImporter().Parse(file);
        Assert.Equal(3, result.ValidRowCount);
    }

    [Fact]
    public void 回归_数据行后跟底部说明行_数据完整且说明行不读入()
    {
        // 行5 是底部说明行（内容在非映射列 A），应作为结构边界停止读取
        string file = CreateHazardWorkbook(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "隐患A"; ws.Cell(2, 3).Value = "措施A";
            ws.Cell(3, 1).Value = 2; ws.Cell(3, 2).Value = "隐患B"; ws.Cell(3, 3).Value = "措施B";
            ws.Cell(4, 1).Value = 3; ws.Cell(4, 2).Value = "隐患C"; ws.Cell(4, 3).Value = "措施C";
            ws.Cell(5, 1).Value = "填表人：张三"; // 非映射列内容 → 底部说明行
        });

        var result = NewImporter().Parse(file);
        Assert.Equal(3, result.ValidRowCount);
        Assert.Equal(3, result.TotalRowCount); // 底部说明行不读入
    }

    [Fact]
    public void 中间空行_跳过不截断后续数据()
    {
        string file = CreateHazardWorkbook(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "隐患A"; ws.Cell(2, 3).Value = "措施A";
            // 行3 完全空
            ws.Cell(4, 1).Value = 2; ws.Cell(4, 2).Value = "隐患B"; ws.Cell(4, 3).Value = "措施B";
            ws.Cell(5, 1).Value = 3; ws.Cell(5, 2).Value = "隐患C"; ws.Cell(5, 3).Value = "措施C";
        });

        var result = NewImporter().Parse(file);
        Assert.Equal(3, result.ValidRowCount);
    }

    // ===== 表头识别 =====

    [Fact]
    public void Parse_无有效表头_返回警告()
    {
        string file = Path.Combine(_root, "noheader.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "隐患描述"; // 仅 1 个匹配字段，不足 2 个
            ws.Cell(2, 1).Value = "内容";
            wb.SaveAs(file);
        }

        var result = NewImporter().Parse(file);
        Assert.Empty(result.Rows);
        Assert.Contains(result.Warnings, w => w.Contains("表头行"));
    }

    [Fact]
    public void Parse_标签内容排版行_不误判为表头()
    {
        // 每行只有 1 个匹配字段（"标签+内容"排版），不应被识别为表头
        string file = Path.Combine(_root, "label.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "隐患描述"; ws.Cell(1, 2).Value = "脚手架缺少防护网";
            ws.Cell(2, 1).Value = "整改措施"; ws.Cell(2, 2).Value = "加装防护网";
            wb.SaveAs(file);
        }

        var result = NewImporter().Parse(file);
        Assert.Contains(result.Warnings, w => w.Contains("表头行"));
    }

    // ===== 边界与图片 =====

    [Fact]
    public void Parse_文件不存在_返回警告()
    {
        var result = NewImporter().Parse(Path.Combine(_root, "missing.xlsx"));
        Assert.Contains(result.Warnings, w => w.Contains("文件不存在"));
    }

    [Fact]
    public void Parse_工作表序号越界_返回警告()
    {
        string file = CreateHazardWorkbook(ws => { });
        var result = NewImporter().Parse(file, sheetIndex: 9);
        Assert.Contains(result.Warnings, w => w.Contains("工作表序号"));
    }

    [Fact]
    public void Parse_照片路径文本_解析为图片路径()
    {
        // 在源文件同目录放一张真实图片，单元格文本写相对路径
        string photoPath = Path.Combine(_root, "hazard_photo.jpg");
        File.WriteAllBytes(photoPath, OnePixelPng);
        string file = Path.Combine(_root, "withphoto.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "序号";
            ws.Cell(1, 2).Value = "隐患照片";
            ws.Cell(1, 3).Value = "整改措施";
            ws.Cell(2, 1).Value = 1;
            ws.Cell(2, 2).Value = Path.GetFileName(photoPath); // 相对路径
            ws.Cell(2, 3).Value = "加装防护网";
            wb.SaveAs(file);
        }

        var result = NewImporter().Parse(file);
        Assert.True(result.Rows[0].PhotoPaths.TryGetValue("HazardPhoto", out var photos));
        Assert.Single(photos);
        Assert.Equal(photoPath, photos[0]);
    }

    [Fact]
    public void Parse_嵌入图片_提取到图片路径()
    {
        string file = Path.Combine(_root, "embedded.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "序号";
            ws.Cell(1, 2).Value = "隐患照片";
            ws.Cell(1, 3).Value = "整改措施";
            ws.Cell(2, 1).Value = 1;
            using var imgStream = new MemoryStream(OnePixelPng);
            ws.AddPicture(imgStream).MoveTo(ws.Cell(2, 2));
            ws.Cell(2, 3).Value = "加装防护网";
            wb.SaveAs(file);
        }

        var result = NewImporter().Parse(file);
        Assert.True(result.Rows[0].PhotoPaths.TryGetValue("HazardPhoto", out var photos));
        Assert.Single(photos);
        Assert.True(File.Exists(photos[0])); // 已写出临时文件
    }

    [Fact]
    public void Parse_纯图片行_也被识别为数据行()
    {
        // 嵌入图片行无文本 → 仍应作为数据行（FindLastDataRow 的 upperBound 覆盖图片锚点行）
        string file = Path.Combine(_root, "imageonly.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "序号";
            ws.Cell(1, 2).Value = "隐患照片";
            ws.Cell(1, 3).Value = "整改措施";
            ws.Cell(2, 1).Value = 1;
            using var imgStream = new MemoryStream(OnePixelPng);
            ws.AddPicture(imgStream).MoveTo(ws.Cell(2, 2));
            // 行2 无任何文本
            wb.SaveAs(file);
        }

        var result = NewImporter().Parse(file);
        Assert.Equal(1, result.TotalRowCount);
        Assert.True(result.Rows[0].PhotoPaths.ContainsKey("HazardPhoto"));
        Assert.True(result.Rows[0].IsValid); // 有图片即有效
    }
}
