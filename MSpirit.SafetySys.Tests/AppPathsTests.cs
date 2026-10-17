// AppPaths 测试：应用级固定路径（2026-10-15 MSIX 适配）
// 守护要点：程序目录/日志/兜底数据根都不能落在 %LOCALAPPDATA% 下，
// 否则 MSIX 打包后会被虚拟化重定向到包私有目录，卸载即清空。

using MS_SafetyManagementSystem.Services;
using System;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class AppPathsTests
{
    private static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void 程序目录_位于用户目录下()
    {
        Assert.Equal(Path.Combine(UserProfile, "MSpirit.SafetySys"), AppPaths.AppFolder);
    }

    [Fact]
    public void 程序目录_不在AppData下_规避MSIX虚拟化()
    {
        // MSIX 只虚拟化 AppData 系列 KnownFolder；用户目录不受影响，卸载后内容保留
        Assert.StartsWith(UserProfile, AppPaths.AppFolder);
        Assert.DoesNotContain(LocalAppData, AppPaths.AppFolder, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 日志路径_位于程序目录的logs子目录()
    {
        Assert.Equal(Path.Combine(AppPaths.AppFolder, "logs", "debug.log"), AppPaths.LogFilePath);
        Assert.DoesNotContain(LocalAppData, AppPaths.LogFilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 记录文件_位于程序目录内()
    {
        Assert.Equal(Path.Combine(AppPaths.AppFolder, "DataRoot.txt"), AppPaths.RecordFilePath);
        Assert.Equal(AppPaths.RecordFilePath, DataRootLocator.RecordFilePath); // 定位器复用同一位置
    }

    [Fact]
    public void 旧记录文件_位于AppData下_仅作回落()
    {
        Assert.Equal(Path.Combine(LocalAppData, "MSpirit.SafetySys", "DataRoot.txt"),
            AppPaths.LegacyRecordFilePath);
        Assert.NotEqual(AppPaths.RecordFilePath, AppPaths.LegacyRecordFilePath);
    }

    [Fact]
    public void 兜底数据根_位于用户目录下且沿用数据根名()
    {
        Assert.Equal(Path.Combine(UserProfile, DataPathService.DataRootFolderName),
            AppPaths.FallbackDataRootPath);
        Assert.DoesNotContain(LocalAppData, AppPaths.FallbackDataRootPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 旧兜底数据根_位于AppData下_仅作浅扫目标()
    {
        Assert.Equal(Path.Combine(LocalAppData, DataPathService.DataRootFolderName),
            AppPaths.LegacyFallbackDataRootPath);
        Assert.NotEqual(AppPaths.FallbackDataRootPath, AppPaths.LegacyFallbackDataRootPath);
    }

    [Fact]
    public void 默认数据根兜底_使用用户目录而非AppData()
    {
        // 无盘可注入的兜底场景：只需确认兜底表达式与 AppPaths 一致（PickDataDrive 由 DataPathServiceTests 覆盖）
        string fallback = AppPaths.FallbackDataRootPath;
        Assert.EndsWith(DataPathService.DataRootFolderName, fallback);
        Assert.StartsWith(UserProfile, fallback);
    }
}
