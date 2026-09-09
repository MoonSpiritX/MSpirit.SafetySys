// 图片剪裁（阶段 2）测试：CropImageProcessor（摆正尺寸读取 / 解码摆正 / 像素裁剪）

using MS_SafetyManagementSystem.Tools;
using SkiaSharp;
using System.IO;

namespace MSpirit.SafetySys.Tests;

public class CropImageProcessorTests : IDisposable
{
    private readonly string _root;

    public CropImageProcessorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MSpiritTests", "CropProcessor", Guid.NewGuid().ToString("N"));
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

    /// <summary>生成一张指定尺寸的 JPEG</summary>
    private string CreateJpeg(int width, int height)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.jpg");
        using var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Red);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    // ===== GetCorrectedSize =====

    [Fact]
    public void 摆正尺寸_普通图片_返回原始宽高()
    {
        string file = CreateJpeg(800, 600);
        var size = CropImageProcessor.GetCorrectedSize(file);
        Assert.NotNull(size);
        Assert.Equal(800, size!.Value.Width);
        Assert.Equal(600, size.Value.Height);
    }

    [Fact]
    public void 摆正尺寸_文件不存在_返回null()
    {
        Assert.Null(CropImageProcessor.GetCorrectedSize(Path.Combine(_root, "不存在.jpg")));
    }

    // ===== DecodeCorrected =====

    [Fact]
    public void 解码摆正_无EXIF方向_输出同尺寸()
    {
        string file = CreateJpeg(400, 300);
        using var bmp = CropImageProcessor.DecodeCorrected(file);
        Assert.NotNull(bmp);
        Assert.Equal(400, bmp!.Width);
        Assert.Equal(300, bmp.Height);
    }

    [Fact]
    public void 解码摆正_限制最大边_等比缩放()
    {
        string file = CreateJpeg(2000, 1000);
        using var bmp = CropImageProcessor.DecodeCorrected(file, 800);
        Assert.NotNull(bmp);
        Assert.True(bmp!.Width <= 800 && bmp.Height <= 800);
        Assert.Equal(2.0, (double)bmp.Width / bmp.Height, 2); // 保持宽高比
    }

    [Fact]
    public void 解码摆正_小图不放大()
    {
        string file = CreateJpeg(300, 200);
        using var bmp = CropImageProcessor.DecodeCorrected(file, 800);
        Assert.Equal(300, bmp!.Width);
        Assert.Equal(200, bmp.Height);
    }

    // ===== Crop 裁剪 =====

    [Fact]
    public void 裁剪_输出指定区域_独立副本()
    {
        string file = CreateJpeg(100, 100);
        using var src = CropImageProcessor.DecodeCorrected(file);
        using var cropped = CropImageProcessor.Crop(src!, 10, 20, 50, 30);

        Assert.NotNull(cropped);
        Assert.Equal(50, cropped!.Width);
        Assert.Equal(30, cropped.Height);
        // 独立副本：释放源后结果仍可用（不共享像素）
        src!.Dispose();
        Assert.Equal(50, cropped.Width);
        Assert.Equal(30, cropped.Height);
    }

    [Fact]
    public void 裁剪_越界_钳制到图片范围()
    {
        string file = CreateJpeg(100, 100);
        using var src = CropImageProcessor.DecodeCorrected(file);
        using var cropped = CropImageProcessor.Crop(src!, 80, 80, 100, 100); // 超出右/下
        Assert.Equal(20, cropped!.Width);
        Assert.Equal(20, cropped.Height);
    }

    [Fact]
    public void 裁剪_无效参数_返回null()
    {
        using var bmp = new SKBitmap(10, 10);
        Assert.Null(CropImageProcessor.Crop(null!, 0, 0, 5, 5));
        Assert.Null(CropImageProcessor.Crop(bmp, 0, 0, 0, 0));
        Assert.Null(CropImageProcessor.Crop(bmp, 0, 0, -1, 5));
    }
}
