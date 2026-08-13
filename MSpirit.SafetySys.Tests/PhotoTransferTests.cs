// 手机图片传输（Services/PhotoTransferService + PhotoTransferLocationsService）核心逻辑测试：
// 文件名白名单 / 日期文件夹校验（防穿越） / EXIF 拍摄日期读取（无 EXIF 返回 null） / 位置列表增删持久化与容错
// 约定：只用临时目录，绝不触碰真实数据目录

using System.IO;
using MS_SafetyManagementSystem.Tools;
using SkiaSharp;

namespace MSpirit.SafetySys.Tests;

public class PhotoTransferServiceTests
{
    [Theory]
    [InlineData("IMG_20260812.jpg", true)]
    [InlineData("photo.JPEG", true)]
    [InlineData("a.PNG", true)]
    [InlineData("b.webp", true)]
    [InlineData("c.tiff", true)]
    [InlineData("d.gif", true)]
    [InlineData("evil.exe", false)]
    [InlineData("readme.txt", false)]
    [InlineData("", false)]
    [InlineData("noext", false)]
    public void IsSupportedImageName_白名单校验(string name, bool expected)
    {
        Assert.Equal(expected, PhotoTransferService.IsSupportedImageName(name));
    }

    [Theory]
    [InlineData("2026-08-12", true)]
    [InlineData("2026-8-2", false)] // 严格 yyyy-MM-dd（EXIF 解析值恒为两位，宽松无意义且更危险）
    [InlineData("2026/08/12", false)]
    [InlineData("..\\..\\evil", false)]
    [InlineData("../../evil", false)]
    [InlineData("2026-13-45", false)]
    [InlineData("", false)]
    public void IsValidDateFolder_校验与防穿越(string folder, bool expected)
    {
        Assert.Equal(expected, PhotoTransferService.IsValidDateFolder(folder));
    }

    [Fact]
    public void ReadExifShootTime_无EXIF图片_返回null()
    {
        // SkiaSharp 生成的纯色 PNG 无 EXIF → 应返回 null（触发兜底上传时间）
        string path = Path.Combine(Path.GetTempPath(), "MSpiritTests", Guid.NewGuid().ToString("N") + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var bmp = new SKBitmap(2, 2))
        using (var image = SKImage.FromBitmap(bmp))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            File.WriteAllBytes(path, data.ToArray());
        }

        try
        {
            Assert.Null(PhotoTransferService.ReadExifShootTime(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadExifShootTime_不存在的文件_返回null不抛异常()
    {
        Assert.Null(PhotoTransferService.ReadExifShootTime(Path.Combine(Path.GetTempPath(), "不存在.jpg")));
    }

    [Fact]
    public void ParseMtimeMillis_正常毫秒时间戳_返回本地时间()
    {
        var expected = new DateTime(2026, 8, 12, 18, 30, 0);
        var ms = new DateTimeOffset(expected).ToUnixTimeMilliseconds();
        var parsed = PhotoTransferService.ParseMtimeMillis(ms.ToString());
        Assert.NotNull(parsed);
        Assert.Equal(expected, parsed!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-100")]
    [InlineData("0")]
    [InlineData("99999999999999999999999")] // 超长溢出
    public void ParseMtimeMillis_非法输入_返回null(string? mtime)
    {
        Assert.Null(PhotoTransferService.ParseMtimeMillis(mtime));
    }

    [Fact]
    public void ParseMtimeMillis_超出合理范围_返回null()
    {
        // 早于 1975-12-01（第一张数码照片）与当前+2 年（超出上限）
        var tooOld = new DateTimeOffset(new DateTime(1970, 1, 1)).ToUnixTimeMilliseconds();
        var tooNew = new DateTimeOffset(DateTime.Now.AddYears(2)).ToUnixTimeMilliseconds();
        Assert.Null(PhotoTransferService.ParseMtimeMillis(tooOld.ToString()));
        Assert.Null(PhotoTransferService.ParseMtimeMillis(tooNew.ToString()));
    }

    [Fact]
    public void ParseMtimeMillis_第一张数码照片纪念日当天_有效()
    {
        // 下限边界：1975-12-01 当天应有效（下限为闭区间）
        var ms = new DateTimeOffset(PhotoTransferService.FirstDigitalPhotoDate).ToUnixTimeMilliseconds();
        var parsed = PhotoTransferService.ParseMtimeMillis(ms.ToString());
        Assert.NotNull(parsed);
    }
}

public class PhotoTransferLocationsServiceTests : IDisposable
{
    private readonly string _rootDir;

    public PhotoTransferLocationsServiceTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "MSpiritTests", "PTLoc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, true); } catch { }
    }

    private PhotoTransferLocationsService NewService() => new(_rootDir);

    private string CreateDir(string name)
    {
        string p = Path.Combine(_rootDir, name);
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void 默认位置为用户图片目录()
    {
        var svc = NewService();
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), svc.DefaultLocation);
        Assert.Equal(svc.DefaultLocation, svc.CurrentLocation); // 初始选中=默认
    }

    [Fact]
    public void AddCustom_添加成功_重复与默认拒绝()
    {
        var svc = NewService();
        string a = CreateDir("a");

        Assert.True(svc.AddCustom(a));
        Assert.False(svc.AddCustom(a));                      // 重复拒绝
        Assert.False(svc.AddCustom(svc.DefaultLocation));    // 与默认重复拒绝
        Assert.False(svc.AddCustom(Path.Combine(_rootDir, "不存在"))); // 目录不存在拒绝

        var all = svc.GetAllLocations();
        Assert.Equal(2, all.Count); // 默认 + a
        Assert.Contains(a, all);
    }

    [Fact]
    public void RemoveCustom_默认不可移除_移除当前选中回退默认()
    {
        var svc = NewService();
        string a = CreateDir("a");
        svc.AddCustom(a);

        Assert.False(svc.RemoveCustom(svc.DefaultLocation)); // 默认不可移除
        Assert.True(svc.RemoveCustom(a));
        Assert.Single(svc.GetAllLocations());                // 只剩默认
    }

    [Fact]
    public void SetCurrent_持久化到文件_重新实例化仍生效()
    {
        string a = CreateDir("a");
        var svc = NewService();
        svc.AddCustom(a);
        svc.SetCurrent(a);

        var reloaded = NewService(); // 重新读取 json
        Assert.Equal(a, reloaded.CurrentLocation);
    }

    [Fact]
    public void 损坏配置文件_回默认不抛异常()
    {
        File.WriteAllText(Path.Combine(_rootDir, "PhotoTransferLocations.json"), "{ 这不是合法JSON ]");
        var svc = NewService();
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), svc.DefaultLocation);
        Assert.Single(svc.GetAllLocations());
    }
}
