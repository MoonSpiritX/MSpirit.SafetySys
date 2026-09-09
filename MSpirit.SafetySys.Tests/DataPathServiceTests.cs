// DataPathService 测试：选盘算法（PickDataDrive）与显式路径构造
// 2026-08-15 算法改造：由"仅认 D 盘"改为"第一个可用的非系统固定盘"，本测试守护该规则

using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class DataPathServiceTests
{
    // ===== PickDataDrive 选盘算法（纯逻辑） =====

    [Fact]
    public void PickDataDrive_多盘_选字母序第一个非系统盘()
    {
        // C(系统) D E → 选 D
        Assert.Equal(@"D:\", DataPathService.PickDataDrive(new[] { @"C:\", @"D:\", @"E:\" }, @"C:\"));
    }

    [Fact]
    public void PickDataDrive_系统盘不是C_排除对应盘符()
    {
        // 系统盘是 D（罕见但可能），候选 C D E → 排除 D 后选 C
        Assert.Equal(@"C:\", DataPathService.PickDataDrive(new[] { @"C:\", @"D:\", @"E:\" }, @"D:\"));
    }

    [Fact]
    public void PickDataDrive_只有系统盘_返回null()
    {
        Assert.Null(DataPathService.PickDataDrive(new[] { @"C:\" }, @"C:\"));
    }

    [Fact]
    public void PickDataDrive_空候选_返回null()
    {
        Assert.Null(DataPathService.PickDataDrive(Array.Empty<string>(), @"C:\"));
        Assert.Null(DataPathService.PickDataDrive(null!, @"C:\"));
    }

    [Fact]
    public void PickDataDrive_大小写不敏感_排除系统盘()
    {
        // 候选用小写 c:\，系统盘大写 C:\ → 仍视为同一盘排除
        Assert.Equal(@"D:\", DataPathService.PickDataDrive(new[] { @"c:\", @"D:\" }, @"C:\"));
    }

    [Fact]
    public void PickDataDrive_候选含空项_过滤后选择()
    {
        Assert.Equal(@"E:\", DataPathService.PickDataDrive(new[] { "", "   ", @"C:\", @"E:\" }, @"C:\"));
    }

    [Fact]
    public void PickDataDrive_系统盘不在候选_按字母序选第一个()
    {
        // 候选只有 D E，系统盘 C 不在其中 → 直接按字母序选 D
        Assert.Equal(@"D:\", DataPathService.PickDataDrive(new[] { @"E:\", @"D:\" }, @"C:\"));
    }

    [Fact]
    public void PickDataDrive_乱序候选_结果与顺序无关()
    {
        // 乱序输入 → 仍按字母序选第一个非系统盘
        Assert.Equal(@"D:\", DataPathService.PickDataDrive(new[] { @"E:\", @"C:\", @"D:\" }, @"C:\"));
        Assert.Equal(@"D:\", DataPathService.PickDataDrive(new[] { @"D:\", @"E:\", @"C:\" }, @"C:\"));
    }

    // ===== 显式路径构造 =====

    [Fact]
    public void 显式构造_RootPath为传入值且创建子目录()
    {
        string root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "DataPath", Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new DataPathService(root);
            Assert.Equal(root, svc.RootPath);
            Assert.True(Directory.Exists(Path.Combine(root, "ProjectsDate")));
            Assert.True(Directory.Exists(Path.Combine(root, "Temp")));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void 显式构造_null路径_抛异常()
    {
        Assert.Throws<ArgumentNullException>(() => new DataPathService(null!));
    }

    [Fact]
    public void 默认构造_RootPath以数据根目录名结尾()
    {
        // 默认构造依赖真实盘符环境，不断言具体盘符，只断言结构正确且目录已创建
        var svc = new DataPathService();
        Assert.EndsWith(DataPathService.DataRootFolderName, svc.RootPath);
        Assert.True(Directory.Exists(Path.Combine(svc.RootPath, "ProjectsDate")));
    }
}
