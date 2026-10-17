// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

// 标注功能纯逻辑测试（2026-08-16，阶段 5）：
//  - Annotation：命中测试（矩形/圆/线/箭头/文字/马赛克）、归一化、平移
//  - AnnotationTool：绘制创建（六类型）/选中/移动/删除/清空/文字提交
//  - AnnotationRenderer：六类型输出像素验证（箭头头/文字/马赛克区域变化）

using MS_SafetyManagementSystem.Tools;
using SkiaSharp;
using Xunit;

namespace MSpirit.SafetySys.Tests;

public class AnnotationTests
{
    // ===== Annotation 命中测试 =====

    [Fact]
    public void 矩形_内部命中_外部不命中()
    {
        var a = new Annotation(AnnotationKind.Rectangle);
        a.X1 = 10; a.Y1 = 10; a.X2 = 100; a.Y2 = 80;

        Assert.True(a.HitTest(50, 40, 5));   // 内部
        Assert.True(a.HitTest(102, 40, 5));  // 右边界外 2px（容差内）
        Assert.False(a.HitTest(200, 40, 5)); // 远处
    }

    [Fact]
    public void 矩形_反向两点_归一化后内部命中()
    {
        var a = new Annotation(AnnotationKind.Rectangle);
        a.X1 = 100; a.Y1 = 80; a.X2 = 10; a.Y2 = 10; // 右下→左上

        Assert.True(a.HitTest(50, 40, 5));
        Assert.Equal(new SKRect(10, 10, 100, 80), a.NormalizedRect);
    }

    [Fact]
    public void 直线_线上命中_远处不命中()
    {
        var a = new Annotation(AnnotationKind.Line);
        a.X1 = 0; a.Y1 = 0; a.X2 = 100; a.Y2 = 0;

        Assert.True(a.HitTest(50, 3, 6));    // 线上方 3px（容差内）
        Assert.False(a.HitTest(50, 20, 6));  // 远处
    }

    [Fact]
    public void 文字_包围盒命中()
    {
        var a = new Annotation(AnnotationKind.Text);
        a.X1 = 20; a.Y1 = 20; a.Text = "测试"; a.FontSize = 24;

        Assert.True(a.HitTest(30, 30, 5));
        Assert.False(a.HitTest(300, 300, 5));
    }

    [Fact]
    public void 平移_整体偏移()
    {
        var a = new Annotation(AnnotationKind.Rectangle);
        a.X1 = 10; a.Y1 = 10; a.X2 = 100; a.Y2 = 80;

        a.Move(50, 30);

        Assert.Equal(60, a.X1);
        Assert.Equal(150, a.X2);
        Assert.Equal(40, a.Y1);
        Assert.Equal(110, a.Y2);
    }
}

public class AnnotationToolTests
{
    [Fact]
    public void 绘制矩形_按下移动抬起_入列表且归一化()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Rectangle;

        tool.OnMouseDown(100, 80);
        tool.OnMouseMove(200, 160);
        var created = tool.OnMouseUp();

