// MappingDictionaryService 测试：别名匹配、归一化、歧义放弃、模块过滤、版本迁移
// 覆盖导入导出的表头识别基础能力（A 层导入导出链路第 0 层）

using MS_SafetyManagementSystem.Services;
using System.IO;
using System.Text.Json;

namespace MSpirit.SafetySys.Tests;

public class MappingDictionaryServiceTests : IDisposable
{
    private readonly string _root;

    public MappingDictionaryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "MappingDict", Guid.NewGuid().ToString("N"));
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

    private MappingDictionaryService NewService() => new(_root);

    [Fact]
    public void 构造_创建默认字典文件_字段映射非空()
    {
        var svc = NewService();
        Assert.True(File.Exists(Path.Combine(_root, "MappingDictionary.json")));
        Assert.NotEmpty(svc.GetFieldMappings());
    }

    [Theory]
    [InlineData("隐患描述", "Description")]
    [InlineData("问题描述", "Description")]      // 别名
    [InlineData("隐患内容", "Description")]      // 别名
    [InlineData("序号", "RowNumber")]
    [InlineData("整改措施", "Measure")]
    [InlineData("隐患位置", "Location")]
    [InlineData("整改前照片", "HazardPhoto")]    // 2026-08-14 从 RemedyPhoto 移除后的回归：仅归 HazardPhoto
    [InlineData("整改前图片", "HazardPhoto")]
    [InlineData("整改后照片", "RemedyPhoto")]
    [InlineData("姓名", "Title")]
    [InlineData("证书编号", "CertNumber")]
    [InlineData("三类人员", "ThreeCategoryPersonnelDisplay")]
    [InlineData("特种作业人员", "SpecialOperationsPersonnelDisplay")]
    [InlineData("身份证有效期", "IDCardValidPeriod")]
    public void MatchField_标准名与别名_直接匹配(string input, string expected)
    {
        var svc = NewService();
        Assert.Equal(expected, svc.MatchField(input));
    }

    [Fact]
    public void MatchField_空白字符归一化_全角空格与不间断空格()
    {
        var svc = NewService();
        Assert.Equal("Description", svc.MatchField("隐患 描述"));
        Assert.Equal("Description", svc.MatchField("　隐患描述　"));
        Assert.Equal("Description", svc.MatchField("隐患\u00A0描述")); // 不间断空格
    }

    [Fact]
    public void MatchField_分隔符回退_岗位工种匹配到岗位()
    {
        var svc = NewService();
        // "岗位/工种" 归一化后整体包含"岗位"（Position 别名），按 contains 直接命中
        Assert.Equal("Position", svc.MatchField("岗位/工种"));
        Assert.Equal("Position", svc.MatchField("岗位／工种")); // 全角斜杠
        Assert.Equal("Position", svc.MatchField("岗位、工种"));
    }

    [Fact]
    public void MatchField_多词组合_整体包含匹配优先_长别名胜出()
    {
        var svc = NewService();
        // 归一化后整体包含"责任单位"（ResponsibleUnit，别名更长）→ 直接命中，
        // 不会进入分隔符回退（该回退分支因 contains 匹配而不可达，属死逻辑，测试守护真实行为）
        Assert.Equal("ResponsibleUnit", svc.MatchField("责任单位/责任人"));
    }

    [Fact]
    public void MatchField_模块过滤_Hazard不匹配人员字段()
    {
        var svc = NewService();
        Assert.Null(svc.MatchField("姓名", "Hazard"));
        Assert.Null(svc.MatchField("证书编号", "Hazard"));
    }

    [Fact]
    public void MatchField_模块过滤_Personnel不匹配隐患字段()
    {
        var svc = NewService();
        Assert.Null(svc.MatchField("隐患描述", "Personnel"));
        Assert.Null(svc.MatchField("整改措施", "Personnel"));
    }

    [Fact]
    public void MatchField_模块为空_不过滤()
    {
        var svc = NewService();
        Assert.Equal("Title", svc.MatchField("姓名", null));
        Assert.Equal("Description", svc.MatchField("隐患描述", null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("完全无关的随机内容abc")]
    public void MatchField_无效输入_返回null(string? input)
    {
        var svc = NewService();
        Assert.Null(svc.MatchField(input!));
    }

    [Fact]
    public void 版本迁移_v1字典自动补人员字段并升级版本号()
    {
        // 手写 v1.0 字典（仅隐患字段，无人员字段），模拟老用户数据
        string json = """
        {
          "Version": "1.0",
          "FieldMappings": [
            { "DataField": "Status", "StandardName": "进度", "Aliases": ["状态", "进度"] },
            { "DataField": "RowNumber", "StandardName": "序号", "Aliases": ["序号"] },
            { "DataField": "Description", "StandardName": "隐患描述", "Aliases": ["隐患描述", "问题描述"] }
          ],
          "CommonSynonyms": {}
        }
        """;
        File.WriteAllText(Path.Combine(_root, "MappingDictionary.json"), json);

        var svc = NewService();
        // 迁移后人员字段可用
        Assert.Equal("Title", svc.MatchField("姓名"));
        Assert.Equal("CertNumber", svc.MatchField("证书编号"));
        // 原有隐患字段保留
        Assert.Equal("Description", svc.MatchField("问题描述"));

        // 版本号已升级并写回文件
        string saved = File.ReadAllText(Path.Combine(_root, "MappingDictionary.json"));
        using var doc = JsonDocument.Parse(saved);
        Assert.Equal("1.1", doc.RootElement.GetProperty("Version").GetString());
    }

    [Fact]
    public void 损坏JSON_回退默认字典_不抛异常()
    {
        File.WriteAllText(Path.Combine(_root, "MappingDictionary.json"), "{ 这不是合法 JSON !!!");
        var svc = NewService(); // 不抛异常
        Assert.NotEmpty(svc.GetFieldMappings());
        Assert.Equal("Description", svc.MatchField("隐患描述"));
    }

    [Fact]
    public void UpdateAliases_修改别名后匹配生效()
    {
        var svc = NewService();
        Assert.True(svc.UpdateAliases("Description", new List<string> { "自定义描述别名" }));
        Assert.Equal("Description", svc.MatchField("自定义描述别名"));
        Assert.Null(svc.MatchField("隐患描述")); // 原别名已被替换
    }

    [Fact]
    public void UpdateAliases_不存在的字段_返回false()
    {
        var svc = NewService();
        Assert.False(svc.UpdateAliases("NotExistField", new List<string> { "x" }));
    }
}
