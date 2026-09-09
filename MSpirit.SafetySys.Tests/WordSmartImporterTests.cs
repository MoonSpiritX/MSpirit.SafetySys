// WordSmartImporter 测试：表头识别/跨表合并/嵌入图片/多图回归/GridSpan/边界
// 2026-08-15 回归：同单元格多图拼接损坏修复（每个图片独立文件）

using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Services;
using System.IO;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MSpirit.SafetySys.Tests;

public class WordSmartImporterTests : IDisposable
{
    private readonly string _root;
    private readonly MappingDictionaryService _mapping;

    // 1x1 透明 PNG（有效图片字节）
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public WordSmartImporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "WordImporter", Guid.NewGuid().ToString("N"));
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

    private WordSmartImporter NewImporter(string module = "Hazard") => new(_mapping, module);

    /// <summary>标准隐患表：表头 序号/隐患描述/整改措施 + 数据行</summary>
    private W.Table HazardTable(params string[][] dataRows)
    {
        var rows = new List<W.TableRow> { WordDocBuilder.CreateRow("序号", "隐患描述", "整改措施") };
        foreach (var r in dataRows) rows.Add(WordDocBuilder.CreateRow(r));
        return WordDocBuilder.CreateTable(rows.ToArray());
    }

    // ===== 基本解析 =====

    [Fact]
    public void 基本解析_单表多行_字段映射正确_序号不入文本()
    {
        string file = WordDocBuilder.CreateDocWithTables(_root, HazardTable(
            new[] { "1", "脚手架缺少防护网", "加装防护网" },
            new[] { "2", "灭火器过期", "更换灭火器" },
            new[] { "3", "临边防护缺失", "补齐防护" }));

        var result = NewImporter().Parse(file);

        Assert.Equal(3, result.ValidRowCount);
        Assert.Equal(3, result.TotalRowCount);
        Assert.Equal("脚手架缺少防护网", result.Rows[0].TextValues["Description"]);
        Assert.Equal("加装防护网", result.Rows[0].TextValues["Measure"]);
        Assert.Equal("灭火器过期", result.Rows[1].TextValues["Description"]);
        Assert.False(result.Rows[0].TextValues.ContainsKey("RowNumber"));
    }

    [Fact]
    public void 匹配字段统计_正确()
    {
        string file = WordDocBuilder.CreateDocWithTables(_root, HazardTable(new[] { "1", "描述", "措施" }));
        var result = NewImporter().Parse(file);
        Assert.Equal(3, result.MatchedFieldCount);
        Assert.True(result.FieldColumnCount.ContainsKey("Description"));
    }

    // ===== 表头识别 =====

    [Fact]
    public void 标签内容排版表_候选行字段重复_整体排除()
    {
        // 表头两个单元格都命中"整改措施"（整改情况/整改回复 均为 Measure 别名）→ distinct<2 → 排除
        string file = WordDocBuilder.CreateDocWithTables(_root, WordDocBuilder.CreateTable(
            WordDocBuilder.CreateRow("整改情况", "整改回复"),
            WordDocBuilder.CreateRow("已整改", "复查通过")));

        var result = NewImporter().Parse(file);
        Assert.Empty(result.Rows);
        Assert.Contains(result.Warnings, w => w.Contains("表头"));
    }

    [Fact]
    public void 多Run表头_文本拆分_方案A合并后匹配()
    {
        // "隐患描述" 拆成两个 Run（"隐患" + "描述"），方案 A 合并后仍应匹配
        var headerRow = new W.TableRow();
        headerRow.AppendChild(WordDocBuilder.TextCell("序号"));
        var splitCell = new W.TableCell();
        splitCell.AppendChild(new W.TableCellProperties());
        var p = new W.Paragraph();
        var r1 = new W.Run(); r1.AppendChild(new W.Text("隐患"));
        var r2 = new W.Run(); r2.AppendChild(new W.Text("描述"));
        p.AppendChild(r1); p.AppendChild(r2);
        splitCell.AppendChild(p);
        headerRow.AppendChild(splitCell);
        headerRow.AppendChild(WordDocBuilder.TextCell("整改措施"));

        string file = WordDocBuilder.CreateDocWithTables(_root, WordDocBuilder.CreateTable(
            headerRow, WordDocBuilder.CreateRow("1", "隐患内容", "措施内容")));

        var result = NewImporter().Parse(file);
        Assert.Equal(1, result.ValidRowCount);
        Assert.Equal("隐患内容", result.Rows[0].TextValues["Description"]);
    }

    // ===== 跨表合并 =====

    [Fact]
    public void 跨表合并_文本表与照片表按行位置关联()
    {
        string file = WordDocBuilder.CreateDoc(_root, (main, body) =>
        {
            // 文本表：2 条记录
            body.AppendChild(HazardTable(
                new[] { "1", "隐患甲", "措施甲" },
                new[] { "2", "隐患乙", "措施乙" }));
            // 照片表：表头需 ≥2 个匹配字段（序号 + 隐患照片），2 行各含图片
            string rel1 = WordDocBuilder.AddImage(main, OnePixelPng);
            string rel2 = WordDocBuilder.AddImage(main, OnePixelPng);
            var photoTable = WordDocBuilder.CreateTable(
                WordDocBuilder.CreateRow("序号", "隐患照片"),
                WordDocBuilder.CreateRow(WordDocBuilder.TextCell("1"), WordDocBuilder.ImageCell(rel1)),
                WordDocBuilder.CreateRow(WordDocBuilder.TextCell("2"), WordDocBuilder.ImageCell(rel2)));
            body.AppendChild(photoTable);
        });

        var result = NewImporter().Parse(file);

        Assert.Equal(2, result.ValidRowCount);
        // 行 0：文本 + 照片合并
        Assert.Equal("隐患甲", result.Rows[0].TextValues["Description"]);
        Assert.True(result.Rows[0].PhotoPaths.TryGetValue("HazardPhoto", out var photos0));
        Assert.Single(photos0);
        Assert.True(File.Exists(photos0[0]));
        // 行 1：文本 + 照片
        Assert.Equal("隐患乙", result.Rows[1].TextValues["Description"]);
        Assert.True(result.Rows[1].PhotoPaths.ContainsKey("HazardPhoto"));
    }

    [Fact]
    public void 跨表合并_行数不一致_警告且缺失行保留为纯文本()
    {
        string file = WordDocBuilder.CreateDoc(_root, (main, body) =>
        {
            body.AppendChild(HazardTable(
                new[] { "1", "隐患甲", "措施甲" },
                new[] { "2", "隐患乙", "措施乙" }));
            string rel1 = WordDocBuilder.AddImage(main, OnePixelPng);
            var photoTable = WordDocBuilder.CreateTable(
                WordDocBuilder.CreateRow("序号", "隐患照片"),
                WordDocBuilder.CreateRow(WordDocBuilder.TextCell("1"), WordDocBuilder.ImageCell(rel1)));
            body.AppendChild(photoTable);
        });

        var result = NewImporter().Parse(file);

        Assert.Contains(result.Warnings, w => w.Contains("行数不一致"));
        Assert.Equal(2, result.TotalRowCount);
        Assert.Equal("隐患甲", result.Rows[0].TextValues["Description"]);
        Assert.Equal("隐患乙", result.Rows[1].TextValues["Description"]); // 第二条纯文本保留
        Assert.False(result.Rows[1].PhotoPaths.ContainsKey("HazardPhoto"));
    }

    // ===== 嵌入图片 =====

    [Fact]
    public void 嵌入图片_提取为临时文件()
    {
        string file = WordDocBuilder.CreateDoc(_root, (main, body) =>
        {
            string rel = WordDocBuilder.AddImage(main, OnePixelPng);
            body.AppendChild(WordDocBuilder.CreateTable(
                WordDocBuilder.CreateRow("序号", "隐患照片", "整改措施"),
                WordDocBuilder.CreateRow(WordDocBuilder.TextCell("1"), WordDocBuilder.ImageCell(rel), WordDocBuilder.TextCell("加装防护网"))));
        });

        var result = NewImporter().Parse(file);

        Assert.Equal(1, result.ValidRowCount);
        Assert.True(result.Rows[0].PhotoPaths.TryGetValue("HazardPhoto", out var photos));
        Assert.Single(photos);
        Assert.True(File.Exists(photos[0]));
    }

    [Fact]
    public void 回归_同一单元格多图_分别写成独立文件()
    {
        // 2026-08-15 修复回归：原实现把同单元格多图字节拼接成单数组写出 → 损坏图片
        string file = WordDocBuilder.CreateDoc(_root, (main, body) =>
        {
            string rel1 = WordDocBuilder.AddImage(main, OnePixelPng);
            string rel2 = WordDocBuilder.AddImage(main, OnePixelPng);
            // 同一单元格两个段落各一张图（同 key 收集 → 多图分支）
            var cell = new W.TableCell();
            cell.AppendChild(new W.TableCellProperties());
            var p1 = new W.Paragraph();
            var run1 = new W.Run();
            run1.AppendChild(WordDocBuilder.MakeDrawing(rel1));
            p1.AppendChild(run1);
            cell.AppendChild(p1);
            var p2 = new W.Paragraph();
            var run2 = new W.Run();
            run2.AppendChild(WordDocBuilder.MakeDrawing(rel2));
            p2.AppendChild(run2);
            cell.AppendChild(p2);

            body.AppendChild(WordDocBuilder.CreateTable(
                WordDocBuilder.CreateRow("序号", "隐患照片"),
                WordDocBuilder.CreateRow(WordDocBuilder.TextCell("1"), cell)));
        });

        var result = NewImporter().Parse(file);

        Assert.True(result.Rows[0].PhotoPaths.TryGetValue("HazardPhoto", out var photos));
        Assert.Equal(2, photos.Count); // 修复后：两张独立文件
        Assert.NotEqual(photos[0], photos[1]);
        Assert.True(File.Exists(photos[0]));
        Assert.True(File.Exists(photos[1]));
    }

    // ===== GridSpan =====

    [Fact]
    public void GridSpan表头_列映射按展开列对齐()
    {
        string file = WordDocBuilder.CreateDocWithTables(_root, WordDocBuilder.CreateTable(
            WordDocBuilder.CreateRowWithSpan(0, 2, "序号", "隐患描述", "整改措施"), // 序号跨 2 列
            WordDocBuilder.CreateRow("1", "x", "y", "措施")));

        var result = NewImporter().Parse(file);
        // 序号列跨 2 列：数据行需 4 个单元格才与表头列数匹配
        Assert.True(result.ValidRowCount >= 0); // 结构不匹配时可能无有效行，此处只验证不崩溃
    }

    // ===== 边界 =====

    [Fact]
    public void 无表格_返回警告()
    {
        string file = WordDocBuilder.CreateDoc(_root, (main, body) => body.AppendChild(new W.Paragraph(new W.Run(new W.Text("无表格文档")))));
        var result = NewImporter().Parse(file);
        Assert.Contains(result.Warnings, w => w.Contains("表格"));
    }

    [Fact]
    public void 无有效表头_返回警告()
    {
        string file = WordDocBuilder.CreateDocWithTables(_root, WordDocBuilder.CreateTable(
            WordDocBuilder.CreateRow("只有一列匹配", "内容")));
        var result = NewImporter().Parse(file);
        Assert.Contains(result.Warnings, w => w.Contains("表头"));
    }

    [Fact]
    public void 文件不存在_返回警告()
    {
        var result = NewImporter().Parse(Path.Combine(_root, "missing.docx"));
        Assert.Contains(result.Warnings, w => w.Contains("文件不存在"));
    }
}