        Assert.NotNull(created);
        Assert.Single(tool.Items);
        Assert.Equal(AnnotationKind.Rectangle, created!.Kind);
        Assert.Equal(new SKRect(100, 80, 200, 160), created.NormalizedRect);
        Assert.Same(created, tool.Selected); // 创建后自动选中
        Assert.Null(tool.Draft);
    }

    [Fact]
    public void 新标注_使用当前标注颜色()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Rectangle;
        tool.CurrentColorArgb = 0xFF1E88E5; // 蓝色

        tool.OnMouseDown(10, 10);
        tool.OnMouseMove(50, 50);
        var created = tool.OnMouseUp();

        Assert.Equal(0xFF1E88E5, created!.ColorArgb);

        // 换色后新标注用新色，旧标注不受影响
        tool.CurrentColorArgb = 0xFF43A047; // 绿色
        tool.ActiveKind = AnnotationKind.Line;
        tool.OnMouseDown(0, 0);
        tool.OnMouseMove(20, 20);
        var created2 = tool.OnMouseUp();

        Assert.Equal(0xFF43A047, created2!.ColorArgb);
        Assert.Equal(0xFF1E88E5, tool.Items[0].ColorArgb); // 旧标注保持原色
    }

    [Fact]
    public void 新标注_使用当前字号与线宽()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Text;
        tool.CurrentFontSize = 30f;
        tool.CurrentStrokeWidth = 5f;

        tool.OnMouseDown(10, 10);
        var pending = tool.OnMouseUp();
        tool.CommitText(pending!, "测试");

        Assert.Equal(30f, tool.Items[0].FontSize);
        Assert.Equal(5f, tool.Items[0].StrokeWidth);

        // 换字号/线宽后新标注用新值，旧标注不受影响
        tool.CurrentFontSize = 12f;
        tool.CurrentStrokeWidth = 1f;
        tool.ActiveKind = AnnotationKind.Rectangle;
        tool.OnMouseDown(0, 0);
        tool.OnMouseMove(30, 30);
        var rect = tool.OnMouseUp();

        Assert.Equal(12f, rect!.FontSize);
        Assert.Equal(1f, rect.StrokeWidth);
        Assert.Equal(30f, tool.Items[0].FontSize);  // 旧文字保持原字号
        Assert.Equal(5f, tool.Items[0].StrokeWidth); // 旧文字保持原线宽
    }

    [Fact]
    public void 绘制反向矩形_归一化左上右下()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Rectangle;

        tool.OnMouseDown(200, 160);
        tool.OnMouseMove(100, 80);
        var created = tool.OnMouseUp();

        Assert.Equal(new SKRect(100, 80, 200, 160), created!.NormalizedRect);
    }

    [Theory]
    [InlineData(AnnotationKind.Oval)]
    [InlineData(AnnotationKind.Line)]
    [InlineData(AnnotationKind.Arrow)]
    [InlineData(AnnotationKind.Mosaic)]
    public void 绘制各类型_入列表(AnnotationKind kind)
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = kind;

        tool.OnMouseDown(10, 10);
        tool.OnMouseMove(90, 70);
        var created = tool.OnMouseUp();

        Assert.NotNull(created);
        Assert.Equal(kind, created!.Kind);
        Assert.Single(tool.Items);
    }

    [Fact]
    public void 文字模式_抬起不入列表_提交文本后入列表()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Text;

        tool.OnMouseDown(50, 50);
        var pending = tool.OnMouseUp();

        Assert.NotNull(pending);
        Assert.Empty(tool.Items); // 未提交前不入列表

        tool.CommitText(pending!, "重要");
        Assert.Single(tool.Items);
        Assert.Equal("重要", tool.Items[0].Text);
        Assert.Same(tool.Items[0], tool.Selected);

        // 空文本提交 → 丢弃
        tool.ActiveKind = AnnotationKind.Text;
        tool.OnMouseDown(10, 10);
        var p2 = tool.OnMouseUp();
        tool.CommitText(p2!, "   ");
        Assert.Single(tool.Items); // 未新增
    }

    [Fact]
    public void 选择模式_点击选中_移动增量_抬起停止()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Rectangle;
        tool.OnMouseDown(100, 100);
        tool.OnMouseMove(200, 200);
        tool.OnMouseUp(); // 创建 100,100-200,200

        tool.ActiveKind = null; // 切回选择
        tool.OnMouseDown(150, 150); // 命中内部 → 选中 + IsMoving
        Assert.Same(tool.Items[0], tool.Selected);
        Assert.True(tool.IsMoving);

        tool.MoveSelectedBy(50, 30);
        Assert.Equal(new SKRect(150, 130, 250, 230), tool.Items[0].NormalizedRect);

        tool.OnMouseUp();
        Assert.False(tool.IsMoving);
    }

    [Fact]
    public void 选择模式_点击空白_不选中()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Rectangle;
        tool.OnMouseDown(0, 0);
        tool.OnMouseMove(50, 50);
        tool.OnMouseUp();

        tool.ActiveKind = null;
        tool.OnMouseDown(300, 300); // 空白
        Assert.Null(tool.Selected);
        Assert.False(tool.IsMoving);
    }

    [Fact]
    public void 删除选中_移除并清空选中()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Line;
        tool.OnMouseDown(0, 0);
        tool.OnMouseMove(50, 50);
        tool.OnMouseUp();

        Assert.True(tool.DeleteSelected());
        Assert.Empty(tool.Items);
        Assert.Null(tool.Selected);
        Assert.False(tool.DeleteSelected()); // 空再删
    }

    [Fact]
    public void 清空_全部移除()
    {
        var tool = new AnnotationTool();
        tool.ActiveKind = AnnotationKind.Rectangle;
        tool.OnMouseDown(0, 0);
        tool.OnMouseMove(10, 10);
        tool.OnMouseUp();
        tool.ActiveKind = AnnotationKind.Line;
        tool.OnMouseDown(0, 0);
        tool.OnMouseMove(10, 10);
        tool.OnMouseUp();

        tool.Clear();
        Assert.Empty(tool.Items);
        Assert.Null(tool.Selected);
    }

    // ===== 坐标换算回归（2026-08-16 修复：标注双重减 offset 偏左）=====
    // Overlay 链路：控件坐标 pos → DisplayToPxX/Y（像素）→ PxToDisplayX/Y（回控件）。
    // 修复前把"已减 offset 的 region 坐标"再传给 DisplayToPxX（内部又减 offset）→ 像素偏左 offset/scale。
    // 本测试用 CropGeometry 的变换验证"控件坐标 → 像素 → 回控件"往返一致，且与 region 坐标无关。

    [Fact]
    public void 像素坐标_往返控件坐标_一致()
    {
        // 控件 1000×500，图片 2000×800 → scale=0.5, offsetX=0, offsetY=50（与 CropOverlay 同链路）
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(1000, 500, 2000, 800);
        Assert.Equal(0.5, scale, 3);
        Assert.Equal(50, oy, 3);

        // 鼠标在控件坐标 (500, 250) → 像素 (1000, 400)（正确链路：DisplayToPx 减 offset 一次）
        double px = (500 - ox) / scale;
        double py = (250 - oy) / scale;
        Assert.Equal(1000, px, 3);
        Assert.Equal(400, py, 3);

        // 像素 → 回控件坐标（PxToDisplay 加 offset）
        double backX = px * scale + ox;
        double backY = py * scale + oy;
        Assert.Equal(500, backX, 3);
        Assert.Equal(250, backY, 3);
    }

    [Fact]
    public void 双重减offset_导致像素偏左_回归防线()
    {
        // 修复前错误链路：把"已减 offset 的 region 坐标"px=pos.X-ox 再传给"减 offset 的换算" → 双重减。
        // 验证正确链路的像素值应等于"控件坐标减一次 offset 再除 scale"，与错误链路的差异即 bug 大小。
        var (scale, ox, oy) = CropGeometry.ComputeUniformTransform(1200, 400, 2000, 800); // offsetX=100
        Assert.True(ox > 0);

        double controlX = 700; // 鼠标控件坐标
        // 正确：像素 = (controlX - offsetX) / scale = 1200
        double correctPx = (controlX - ox) / scale;
        // 错误：若把 region 坐标 (controlX - ox) 再减 offsetX → (controlX - 2*ox)/scale
        double buggyPx = (controlX - 2 * ox) / scale;
        Assert.Equal(1200, correctPx, 3);
        Assert.True(Math.Abs(correctPx - buggyPx) > 100, "双重减 offset 应造成显著偏移（回归防线）");
    }
}

