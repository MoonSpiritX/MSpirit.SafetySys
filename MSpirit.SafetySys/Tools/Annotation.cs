// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using SkiaSharp;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>标注类型（六种，2026-08-16 方案定稿）</summary>
    public enum AnnotationKind
    {
        Rectangle,  // 矩形
        Oval,       // 圆形
        Line,       // 直线
        Arrow,      // 箭头
        Text,       // 文字
        Mosaic      // 马赛克（矩形区域像素化）
    }

    /// <summary>
    /// 标注对象（纯数据 + 命中测试，不依赖 UI；2026-08-16 新增）。
    /// 坐标域：**原始图片像素坐标**（摆正后全尺寸图）——缩放/平移只是显示层变换，
    /// 对象数据恒定，保存时按全尺寸一次重绘（矢量方案 X，见 开发-标注与剪裁工具.md 3.3）。
    /// 语义：Rectangle/Oval/Mosaic = 对角两点；Line/Arrow = 起终点；Text = 锚点(X1,Y1)+文本。
    /// </summary>
    public sealed class Annotation
    {
        public Annotation(AnnotationKind kind) => Kind = kind;

        public AnnotationKind Kind { get; }
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }

        /// <summary>颜色（ARGB）</summary>
        public uint ColorArgb { get; set; } = 0xFFE53935;

        /// <summary>线宽（像素）</summary>
        public float StrokeWidth { get; set; } = 3f;

        /// <summary>文字内容（仅 Text 用）</summary>
        public string Text { get; set; } = "";

        /// <summary>字号（像素，仅 Text 用）</summary>
        public float FontSize { get; set; } = 28f;

        /// <summary>归一化对角矩形（X1Y1X2Y2 任意顺序 → 左上/右下），马赛克/矩形/圆形/文字包围盒用</summary>
        public SKRect NormalizedRect
        {
            get
            {
                float l = Math.Min(X1, X2), t = Math.Min(Y1, Y2);
                float r = Math.Max(X1, X2), b = Math.Max(Y1, Y2);
                return new SKRect(l, t, r, b);
            }
        }

        /// <summary>命中测试（像素坐标，tolerance=命中容差 px）：选中/移动用</summary>
        public bool HitTest(float px, float py, float tolerance)
        {
            return Kind switch
            {
                AnnotationKind.Line or AnnotationKind.Arrow => DistToSegment(px, py) <= tolerance,
                AnnotationKind.Text => HitText(px, py, tolerance),
                _ => ContainsWithTolerance(px, py, tolerance) // Rectangle/Oval/Mosaic：包围盒内（含容差）
            };
        }

        private bool ContainsWithTolerance(float px, float py, float tol)
        {
            var r = NormalizedRect;
            return px >= r.Left - tol && px <= r.Right + tol && py >= r.Top - tol && py <= r.Bottom + tol;
        }

        private bool HitText(float px, float py, float tol)
        {
            var r = TextBounds;
            return px >= r.Left - tol && px <= r.Right + tol && py >= r.Top - tol && py <= r.Bottom + tol;
        }

        /// <summary>文字近似包围盒（宽=字号×字符数×0.55，高=字号×1.3；精确宽度由渲染时测量）</summary>
        public SKRect TextBounds
        {
            get
            {
                float w = FontSize * Math.Max(1, Text.Length) * 0.55f;
                float h = FontSize * 1.3f;
                return new SKRect(X1, Y1, X1 + w, Y1 + h);
            }
        }

        /// <summary>点到线段距离（Line/Arrow 命中）</summary>
        private float DistToSegment(float px, float py)
        {
            float dx = X2 - X1, dy = Y2 - Y1;
            float lenSq = dx * dx + dy * dy;
            if (lenSq <= 0.0001f) return (float)Math.Sqrt((px - X1) * (px - X1) + (py - Y1) * (py - Y1));
            float t = ((px - X1) * dx + (py - Y1) * dy) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float cx = X1 + t * dx, cy = Y1 + t * dy;
            return (float)Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        /// <summary>整体平移（像素坐标增量；移动标注用）</summary>
        public void Move(float dx, float dy)
        {
            X1 += dx; Y1 += dy; X2 += dx; Y2 += dy;
        }
    }
}
