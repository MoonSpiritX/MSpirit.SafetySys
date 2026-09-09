// 图片剪裁（阶段 2 纯逻辑）测试：坐标换算 CropGeometry + 裁剪框状态机 CropRegion

using MS_SafetyManagementSystem.Tools;

namespace MSpirit.SafetySys.Tests;

public class CropGeometryTests
{
    // ===== ComputeUniformTransform =====

    [Fact]
    public void Uniform变换_宽图_以宽度为准_垂直居中()
    {
        // 2000x800 宽图放入 1000x500 区域：scale=0.5（宽主导），显示 1000x400，垂直偏移 (500-400)/2=50
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(1000, 500, 2000, 800);
        Assert.Equal(0.5, scale, 3);
        Assert.Equal(0, ox, 3);
        Assert.Equal(50, oy, 3);
    }

    [Fact]
    public void Uniform变换_高图_以高度为准_水平居中()
    {
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(1000, 500, 500, 1000);
        Assert.Equal(0.5, scale, 3);
        Assert.Equal(375, ox, 3);
        Assert.Equal(0, oy, 3);
    }

    [Fact]
    public void Uniform变换_等比例_无偏移()
    {
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(800, 600, 800, 600);
        Assert.Equal(1, scale, 3);
        Assert.Equal(0, ox, 3);
        Assert.Equal(0, oy, 3);
    }

    [Fact]
    public void Uniform变换_无效输入_返回单位变换()
    {
        Assert.Equal((1, 0, 0), CropGeometry.ComputeUniformTransform(0, 0, 100, 100));
        Assert.Equal((1, 0, 0), CropGeometry.ComputeUniformTransform(100, 100, 0, 0));
    }

    // ===== DisplayToImage / PixelRect =====

    [Fact]
    public void 显示到像素_反变换()
    {
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(1000, 500, 2000, 800); // 0.5, 0, 50
        var (x, y) = CropGeometry.DisplayToImage(100, 250, scale, ox, oy);
        Assert.Equal(200, x, 3);
        Assert.Equal(400, y, 3);
    }

    [Fact]
    public void 显示选区转像素选区_尺寸按比例放大()
    {
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(1000, 500, 2000, 800);
        var rect = CropGeometry.DisplayRectToPixelRect(100, 50, 200, 100, scale, ox, oy, 2000, 800);
        Assert.Equal(new CropGeometry.PixelRect(200, 0, 400, 200), rect);
    }

    [Fact]
    public void 像素选区_边界钳制_不越界()
    {
        // 选区超出图片右/下边界 → 钳制
        var rect = CropGeometry.DisplayRectToPixelRect(900, 400, 300, 300, 1, 0, 0, 1000, 500);
        Assert.Equal(new CropGeometry.PixelRect(900, 400, 100, 100), rect);
    }

    [Fact]
    public void 像素选区_负坐标_钳制到零()
    {
        var rect = CropGeometry.DisplayRectToPixelRect(-50, -50, 100, 100, 1, 0, 0, 1000, 500);
        Assert.Equal(new CropGeometry.PixelRect(0, 0, 50, 50), rect);
    }

    [Fact]
    public void 像素选区_无效缩放_返回空()
    {
        var rect = CropGeometry.DisplayRectToPixelRect(0, 0, 100, 100, 0, 0, 0, 1000, 500);
        Assert.Equal(new CropGeometry.PixelRect(0, 0, 0, 0), rect);
    }

    // ===== RegionRectToPixelRect（2026-08-16 修复回归）=====
    // CropOverlay 的裁剪框坐标域是"相对图片显示区域"（不含 offset）；此前误把该坐标直接传给
    // DisplayRectToPixelRect（期望控件坐标，内部再减 offset）→ 非满幅（有留白）时像素选区向左上偏移。
    // RegionRectToPixelRect 把"加 offset"收进函数内，以下用例验证坐标域与钳制。

    [Fact]
    public void Region坐标转像素_有留白时位置不偏移_回归()
    {
        // 控件 1000×500，图片 2000×800 → scale=0.5, offsetX=0, offsetY=50（图片垂直居中，上下各留 50）
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(1000, 500, 2000, 800);
        Assert.Equal(0.5, scale, 3);
        Assert.Equal(0, ox, 3);
        Assert.Equal(50, oy, 3);

        // 框选图片显示区域（不含留白）内 region(200,150,100,100)：
        // RegionRectToPixelRect 内部加 offset 后由 DisplayRectToPixelRect 再减 offset → 恰好抵消，
        // 像素坐标 = region 坐标 / scale：x=200/0.5=400, y=150/0.5=300, w=h=200。
        var rect = CropGeometry.RegionRectToPixelRect(200, 150, 100, 100, scale, ox, oy, 2000, 800);
        Assert.Equal(new CropGeometry.PixelRect(400, 300, 200, 200), rect);
    }

