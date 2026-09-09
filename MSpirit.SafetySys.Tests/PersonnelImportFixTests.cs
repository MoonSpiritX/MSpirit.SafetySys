// 人员导入「问题5」修复测试：有效期区间解析 + PersonnelDataWriter 5 个字段落库
// 覆盖：TryParseValidPeriod 常见格式/失败兜底、Writer 导入后 IDCardStart/End、
// CertDocNumber、CertStart/End、ThreeCategoryPersonnel、SpecialOperationsPersonnel 落库

using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Models;

namespace MSpirit.SafetySys.Tests;

public class PersonnelImportFixTests
{
    [Theory]
    [InlineData("2021-01-01 至 2041-01-01", "2021-01-01", "2041-01-01")]
    [InlineData("20210101-20410101", "2021-01-01", "2041-01-01")]
    [InlineData("2021.01.01-2041.01.01", "2021-01-01", "2041-01-01")]
    [InlineData("2021/01/01 至 2041/01/01", "2021-01-01", "2041-01-01")]
    [InlineData("2021-01-01 至 长期", "2021-01-01", "长期")]
    [InlineData("2021年1月1日 至 2041年1月1日", "2021-01-01", "2041-01-01")]
    public void TryParseValidPeriod_常见格式_解析正确(string input, string expStart, string expEnd)
    {
        Assert.True(PersonnelHelper.TryParseValidPeriod(input, out var s, out var e));
        Assert.Equal(expStart, s);
        Assert.Equal(expEnd, e);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("无效文本")]
    [InlineData("2021-01-01")]            // 单段
    [InlineData("2021-01-01-2041-01-01")]  // 全连字符无法可靠切分（避免日期内连字符误拆）
    public void TryParseValidPeriod_无法解析_返回false(string input)
    {
        Assert.False(PersonnelHelper.TryParseValidPeriod(input, out _, out _));
    }

    [Fact]
    public void Writer导入_五个字段全部落库()
    {
        using var tp = new TestProject();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            IsValid = true,
            TextValues = new Dictionary<string, string>
            {
                ["Title"] = "章北海",
                ["Gender"] = "男",
                ["Unit"] = "星舰地球",
                ["Department"] = "自然选择号",
                ["IDCardValidPeriod"] = "2021-01-01 至 2041-01-01",
                ["CertName"] = "建筑施工特种作业操作资格证",
                ["CertNumber"] = "京A-TZ2026-0001",
                ["CertValidPeriod"] = "2025-01-01 至 2026-12-31",
                ["ThreeCategoryPersonnelDisplay"] = "是",
                ["SpecialOperationsPersonnelDisplay"] = "否",
            }
        });

        var writer = new PersonnelDataWriter();
        var stats = writer.Import(result, tp.Service, "", "", autoAssignByDate: true);

        Assert.Equal(1, stats.success);
        Assert.Empty(stats.errors);

        var loaded = Assert.Single(tp.Service.GetAllPersonnelRecords());
        Assert.Equal("章北海", loaded.Title);
        Assert.Equal("2021-01-01", loaded.IDCardStart);
        Assert.Equal("2041-01-01", loaded.IDCardEnd);
        Assert.Equal("京A-TZ2026-0001", loaded.CertDocNumber);
        Assert.Equal("2025-01-01", loaded.CertStart);
        Assert.Equal("2026-12-31", loaded.CertEnd);
        Assert.True(loaded.ThreeCategoryPersonnel);
        Assert.False(loaded.SpecialOperationsPersonnel);
        // 证书槽位（卡片 UI 显示来源）
        Assert.Equal("建筑施工特种作业操作资格证", loaded.Cert1Name);
        Assert.Equal("京A-TZ2026-0001", loaded.Cert1DocNumber);
        Assert.Equal("2025-01-01", loaded.Cert1Start);
        Assert.Equal("2026-12-31", loaded.Cert1End);
    }

    [Fact]
    public void Writer导入_有效期无法解析_原样保存起始日期()
    {
        using var tp = new TestProject();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            IsValid = true,
            TextValues = new Dictionary<string, string>
            {
                ["Title"] = "叶文洁",
                ["Unit"] = "红岸基地",
                ["IDCardValidPeriod"] = "随便写的有效期",
            }
        });

        var writer = new PersonnelDataWriter();
        var stats = writer.Import(result, tp.Service, "", "", autoAssignByDate: true);

        Assert.Equal(1, stats.success);
        Assert.Empty(stats.errors);

        var loaded = Assert.Single(tp.Service.GetAllPersonnelRecords());
        Assert.Equal("随便写的有效期", loaded.IDCardStart);
        Assert.Equal("", loaded.IDCardEnd);
    }

    [Fact]
    public void Writer导入_行内已有起止字段_优先使用()
    {
        using var tp = new TestProject();
        var result = new ImportParseResult();
        result.Rows.Add(new ImportRowData
        {
            IsValid = true,
            TextValues = new Dictionary<string, string>
            {
                ["Title"] = "程心",
                ["Unit"] = "行星防御理事会",
                ["IDCardStart"] = "2022-06-01",
                ["IDCardEnd"] = "2042-06-01",
                ["IDCardValidPeriod"] = "2021-01-01 至 2041-01-01",
            }
        });

        var writer = new PersonnelDataWriter();
        var stats = writer.Import(result, tp.Service, "", "", autoAssignByDate: true);

        Assert.Equal(1, stats.success);
        var loaded = Assert.Single(tp.Service.GetAllPersonnelRecords());
        Assert.Equal("2022-06-01", loaded.IDCardStart);
        Assert.Equal("2042-06-01", loaded.IDCardEnd);
    }
}
