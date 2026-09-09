// ExcelSmartExporter 测试：模板导出、数据写入、FindLastDataRow 同步修复回归
// 2026-08-14 回归：导出侧曾漏改「数据行优先」，模板示例数据以"日期/审核/注："开头时
// 会被误判为底部说明行而截断 → 旧示例数据删不干净残留在导出文件

using ClosedXML.Excel;
using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class ExcelSmartExporterTests : IDisposable
{
    private readonly string _root;
    private readonly MappingDictionaryService _mapping;

    public ExcelSmartExporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "ExcelExporter", Guid.NewGuid().ToString("N"));
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

    private ExcelSmartExporter NewExporter(string module = "Hazard") => new(_mapping, _root, module);

    /// <summary>生成模板：表头（序号/隐患描述/整改措施）+ 可选示例数据区 + 可选底部说明行</summary>
    private string CreateTemplate(Action<IXLWorksheet> fill)
    {
        string file = Path.Combine(_root, $"{Guid.NewGuid():N}_template.xlsx");
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

    /// <summary>简单数据提供者桩：按字典返回字段值</summary>
    private sealed class FakeProvider : IExportDataProvider
    {
        private readonly List<Dictionary<string, string>> _rows;
        public FakeProvider(params Dictionary<string, string>[] rows) => _rows = rows.ToList();

        public int RowCount => _rows.Count;
        public string GetTextValue(int rowIndex, string dataField) =>
            _rows[rowIndex].TryGetValue(dataField, out var v) ? v : "";
        public List<string> GetPhotoPaths(int rowIndex, string dataField, string attachmentsRootPath) => new();
        public string OneClickTitleSuffix => "测试统计";
        public string OneClickFileSuffix => "测试统计";
        public string[] OneClickHeaders => new[] { "序号", "隐患描述", "整改措施" };
        public string[] OneClickFieldNames => new[] { "RowNumber", "Description", "Measure" };
        public int[] OneClickColumnWidths => new[] { 6, 40, 30 };
    }

    private static Dictionary<string, string> Row(string description, string measure) => new()
    {
        ["Description"] = description,
        ["Measure"] = measure
    };

    // ===== 基础 =====

    [Fact]
    public void Export_模板不存在_返回错误()
    {
        var (success, error) = NewExporter().Export(Path.Combine(_root, "missing.xlsx"),
            Path.Combine(_root, "out.xlsx"), new FakeProvider(Row("a", "b")), "Full", "1");
        Assert.False(success);
        Assert.Contains("模板文件不存在", error);
    }

    [Fact]
    public void Export_无数据_返回错误()
    {
        string template = CreateTemplate(ws => { });
        var (success, error) = NewExporter().Export(template, Path.Combine(_root, "out.xlsx"),
            new FakeProvider(), "Full", "1");
        Assert.False(success);
        Assert.Contains("没有可导出的数据", error);
    }

    [Fact]
    public void Export_基本导出_数据正确写入()
    {
        string template = CreateTemplate(ws => { }); // 空数据区
        string output = Path.Combine(_root, "out.xlsx");

        var provider = new FakeProvider(Row("脚手架缺少防护网", "加装防护网"), Row("灭火器过期", "更换灭火器"));
        var (success, error) = NewExporter().Export(template, output, provider, "Full", "1");
        Assert.True(success, error);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet(1);
        Assert.Equal("脚手架缺少防护网", ws.Cell(2, 2).GetString());
        Assert.Equal("加装防护网", ws.Cell(2, 3).GetString());
        Assert.Equal("灭火器过期", ws.Cell(3, 2).GetString());
        Assert.Equal("更换灭火器", ws.Cell(3, 3).GetString());
        Assert.Equal(1, ws.Cell(2, 1).GetValue<int>()); // 序号自动编号
        Assert.Equal(2, ws.Cell(3, 1).GetValue<int>());
    }

    // ===== FindLastDataRow 同步修复回归 =====

    [Fact]
    public void 回归_模板示例数据以日期开头_导出后旧数据删除干净()
    {
        // 旧逻辑：示例数据"日期牌脱落未更换"被误判为底部说明行 → 数据区边界停在表头下一行
        // → 示例行不删除 → 导出后残留旧数据。修复后应删干净。
        string template = CreateTemplate(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "日期牌脱落未更换"; ws.Cell(2, 3).Value = "更换日期牌";
            ws.Cell(3, 1).Value = "填表人：张三"; // 底部说明行（非映射列）
        });
        string output = Path.Combine(_root, "out.xlsx");

        var provider = new FakeProvider(Row("配电箱未上锁", "加锁"));
        var (success, error) = NewExporter().Export(template, output, provider, "Full", "1");
        Assert.True(success, error);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet(1);
        // 旧示例数据必须删除干净
        var allText = string.Join("|", ws.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("日期牌脱落未更换", allText);
        Assert.DoesNotContain("更换日期牌", allText);
        // 新数据在表头下一行
        Assert.Equal("配电箱未上锁", ws.Cell(2, 2).GetString());
        // 底部说明行保留
        Assert.Contains("填表人：张三", allText);
    }

    [Fact]
    public void 回归_模板示例数据以审核开头_导出后旧数据删除干净()
    {
        string template = CreateTemplate(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "审核资料缺失"; ws.Cell(2, 3).Value = "补齐资料";
        });
        string output = Path.Combine(_root, "out.xlsx");

        var provider = new FakeProvider(Row("新隐患", "新措施"));
        var (success, error) = NewExporter().Export(template, output, provider, "Full", "1");
        Assert.True(success, error);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet(1);
        var allText = string.Join("|", ws.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("审核资料缺失", allText);
        Assert.Equal("新隐患", ws.Cell(2, 2).GetString());
    }

    [Fact]
    public void 回归_模板多行示例数据_全部删除仅留新数据()
    {
        string template = CreateTemplate(ws =>
        {
            ws.Cell(2, 1).Value = 1; ws.Cell(2, 2).Value = "示例隐患一"; ws.Cell(2, 3).Value = "措施一";
            ws.Cell(3, 1).Value = 2; ws.Cell(3, 2).Value = "示例隐患二"; ws.Cell(3, 3).Value = "措施二";
            ws.Cell(4, 1).Value = "填表人：李四";
        });
        string output = Path.Combine(_root, "out.xlsx");

        var provider = new FakeProvider(Row("真实隐患A", "真实措施A"));
        var (success, error) = NewExporter().Export(template, output, provider, "Full", "1");
        Assert.True(success, error);

        using var wb = new XLWorkbook(output);
        var ws = wb.Worksheet(1);
        var allText = string.Join("|", ws.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("示例隐患", allText);
        Assert.Equal("真实隐患A", ws.Cell(2, 2).GetString());
        // 底部说明行被下推但保留
        Assert.Contains("填表人：李四", allText);
    }
}