    [Fact]
    public void Region坐标转像素_与控件坐标路径等价_回归()
    {
        // 控件 1200×400，图片 2000×800 → scale=0.5, offsetX=100（图片水平居中，两侧各留 100）
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(1200, 400, 2000, 800);
        Assert.True(ox > 0);

        // region 坐标 + offset 后应等价于 DisplayRectToPixelRect 的控件坐标输入
        double rx = 100, ry = 50, rw = 300, rh = 200;
        var viaRegion = CropGeometry.RegionRectToPixelRect(rx, ry, rw, rh, scale, ox, oy, 2000, 800);
        var viaDisplay = CropGeometry.DisplayRectToPixelRect(rx + ox, ry + oy, rw, rh, scale, ox, oy, 2000, 800);
        Assert.Equal(viaDisplay, viaRegion);
    }
}

public class CropRegionTests
{
    private const double BoundsW = 800, BoundsH = 600;

    // ===== Reset 默认选区 =====

    [Fact]
    public void 自由模式_默认选区_全图()
    {
        var r = new CropRegion();
        r.Reset(BoundsW, BoundsH);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.Equal(BoundsW, r.Width);
        Assert.Equal(BoundsH, r.Height);
    }

    [Fact]
    public void 固定比例_默认选区_比例正确且居中()
    {
        var r = new CropRegion();
        r.SetAspectRatio(1, BoundsW, BoundsH); // 1:1
        Assert.Equal(1, r.Width / r.Height, 3);
        Assert.Equal((BoundsW - r.Width) / 2, r.X, 3);   // 水平居中
        Assert.Equal((BoundsH - r.Height) / 2, r.Y, 3);  // 垂直居中
        Assert.Equal(600, r.Height, 3);
        Assert.Equal(600, r.Width, 3);
    }

    [Fact]
    public void 固定比例16比9_默认选区()
    {
        var r = new CropRegion();
        r.SetAspectRatio(16.0 / 9.0, BoundsW, BoundsH);
        Assert.Equal(16.0 / 9.0, r.Width / r.Height, 3);
        Assert.True(r.Width <= BoundsW && r.Height <= BoundsH);
    }

    [Fact]
    public void 无效边界_选区归零()
    {
        var r = new CropRegion();
        r.Reset(0, 0);
        Assert.Equal(0, r.Width);
        Assert.Equal(0, r.Height);
    }

    // ===== Move =====

    [Fact]
    public void 移动_边界钳制()
    {
        var r = new CropRegion();
        r.Reset(BoundsW, BoundsH);
        r.ResizeTo(CropHandle.BottomRight, 400, 300, BoundsW, BoundsH); // 缩小为 400x300 才有移动空间
        double w = r.Width, h = r.Height;
        r.Move(50, 30, BoundsW, BoundsH);
        Assert.Equal(50, r.X);
        Assert.Equal(30, r.Y);
        Assert.Equal(w, r.Width);
        Assert.Equal(h, r.Height);

        r.Move(1000, 1000, BoundsW, BoundsH); // 越界 → 钳到最大
        Assert.Equal(BoundsW - r.Width, r.X);
        Assert.Equal(BoundsH - r.Height, r.Y);

        r.Move(-5000, -5000, BoundsW, BoundsH);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
    }

    [Fact]
    public void 移动_选区尺寸不变()
    {
        var r = new CropRegion();
        r.SetAspectRatio(1, BoundsW, BoundsH);
        double w = r.Width, h = r.Height;
        r.Move(10, 20, BoundsW, BoundsH);
        Assert.Equal(w, r.Width);
        Assert.Equal(h, r.Height);
    }

    // ===== ResizeTo =====

    [Fact]
    public void 缩放右边_宽度变化_上下边不动()
    {
        var r = new CropRegion();
        r.Reset(BoundsW, BoundsH); // 全图 0,0,800,600
        r.ResizeTo(CropHandle.Right, 600, 0, BoundsW, BoundsH);
        Assert.Equal(0, r.X);
        Assert.Equal(600, r.Width);
        Assert.Equal(600, r.Height);
    }

    [Fact]
    public void 缩放左边_右边锚定不动()
    {
        var r = new CropRegion();
        r.Reset(BoundsW, BoundsH);
        r.ResizeTo(CropHandle.Left, 200, 0, BoundsW, BoundsH);
        Assert.Equal(200, r.X);
        Assert.Equal(600, r.Width); // 800-200
    }

    [Fact]
    public void 缩放_最小尺寸限制()
    {
        var r = new CropRegion();
        r.Reset(BoundsW, BoundsH);
        r.ResizeTo(CropHandle.Right, 1, 0, BoundsW, BoundsH); // 拉到 1px
        Assert.True(r.Width >= CropRegion.MinSize);
    }

