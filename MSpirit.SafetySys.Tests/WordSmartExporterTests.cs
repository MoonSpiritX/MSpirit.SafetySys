// WordSmartExporter 测试：模板导出（旧数据删除/多表/底部行保留/mode 过滤/图片嵌入/无示例数据模板）

using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Services;
using System.IO;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MSpirit.SafetySys.Tests;

public class WordSmartExporterTests : IDisposable
{
    private readonly string _root;
    private readonly MappingDictionaryService _mapping;

    // 1x1 透明 PNG（有效图片字节）
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public WordSmartExporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "WordExporter", Guid.NewGuid().ToString("N"));
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

    private WordSmartExporter NewExporter(string module = "Hazard") => new(_mapping, _root, module);

    /// <summary>标准隐患表模板：表头 序号/隐患描述/整改措施</summary>
    private static W.Table HazardTable(params W.TableRow[] dataRows)
    {
        var rows = new List<W.TableRow> { WordDocBuilder.CreateRow("序号", "隐患描述", "整改措施") };
        rows.AddRange(dataRows);
        return WordDocBuilder.CreateTable(rows.ToArray());
    }

    private sealed class FakeProvider : IExportDataProvider
    {
        private readonly List<Dictionary<string, string>> _rows;
        private readonly List<Dictionary<string, List<string>>> _photos;

        public FakeProvider(List<Dictionary<string, string>> rows, List<Dictionary<string, List<string>>>? photos = null)
        {
            _rows = rows;
            _photos = photos ?? rows.Select(_ => new Dictionary<string, List<string>>()).ToList();
        }

        public int RowCount => _rows.Count;
        public string GetTextValue(int rowIndex, string dataField) =>
            _rows[rowIndex].TryGetValue(dataField, out var v) ? v : "";
        public List<string> GetPhotoPaths(int rowIndex, string dataField, string attachmentsRootPath) =>
            _photos[rowIndex].TryGetValue(dataField, out var v) ? v : new List<string>();
        public string OneClickTitleSuffix => "测试";
        public string OneClickFileSuffix => "测试";
        public string[] OneClickHeaders => new[] { "序号", "隐患描述", "整改措施" };
        public string[] OneClickFieldNames => new[] { "RowNumber", "Description", "Measure" };
        public int[] OneClickColumnWidths => new[] { 6, 30, 20 };
    }

    private static Dictionary<string, string> Row(string description, string measure) => new()
    {
        ["Description"] = description,
        ["Measure"] = measure
    };

    // ===== 基础 =====

    [Fact]
    public void 模板不存在_返回错误()
    {
        var (success, error) = NewExporter().Export(Path.Combine(_root, "missing.docx"),
            Path.Combine(_root, "out.docx"), new FakeProvider(new List<Dictionary<string, string>> { Row("a", "b") }), "Full");
        Assert.False(success);
        Assert.Contains("模板文件不存在", error);
    }

    [Fact]
    public void 无数据_返回错误()
    {
        string template = WordDocBuilder.CreateDocWithTables(_root, HazardTable());
        var (success, error) = NewExporter().Export(template, Path.Combine(_root, "out.docx"),
            new FakeProvider(new List<Dictionary<string, string>>()), "Full");
        Assert.False(success);
        Assert.Contains("没有可导出的数据", error);
    }

    // ===== 基本导出 =====

    [Fact]
    public void 基本导出_新数据写入_旧示例数据删除()
    {
        string template = WordDocBuilder.CreateDocWithTables(_root, HazardTable(
            WordDocBuilder.CreateRow("1", "旧示例隐患", "旧措施")));
        string output = Path.Combine(_root, "out.docx");

        var provider = new FakeProvider(new List<Dictionary<string, string>> { Row("脚手架缺少防护网", "加装防护网") });
        var (success, error) = NewExporter().Export(template, output, provider, "Full");
        Assert.True(success, error);

        var tables = WordDocBuilder.ReadTableTexts(output);
        var rows = tables[0];
        Assert.Equal(2, rows.Count); // 表头 + 1 数据行
        Assert.Equal("序号|隐患描述|整改措施", rows[0]);
        Assert.Contains("脚手架缺少防护网", rows[1]);
        Assert.Contains("加装防护网", rows[1]);
        Assert.DoesNotContain("旧示例隐患", rows[1]);
    }

    [Fact]
    public void 多表模板_每表独立处理()
    {
        string template = WordDocBuilder.CreateDocWithTables(_root,
            HazardTable(WordDocBuilder.CreateRow("1", "表一旧数据", "旧措施")),
            HazardTable(WordDocBuilder.CreateRow("1", "表二旧数据", "旧措施")));
        string output = Path.Combine(_root, "out.docx");

        var provider = new FakeProvider(new List<Dictionary<string, string>> { Row("新隐患甲", "措施甲") });
        var (success, error) = NewExporter().Export(template, output, provider, "Full");
        Assert.True(success, error);

        var tables = WordDocBuilder.ReadTableTexts(output);
        Assert.Equal(2, tables.Count);
        Assert.All(tables, t =>
        {
            Assert.Equal(2, t.Count);
            Assert.Contains("新隐患甲", t[1]);
            Assert.DoesNotContain("旧数据", string.Join("", t));
        });
    }

    [Fact]
    public void 底部说明行_列数不同_保留不被删除()
    {
        string template = WordDocBuilder.CreateDocWithTables(_root, HazardTable(
            WordDocBuilder.CreateRow("1", "示例隐患", "措施"),
            WordDocBuilder.CreateRow("填表人：张三"))); // 1 列 ≠ 3 列 → 结构边界
        string output = Path.Combine(_root, "out.docx");

        var provider = new FakeProvider(new List<Dictionary<string, string>> { Row("真实隐患", "真实措施") });
        var (success, error) = NewExporter().Export(template, output, provider, "Full");
        Assert.True(success, error);

        var rows = WordDocBuilder.ReadTableTexts(output)[0];
        Assert.Equal(3, rows.Count); // 表头 + 新数据 + 底部行
        Assert.Contains("填表人：张三", rows[2]);
        Assert.Contains("真实隐患", rows[1]);
    }

    // ===== mode 过滤 =====

    [Fact]
    public void NoImage模式_文本写入_不嵌入图片()
    {
        string template = WordDocBuilder.CreateDocWithTables(_root, WordDocBuilder.CreateTable(
            WordDocBuilder.CreateRow("序号", "隐患描述", "隐患照片", "整改措施"),
            WordDocBuilder.CreateRow("1", "占位描述", "占位图片文本", "占位措施")));
        string output = Path.Combine(_root, "out.docx");

        var provider = new FakeProvider(new List<Dictionary<string, string>> { Row("文本隐患", "文本措施") });
        var (success, error) = NewExporter().Export(template, output, provider, "NoImage");
        Assert.True(success, error);

        var rows = WordDocBuilder.ReadTableTexts(output)[0];
        Assert.Contains("文本隐患", rows[1]);
        Assert.Contains("文本措施", rows[1]);
        // NoImage 模式不嵌入任何图片
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(output, false);
        Assert.Empty(doc.MainDocumentPart!.ImageParts);
    }

    [Fact]
    public void 仅表头无示例数据_当前行为_不插入数据行()
    {
        // 守护当前行为：模板必须含示例数据行才会处理（ProcessTable 仅在有结构匹配的数据行时
        // 调用 ProcessTableBlock；CreateDataRowFromHeader 分支实际不可达——见 ISSUES 死代码观察）。
        // 注意：这是现状守护，非推荐行为；若未来支持"空模板"，此用例需同步更新。
        string template = WordDocBuilder.CreateDocWithTables(_root, HazardTable());
        string output = Path.Combine(_root, "out.docx");

        var provider = new FakeProvider(new List<Dictionary<string, string>> { Row("首条隐患", "首条措施") });
        var (success, error) = NewExporter().Export(template, output, provider, "Full");
        Assert.True(success, error); // 导出本身不报错

        var rows = WordDocBuilder.ReadTableTexts(output)[0];
        Assert.Single(rows); // 仅表头，无数据行（当前行为）
    }

    // ===== 图片导出 =====

    [Fact]
    public void 图片导出_嵌入图片关系()
    {
        // 真实图片文件
        string photoPath = Path.Combine(_root, "photo.png");
        File.WriteAllBytes(photoPath, OnePixelPng);

        string template = WordDocBuilder.CreateDocWithTables(_root, WordDocBuilder.CreateTable(
            WordDocBuilder.CreateRow("序号", "隐患照片", "整改措施"),
            WordDocBuilder.CreateRow("1", "", "")));
        string output = Path.Combine(_root, "out.docx");

        var provider = new FakeProvider(
            new List<Dictionary<string, string>> { Row("有图隐患", "措施") },
            new List<Dictionary<string, List<string>>> { new() { ["HazardPhoto"] = new List<string> { photoPath } } });

        var (success, error) = NewExporter().Export(template, output, provider, "Full");
        Assert.True(success, error);

        // 导出文档包含图片关系
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(output, false);
        Assert.NotEmpty(doc.MainDocumentPart!.ImageParts);
    }

    [Fact]
    public void 图片导出_图片文件不存在_跳过不失败()
    {
        string template = WordDocBuilder.CreateDocWithTables(_root, WordDocBuilder.CreateTable(
            WordDocBuilder.CreateRow("序号", "隐患照片", "整改措施"),
            WordDocBuilder.CreateRow("1", "", "")));
        string output = Path.Combine(_root, "out.docx");

        var provider = new FakeProvider(
            new List<Dictionary<string, string>> { Row("无图隐患", "措施") },
            new List<Dictionary<string, List<string>>> { new() { ["HazardPhoto"] = new List<string> { @"Z:\不存在\missing.jpg" } } });

        var (success, error) = NewExporter().Export(template, output, provider, "Full");
        Assert.True(success, error); // 图片缺失不导致整体失败
    }
}
