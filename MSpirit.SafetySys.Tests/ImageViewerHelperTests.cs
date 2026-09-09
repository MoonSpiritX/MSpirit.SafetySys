// D 层：ImageViewerHelper 测试（图片格式判断 + 模块图片索引构建）
// GetScreenWorkingArea 依赖真实窗口/Win32，不在单测范围

using MS_SafetyManagementSystem.ImageViewer;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class ImageViewerHelperTests : IDisposable
{
    private readonly string _root;

    public ImageViewerHelperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "ImageViewerHelper", Guid.NewGuid().ToString("N"));
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

    // ===== IsSupportedImage =====

    [Theory]
    [InlineData("a.jpg")]
    [InlineData("a.jpeg")]
    [InlineData("a.png")]
    [InlineData("a.gif")]
    [InlineData("a.bmp")]
    [InlineData("a.webp")]
    [InlineData("a.avif")]
    [InlineData("a.tiff")]
    [InlineData("a.tif")]
    [InlineData("a.jfif")]
    [InlineData("A.PNG")]          // 大写扩展名
    [InlineData("photo.avif")]
    public void IsSupportedImage_支持的格式_返回true(string fileName)
    {
        Assert.True(ImageViewerHelper.IsSupportedImage(Path.Combine(_root, fileName)));
    }

    [Theory]
    [InlineData("a.pdf")]
    [InlineData("a.txt")]
    [InlineData("a.docx")]
    [InlineData("noextension")]
    [InlineData(".hidden")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupportedImage_不支持的格式或空_返回false(string? fileName)
    {
        string? path = fileName == null ? null : Path.Combine(_root, fileName);
        Assert.False(ImageViewerHelper.IsSupportedImage(path!));
    }

    // ===== BuildModuleImageIndex =====

    [Fact]
    public void 索引构建_按卡顺序与文件名排序_排除缩略图与不支持格式()
    {
        // 模块目录：Hazard_2026-08/，含 Card_a 与 Card_b
        string baseDir = Path.Combine(_root, "Hazard_2026-08");
        string cardA = Path.Combine(baseDir, "Card_a");
        string cardB = Path.Combine(baseDir, "Card_b");
        Directory.CreateDirectory(cardA);
        Directory.CreateDirectory(cardB);
        File.WriteAllText(Path.Combine(cardA, "b.png"), "x");
        File.WriteAllText(Path.Combine(cardA, "a.jpg"), "x");
        File.WriteAllText(Path.Combine(cardA, "c_thumb.jpg"), "x"); // 缩略图应排除
        File.WriteAllText(Path.Combine(cardA, "d.txt"), "x");       // 不支持格式应排除
        File.WriteAllText(Path.Combine(cardB, "g.JPG"), "x");
        File.WriteAllText(Path.Combine(cardB, "f.jpg"), "x");

        var (all, idx) = ImageViewerHelper.BuildModuleImageIndex(baseDir, new List<string> { "a", "b" }, Path.Combine(cardA, "b.png"));

        // 顺序：Card_a 内按文件名（a.jpg, b.png）→ Card_b 内（f.jpg, g.JPG）
        Assert.Equal(4, all.Count);
        Assert.Equal(Path.Combine(cardA, "a.jpg"), all[0]);
        Assert.Equal(Path.Combine(cardA, "b.png"), all[1]);
        Assert.Equal(Path.Combine(cardB, "f.jpg"), all[2]);
        Assert.Equal(Path.Combine(cardB, "g.JPG"), all[3]);
        Assert.DoesNotContain(all, p => p.Contains("_thumb"));
        Assert.DoesNotContain(all, p => p.EndsWith(".txt"));
        Assert.Equal(1, idx); // 当前图片 b.png 在索引 1
    }

    [Fact]
    public void 索引构建_不存在的卡片_跳过()
    {
        string baseDir = Path.Combine(_root, "Hazard_2026-08");
        string cardA = Path.Combine(baseDir, "Card_a");
        Directory.CreateDirectory(cardA);
        File.WriteAllText(Path.Combine(cardA, "a.jpg"), "x");

        var (all, _) = ImageViewerHelper.BuildModuleImageIndex(baseDir, new List<string> { "a", "不存在的卡" }, "");
        Assert.Single(all); // 只有 Card_a 的图片
    }

    [Fact]
    public void 索引构建_空记录列表_返回空()
    {
        string baseDir = Path.Combine(_root, "Hazard_2026-08");
        Directory.CreateDirectory(baseDir);
        var (all, idx) = ImageViewerHelper.BuildModuleImageIndex(baseDir, new List<string>(), "x.jpg");
        Assert.Empty(all);
        Assert.Equal(-1, idx);
    }

    [Fact]
    public void 索引构建_当前图片不在列表_返回负一()
    {
        string baseDir = Path.Combine(_root, "Hazard_2026-08");
        string cardA = Path.Combine(baseDir, "Card_a");
        Directory.CreateDirectory(cardA);
        File.WriteAllText(Path.Combine(cardA, "a.jpg"), "x");

        var (_, idx) = ImageViewerHelper.BuildModuleImageIndex(baseDir, new List<string> { "a" }, Path.Combine(cardA, "不存在.jpg"));
        Assert.Equal(-1, idx);
    }
}