    [Fact]
    public void 缩放_边界钳制_不越界()
    {
        var r = new CropRegion();
        r.Reset(BoundsW, BoundsH);
        r.ResizeTo(CropHandle.Right, 2000, 0, BoundsW, BoundsH); // 超出右边界
        Assert.Equal(BoundsW, r.X + r.Width, 3);
    }

    [Fact]
    public void 固定比例_缩放_保持比例()
    {
        var r = new CropRegion();
        r.SetAspectRatio(1, BoundsW, BoundsH); // 1:1 全图 600x600 居中 (100,0)
        double ratio = r.Width / r.Height;

        // 拖动右下角缩小
        r.ResizeTo(CropHandle.BottomRight, 300, 300, BoundsW, BoundsH);
        Assert.Equal(ratio, r.Width / r.Height, 3);
        Assert.True(r.Width <= 600 && r.Height <= 600);
        // 锚定左上角 (100,0) 不动
        Assert.Equal(100, r.X, 3);
        Assert.Equal(0, r.Y, 3);
    }

    [Fact]
    public void 固定比例_缩放左边_仍保持比例()
    {
        var r = new CropRegion();
        r.SetAspectRatio(16.0 / 9.0, BoundsW, BoundsH);
        double ratio = r.Width / r.Height;
        double right = r.X + r.Width;

        r.ResizeTo(CropHandle.Left, 100, 0, BoundsW, BoundsH);
        Assert.Equal(ratio, r.Width / r.Height, 3);
        Assert.Equal(right, r.X + r.Width, 3); // 右边锚定
    }

    // ===== 固定比例下拖"边"向外扩大（2026-08-16 修复回归）=====
    // 原实现比例约束只有"收缩主导维"分支：向外拖边 → 宽被缩回 h×ratio → 纹丝不动。
    // 修复后按被拖边联动另一维（等比放大），以下用例验证各边向外扩大、比例保持、锚定边固定。

    [Fact]
    public void 固定比例_拖右边向外_等比扩大且左边锚定()
    {
        var r = new CropRegion();
        r.SetAspectRatio(1, BoundsW, BoundsH);          // 1:1 → 600x600 居中 (100,0)
        r.ResizeTo(CropHandle.BottomRight, 300, 300, BoundsW, BoundsH); // 先缩到 200x200
        double x0 = r.X, y0 = r.Y;
        double right0 = r.X + r.Width;

        // 向右拖出选区右边界（200 → 700）→ 应等比扩大而非纹丝不动
        r.ResizeTo(CropHandle.Right, right0 + 500, 0, BoundsW, BoundsH);

        Assert.Equal(1, r.Width / r.Height, 3); // 比例保持
        Assert.True(r.Width > 200, $"向外拖右边应扩大，实际宽={r.Width}");
        Assert.Equal(x0, r.X, 3);                // 左边锚定
        Assert.Equal(y0, r.Y, 3);                // 上边锚定
        Assert.True(r.Y + r.Height <= BoundsH + 0.01); // 不越界（高随宽联动可能触底）
    }

    [Fact]
    public void 固定比例_拖上边向外_等比扩大且下边锚定()
    {
        var r = new CropRegion();
        r.SetAspectRatio(1, BoundsW, BoundsH);                          // 600x600 居中 (100,0)
        r.ResizeTo(CropHandle.BottomRight, 500, 400, BoundsW, BoundsH); // 缩到 400x400 (100,0)
        r.Move(100, 100, BoundsW, BoundsH);                             // 移到 (200,100)，上边=100 不再贴顶
        double bottom0 = r.Y + r.Height;                                // = 500（下边锚定点）
        double left0 = r.X;                                             // = 200

        // 向上拖出选区上边界（100 → 50）→ 应等比扩大
        r.ResizeTo(CropHandle.Top, 0, 50, BoundsW, BoundsH);

        Assert.Equal(1, r.Width / r.Height, 3);
        Assert.True(r.Height > 400, $"向外拖上边应扩大，实际高={r.Height}");
        Assert.Equal(bottom0, r.Y + r.Height, 3); // 下边锚定
        Assert.Equal(left0, r.X, 3);              // 左边锚定
    }

    [Fact]
    public void 固定比例_拖右边到边界_等比扩大受边界钳制()
    {
        var r = new CropRegion();
        r.SetAspectRatio(16.0 / 9.0, BoundsW, BoundsH); // 800x450 居中 (0,75)
        r.ResizeTo(CropHandle.BottomRight, 400, 300, BoundsW, BoundsH); // 缩到 (0,75)-(400,300)

        // 向右拖到超出图片右边界 → 宽被钳到边界，高按比例联动且不越界
        r.ResizeTo(CropHandle.Right, BoundsW + 1000, 0, BoundsW, BoundsH);

        Assert.Equal(16.0 / 9.0, r.Width / r.Height, 3); // 比例保持
        Assert.True(r.X + r.Width <= BoundsW + 0.01);     // 右边界
        Assert.True(r.Y + r.Height <= BoundsH + 0.01);    // 高联动后不越界
    }
}
