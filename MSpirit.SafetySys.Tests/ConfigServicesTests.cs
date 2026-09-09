// C 层：配置服务测试（FavoriteLinksService / CommonHazardTypeService / SettingFileService / SettingsService）
// 均为数据根下 JSON/数据库配置读写，模式相同：默认值 → 修改 → 持久化 → 损坏回退

using MS_SafetyManagementSystem.Services;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class ConfigServicesTests : IDisposable
{
    private readonly string _root;

    public ConfigServicesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "ConfigSvcs", Guid.NewGuid().ToString("N"));
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

    // ===== FavoriteLinksService =====

    [Fact]
    public void 收藏链接_默认分类_含政府信息官网()
    {
        var svc = new FavoriteLinksService(_root);
        Assert.NotEmpty(svc.GetAllCategories());
        Assert.Contains(svc.GetAllCategories(), c => c.Name == "政府信息官网");
    }

    [Fact]
    public void 收藏链接_增删改与排序_持久化()
    {
        var svc = new FavoriteLinksService(_root);
        var cat = svc.GetAllCategories().First(c => c.Name == "政府信息官网");

        svc.AddLink(cat.Id, "测试网址", "https://example.com");
        var added = svc.GetLinksByCategoryId(cat.Id).Single(l => l.Name == "测试网址");
        Assert.Equal("https://example.com", added.Url);

        // 排序移动
        Assert.True(svc.MoveLinkUp(cat.Id, added.Id));
        Assert.True(svc.MoveToTop(cat.Id, added.Id));

        // 更新
        added.Name = "改名网址";
        Assert.True(svc.UpdateLink(added));

        // 删除
        Assert.True(svc.DeleteLink(added.Id));
        Assert.DoesNotContain(svc.GetLinksByCategoryId(cat.Id), l => l.Id == added.Id);
    }

    [Fact]
    public void 收藏链接_新实例_读取已保存数据()
    {
        var svc = new FavoriteLinksService(_root);
        var cat = svc.GetAllCategories().First();
        svc.AddCategory("自定义分类");
        var newCat = svc.GetAllCategories().Single(c => c.Name == "自定义分类");
        svc.AddLink(newCat.Id, "持久链接", "https://persist.example.com");

        // 重新实例化（模拟重启）
        var fresh = new FavoriteLinksService(_root);
        var persisted = fresh.GetLinksByCategoryId(
            fresh.GetAllCategories().Single(c => c.Name == "自定义分类").Id);
        Assert.Contains(persisted, l => l.Name == "持久链接");
    }

    [Fact]
    public void 收藏链接_损坏JSON_回退默认()
    {
        File.WriteAllText(Path.Combine(_root, "FavoriteLinks.json"), "{ 非法 !!!");
        var svc = new FavoriteLinksService(_root); // 不抛异常
        Assert.NotEmpty(svc.GetAllCategories());
    }

    // ===== CommonHazardTypeService =====

    [Fact]
    public void 常用隐患类型_全量类型与名称查询()
    {
        var svc = new CommonHazardTypeService(_root);
        Assert.NotEmpty(svc.GetAllTypes());
        Assert.Equal("物体打击", svc.GetNameByCode("01"));
    }

    [Fact]
    public void 常用隐患类型_默认常用代码_与持久化()
    {
        var svc = new CommonHazardTypeService(_root);
        Assert.NotEmpty(svc.GetCommonCodes());
        Assert.NotEmpty(svc.GetCommonNames());

        svc.SetCommonCodes(new List<string> { "01", "05" });
        var fresh = new CommonHazardTypeService(_root);
        Assert.Equal(new[] { "01", "05" }, fresh.GetCommonCodes());
    }

    [Fact]
    public void 常用隐患类型_损坏配置_回退默认()
    {
        File.WriteAllText(Path.Combine(_root, "CommonHazardTypes.json"), "[[[ 非法");
        var svc = new CommonHazardTypeService(_root); // 不抛异常
        Assert.NotEmpty(svc.GetCommonCodes());
    }

    // ===== SettingFileService =====

    [Fact]
    public void 待清理目录_增删与持久化_去重()
    {
        var svc = new SettingFileService(_root);
        svc.AddPendingDeleteDir(@"D:\残留1");
        svc.AddPendingDeleteDir(@"D:\残留1"); // 去重
        svc.AddPendingDeleteDir(@"D:\残留2");

        Assert.Equal(2, svc.PendingDeleteDirs.Count);
        svc.RemovePendingDeleteDir(@"D:\残留1");
        Assert.Single(svc.PendingDeleteDirs);

        var fresh = new SettingFileService(_root);
        Assert.Single(fresh.PendingDeleteDirs);
    }

    [Fact]
    public void 待清理目录_损坏JSON_回退默认()
    {
        File.WriteAllText(Path.Combine(_root, "setting.json"), "!!!bad");
        var svc = new SettingFileService(_root);
        Assert.Empty(svc.PendingDeleteDirs);
    }

    // ===== SettingsService =====

    [Fact]
    public void 设置服务_默认值与修改持久化()
    {
        var settings = new SettingsService(new DataPathService(_root));
        Assert.Equal("Hazard", settings.GetCurrentModule());
        Assert.Equal("Internal", settings.GetImageViewerType());

        settings.SetCurrentModule("Personnel");
        settings.SetImageViewerType("System");
        settings.SetHazardTypeSelectorMode("DropdownMenu");
        settings.SetImageViewerStartMode("Normal");

        var fresh = new SettingsService(new DataPathService(_root));
        Assert.Equal("Personnel", fresh.GetCurrentModule());
        Assert.Equal("System", fresh.GetImageViewerType());
        Assert.Equal("DropdownMenu", fresh.GetHazardTypeSelectorMode());
        Assert.Equal("Normal", fresh.GetImageViewerStartMode());
    }

    [Fact]
    public void 设置服务_非法值_拒绝修改()
    {
        var settings = new SettingsService(new DataPathService(_root));
        settings.SetCurrentModule("非法模块");
        settings.SetImageViewerType("非法类型");
        Assert.Equal("Hazard", settings.GetCurrentModule());
        Assert.Equal("Internal", settings.GetImageViewerType());
    }

    [Fact]
    public void 设置服务_窗口规格_往返()
    {
        var settings = new SettingsService(new DataPathService(_root));
        Assert.Null(settings.GetWindowBounds()); // 初始无记录

        settings.SetWindowBounds(10, 20, 800, 600, "Normal");
        var bounds = settings.GetWindowBounds();
        Assert.NotNull(bounds);
        Assert.Equal((10d, 20d, 800d, 600d, "Normal"), bounds!.Value);

        // 损坏数据返回 null
        var master = new MasterDbContext(_root);
        master.SetSetting("WindowBounds", "不是数字|xx");
        var fresh = new SettingsService(new DataPathService(_root));
        Assert.Null(fresh.GetWindowBounds());
    }
}
