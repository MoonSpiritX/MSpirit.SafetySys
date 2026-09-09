// ImportManager 测试：Validate 行级校验、BuildPreviewMessage 预览文案、ParseFile 分派、Import 委托
// A 层导入导出链路的编排逻辑（纯逻辑，无 I/O 依赖）

using ClosedXML.Excel;
using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class ImportManagerTests : IDisposable
{
    private readonly string _root;

    public ImportManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "ImportManager", Guid.NewGuid().ToString("N"));
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

    private ImportManager NewManager(string module = "Hazard") => new(new MappingDictionaryService(_root), module);

    // ===== Validate =====

    [Fact]
    public void Validate_空结果_警告无可导入数据()
    {
        var manager = NewManager();
        var result = new ImportParseResult();
        manager.Validate(result, false);
        Assert.Contains(result.Warnings, w => w.Contains("没有可导入的数据行"));
    }

    [Fact]
    public void Validate_行无文本无图片_标为无效()
    {
        var manager = NewManager();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData { RowIndex = 0 }); // 无任何值
        manager.Validate(result, false);
        Assert.Contains(0, result.InvalidRowIndices);
        Assert.Equal(0, result.ValidRowCount);
    }

    [Fact]
    public void Validate_autoAssign缺日期字段_标为无效()
    {
        var manager = NewManager();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            RowIndex = 0,
            TextValues = new Dictionary<string, string> { ["Description"] = "只有描述没有日期" }
        });
        manager.Validate(result, true); // autoAssignByDate = true
        Assert.Contains(0, result.InvalidRowIndices);
        Assert.True(result.Rows[0].ValidationMessage!.Contains("日期"), $"校验消息应包含'日期': {result.Rows[0].ValidationMessage}");
    }

    [Fact]
    public void Validate_autoAssign日期无法解析_标为无效并加警告()
    {
        var manager = NewManager();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            RowIndex = 0,
            TextValues = new Dictionary<string, string> { ["RecordDate"] = "不是日期" }
        });
        manager.Validate(result, true);
        Assert.Contains(0, result.InvalidRowIndices);
        Assert.Contains(result.Warnings, w => w.Contains("无法解析"));
    }

    [Fact]
    public void Validate_全部无效_警告所有行均无效()
    {
        var manager = NewManager();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData { RowIndex = 0 });
        result.Rows.Add(new ImportRowData { RowIndex = 1 });
        manager.Validate(result, false);
        Assert.Contains(result.Warnings, w => w.Contains("所有行均无效"));
    }

    [Fact]
    public void Validate_有效行_保持不变()
    {
        var manager = NewManager();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            RowIndex = 0,
            TextValues = new Dictionary<string, string> { ["Description"] = "有效隐患" }
        });
        manager.Validate(result, false);
        Assert.Empty(result.InvalidRowIndices);
        Assert.Equal(1, result.ValidRowCount);
    }

    [Fact]
    public void Validate_autoAssign有有效日期_保持有效()
    {
        var manager = NewManager();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            RowIndex = 0,
            TextValues = new Dictionary<string, string> { ["RecordDate"] = "2026-08-15", ["Description"] = "有效隐患" }
        });
        manager.Validate(result, true);
        Assert.Empty(result.InvalidRowIndices);
    }

    // ===== BuildPreviewMessage =====

    [Fact]
    public void BuildPreviewMessage_包含源文件字段数与确认文案()
    {
        var manager = NewManager();
        var result = new ImportParseResult
        {
            SourceFilePath = @"C:\tmp\隐患台账.xlsx",
            SourceFileType = "Excel 工作簿"
        };
        result.Rows.Add(new ImportRowData { RowIndex = 0, TextValues = new Dictionary<string, string> { ["Description"] = "x" } });
        result.FieldColumnCount["Description"] = 1;
        result.FieldColumnCount["Measure"] = 1;

        string msg = manager.BuildPreviewMessage(result, false, "2026年08月 / 08月15日");

        Assert.Contains("隐患台账.xlsx", msg);
        Assert.Contains("识别到 2 个字段", msg);
        Assert.Contains("有效数据：1 条", msg);
        Assert.Contains("目标位置：2026年08月 / 08月15日", msg);
        Assert.Contains("确认导入 1 条记录？", msg);
    }

    [Fact]
    public void BuildPreviewMessage_autoAssign模式_显示按日期分配()
    {
        var manager = NewManager();
        var result = new ImportParseResult { SourceFilePath = "a.xlsx" };
        result.Rows.Add(new ImportRowData { RowIndex = 0, TextValues = new Dictionary<string, string> { ["Description"] = "x" } });

        string msg = manager.BuildPreviewMessage(result, true, null);
        Assert.Contains("分配方式：按文件中日期自动分配分组", msg);
    }

    // ===== ParseFile 分派 =====

    [Fact]
    public void ParseFile_不支持的扩展名_返回警告()
    {
        var manager = NewManager();
        var result = manager.ParseFile(@"C:\tmp\data.csv");
        Assert.Empty(result.Rows);
        Assert.Contains(result.Warnings, w => w.Contains("不支持的文件格式"));
    }

    [Fact]
    public void ParseFile_xlsx_分派到Excel解析器()
    {
        // 生成一个含表头+1 数据行的 xlsx
        string file = Path.Combine(_root, "hazard.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "序号";
            ws.Cell(1, 2).Value = "隐患描述";
            ws.Cell(1, 3).Value = "整改措施";
            ws.Cell(2, 1).Value = 1;
            ws.Cell(2, 2).Value = "脚手架缺少防护网";
            ws.Cell(2, 3).Value = "加装防护网";
            wb.SaveAs(file);
        }

        var manager = NewManager("Hazard");
        var result = manager.ParseFile(file);
        Assert.Equal(1, result.ValidRowCount);
        Assert.Equal("脚手架缺少防护网", result.Rows[0].TextValues["Description"]);
    }

    // ===== Import 委托 =====

    [Fact]
    public void Import_委托给writer_返回writer结果()
    {
        var manager = NewManager();
        var result = new ImportParseResult();
        var writer = new FakeWriter();
        using var tp = new TestProject();

        var (success, failed, errors) = manager.Import(result, writer, tp.Service, "2026-08", "group1", true);

        Assert.Equal(2, success);
        Assert.Equal(1, failed);
        Assert.Single(errors);
        // 参数原样透传
        Assert.Same(result, writer.ReceivedResult);
        Assert.Same(tp.Service, writer.ReceivedDataService);
        Assert.Equal("2026-08", writer.ReceivedYearMonth);
        Assert.Equal("group1", writer.ReceivedGroupId);
        Assert.True(writer.ReceivedAutoAssign);
    }

    private sealed class FakeWriter : IDataWriter
    {
        public ImportParseResult? ReceivedResult;
        public ProjectDataService? ReceivedDataService;
        public string ReceivedYearMonth = "";
        public string ReceivedGroupId = "";
        public bool ReceivedAutoAssign;

        public (int success, int failed, List<string> errors) Import(
            ImportParseResult result,
            ProjectDataService dataService,
            string targetYearMonth,
            string targetDateGroupId,
            bool autoAssignByDate)
        {
            ReceivedResult = result;
            ReceivedDataService = dataService;
            ReceivedYearMonth = targetYearMonth;
            ReceivedGroupId = targetDateGroupId;
            ReceivedAutoAssign = autoAssignByDate;
            return (2, 1, new List<string> { "测试错误" });
        }
    }
}
