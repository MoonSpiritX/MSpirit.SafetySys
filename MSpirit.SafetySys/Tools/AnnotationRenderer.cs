// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 标注渲染器（纯逻辑、可测，2026-08-16 新增）：把标注对象绘制到**全尺寸摆正位图**上。
    /// 供「应用」执行链路使用：解码全尺寸 → DrawAll 重绘标注 → 裁剪选区 → 输出。
    /// 六类型：矩形/圆形/直线/箭头/文字/马赛克（区域降采样放大 = 像素化）。
    /// </summary>
    public static class AnnotationRenderer
    {
        /// <summary>把全部标注绘制到目标位图上（就地绘制，越界部分由画布自动裁剪）</summary>
        public static void DrawAll(SKBitmap target, IReadOnlyList<Annotation> annotations)
        {
            if (target == null || annotations == null || annotations.Count == 0) return;
            using var canvas = new SKCanvas(target);
            foreach (var a in annotations)
            {
                if (a == null) continue;
                if (a.Kind == AnnotationKind.Mosaic)
                    DrawMosaic(canvas, target, a);
                else
                    DrawVector(canvas, a);
            }
        }

        /// <summary>矢量类标注（矩形/圆形/直线/箭头/文字）</summary>
        private static void DrawVector(SKCanvas canvas, Annotation a)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, a.StrokeWidth),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                Color = new SKColor(a.ColorArgb)
            };

            switch (a.Kind)
            {
                case AnnotationKind.Rectangle:
                    canvas.DrawRect(a.NormalizedRect, paint);
                    break;
                case AnnotationKind.Oval:
                    canvas.DrawOval(a.NormalizedRect, paint);
                    break;
                case AnnotationKind.Line:
                    canvas.DrawLine(a.X1, a.Y1, a.X2, a.Y2, paint);
                    break;
                case AnnotationKind.Arrow:
                    DrawArrow(canvas, a, paint);
                    break;
                case AnnotationKind.Text:
                    DrawText(canvas, a);
                    break;
            }
        }

        /// <summary>箭头：直线 + 按角度计算的箭头头（两段短线）</summary>
        private static void DrawArrow(SKCanvas canvas, Annotation a, SKPaint paint)
        {
            canvas.DrawLine(a.X1, a.Y1, a.X2, a.Y2, paint);

            double angle = Math.Atan2(a.Y2 - a.Y1, a.X2 - a.X1);
            float headLen = Math.Max(12, a.StrokeWidth * 4);
            const double spread = Math.PI / 6; // 30°

            float hx1 = a.X2 - headLen * (float)Math.Cos(angle - spread);
            float hy1 = a.Y2 - headLen * (float)Math.Sin(angle - spread);
            float hx2 = a.X2 - headLen * (float)Math.Cos(angle + spread);
            float hy2 = a.Y2 - headLen * (float)Math.Sin(angle + spread);

            using var head = new SKPath();
            head.MoveTo(a.X2, a.Y2);
            head.LineTo(hx1, hy1);
            head.MoveTo(a.X2, a.Y2);
            head.LineTo(hx2, hy2);
            canvas.DrawPath(head, paint);
        }

        /// <summary>文字：中文字体（系统字体兜底 Microsoft YaHei / SimSun / 默认）</summary>
        private static void DrawText(SKCanvas canvas, Annotation a)
        {
            if (string.IsNullOrEmpty(a.Text)) return;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(a.ColorArgb),
                TextSize = Math.Max(8, a.FontSize)
            };
            var typeface = CreateCjkTypeface();
            if (typeface != null) paint.Typeface = typeface;

            // DrawText 的 y 是基线；用 FontMetrics 把 (X1,Y1) 当左上角
            float baseline = a.Y1 + Math.Abs(paint.FontMetrics.Ascent);
            canvas.DrawText(a.Text, a.X1, baseline, paint);
            typeface?.Dispose();
        }

        /// <summary>尝试加载中文系统字体（微信同款效果；失败返回 null 用默认字体）</summary>
        private static SKTypeface? CreateCjkTypeface()
        {
            foreach (var name in new[] { "Microsoft YaHei", "SimSun", "PingFang SC", "Noto Sans CJK SC" })
            {
                var tf = SKTypeface.FromFamilyName(name);
                if (tf != null && tf.FamilyName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return tf;
                tf?.Dispose();
            }
            return null;
        }

        /// <summary>马赛克：矩形区域降采样再放大回原尺寸（块状像素 = 像素化效果）</summary>
        private static void DrawMosaic(SKCanvas canvas, SKBitmap source, Annotation a)
        {
            var rect = a.NormalizedRect;
            int l = Math.Clamp((int)Math.Floor(rect.Left), 0, source.Width - 1);
            int t = Math.Clamp((int)Math.Floor(rect.Top), 0, source.Height - 1);
            int r = Math.Clamp((int)Math.Ceiling(rect.Right), l + 1, source.Width);
            int b = Math.Clamp((int)Math.Ceiling(rect.Bottom), t + 1, source.Height);
            int w = r - l, h = b - t;
            if (w <= 1 || h <= 1) return;

            try
            {
                using var srcImg = SKImage.FromBitmap(source);
                using var sub = srcImg.Subset(new SKRectI(l, t, r, b));
                if (sub == null) return;

                // SKImage 无 Resize（SKBitmap 才有）：Subset → 转 SKBitmap → 降采样/放大
                using var subBmp = SKBitmap.FromImage(sub);
                if (subBmp == null) return;

                // 块尺寸：约 1/12 边长（最小 1px），降采样 → 放大回原尺寸（NearestNeighbor 产生块状）
                int bw = Math.Max(1, w / 12);
                int bh = Math.Max(1, h / 12);
                using var small = subBmp.Resize(new SKImageInfo(bw, bh), new SKSamplingOptions(SKFilterMode.Nearest));
                using var big = small.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Nearest));
                if (big == null) return;

                using var mosaicImg = SKImage.FromBitmap(big);
                canvas.DrawImage(mosaicImg, l, t);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "标注马赛克渲染失败（跳过该区域）");
            }
        }
    }
}