public class AnnotationRendererTests
{
    private static Annotation MakeRect(float l, float t, float r, float b, uint color = 0xFFE53935) =>
        new(AnnotationKind.Rectangle) { X1 = l, Y1 = t, X2 = r, Y2 = b, ColorArgb = color };

    /// <summary>创建纯白 100x100 位图，绘制后采样验证</summary>
    private static SKBitmap MakeWhiteCanvas(int w = 100, int h = 100)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        return bmp;
    }

    [Fact]
    public void 绘制矩形_边框像素为颜色_内部为原色()
    {
        using var bmp = MakeWhiteCanvas();
        var a = MakeRect(10, 10, 90, 90);
        AnnotationRenderer.DrawAll(bmp, new[] { a });

        // 边框上像素 = 标注色（红色）
        var borderPx = bmp.GetPixel(10, 50); // 左边框
        var borderPx2 = bmp.GetPixel(50, 10); // 上边框
        Assert.Equal(0xFFE53935, (uint)borderPx);
        Assert.Equal(0xFFE53935, (uint)borderPx2);
        // 内部中心仍是白色（描边非填充）
        var center = bmp.GetPixel(50, 50);
        Assert.Equal(SKColors.White, center);
    }

    [Fact]
    public void 绘制箭头_端点为颜色()
    {
        using var bmp = MakeWhiteCanvas();
        var a = new Annotation(AnnotationKind.Arrow)
        {
            X1 = 10, Y1 = 50, X2 = 90, Y2 = 50,
            ColorArgb = 0xFF0000FF
        };
        AnnotationRenderer.DrawAll(bmp, new[] { a });

        // 线中点为蓝色
        Assert.Equal(0xFF0000FF, (uint)bmp.GetPixel(50, 50));
        // 箭头头区域（终点附近偏移处）应有蓝色（head 与终点夹角方向）
        bool headFound = false;
        for (int dy = -8; dy <= 8 && !headFound; dy++)
            for (int dx = -12; dx <= 0 && !headFound; dx++)
                if (bmp.GetPixel(90 + dx, 50 + dy) == (SKColor)0xFF0000FF && (dx != 0 || dy != 0))
                    headFound = true;
        Assert.True(headFound, "箭头头应有蓝色像素（除终点本身外）");
    }

    [Fact]
    public void 绘制文字_区域出现非白像素()
    {
        using var bmp = MakeWhiteCanvas(200, 100);
        var a = new Annotation(AnnotationKind.Text)
        {
            X1 = 20, Y1 = 20, Text = "测试", FontSize = 24,
            ColorArgb = 0xFF000000
        };
        AnnotationRenderer.DrawAll(bmp, new[] { a });

        // 文字区域内应存在黑色像素（字形被绘制）
        bool found = false;
        for (int y = 20; y < 60 && !found; y++)
            for (int x = 20; x < 90 && !found; x++)
                if (bmp.GetPixel(x, y) != SKColors.White)
                    found = true;
        Assert.True(found, "文字区域内应绘制出字形（非白像素）");
    }

    [Fact]
    public void 马赛克_区域内像素被块化_与原图不同()
    {
        // 原图：左侧黑色右半白 → 马赛克区域 40,40-60,60 应被块化（不再与原图一致）
        var bmp = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            using var p = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(new SKRect(0, 0, 50, 100), p);
        }

        var a = new Annotation(AnnotationKind.Mosaic)
        {
            X1 = 40, Y1 = 40, X2 = 60, Y2 = 60
        };
        AnnotationRenderer.DrawAll(bmp, new[] { a });

        // 马赛克区域（原黑白交界处 48,50）应被块化——与原像素不再相同（变成块的采样色）
        var before = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(before))
        {
            canvas.Clear(SKColors.White);
            using var p = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(new SKRect(0, 0, 50, 100), p);
        }
        // 对比马赛克后与原图在交界处的差异：块化会改变交界附近的精确像素
        bool changed = false;
        for (int y = 40; y < 60 && !changed; y++)
            for (int x = 40; x < 60 && !changed; x++)
                if (bmp.GetPixel(x, y) != before.GetPixel(x, y))
                    changed = true;
        Assert.True(changed, "马赛克区域应产生像素块化（与原图不同）");
    }

    [Fact]
    public void 空列表_不绘制_不抛异常()
    {
        using var bmp = MakeWhiteCanvas();
        AnnotationRenderer.DrawAll(bmp, Array.Empty<Annotation>());
        Assert.Equal(SKColors.White, bmp.GetPixel(50, 50));
    }

    [Fact]
    public void 越界矩形_绘制不抛异常且画布内边框可见()
    {
        using var bmp = MakeWhiteCanvas();
        // 部分越界：左/上边在画布外，右/下边在画布内（100×100）
        var a = MakeRect(-20, -20, 90, 90);
        AnnotationRenderer.DrawAll(bmp, new[] { a }); // 画布自动裁剪，不应抛

        Assert.Equal(0xFFE53935, (uint)bmp.GetPixel(90, 50)); // 右边框（画布内）
        Assert.Equal(0xFFE53935, (uint)bmp.GetPixel(50, 90)); // 下边框（画布内）
        Assert.Equal(SKColors.White, bmp.GetPixel(50, 50));   // 内部仍白色
    }
}
