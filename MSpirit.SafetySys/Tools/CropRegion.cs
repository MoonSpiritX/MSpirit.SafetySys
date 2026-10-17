// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>裁剪框手柄（4 角 + 4 边 + 无）</summary>
    public enum CropHandle
    {
        None,
        TopLeft, Top, TopRight,
        Right,
        BottomRight, Bottom, BottomLeft,
        Left
    }

    /// <summary>
    /// 裁剪选区状态机（显示坐标域，相对图片显示区域）：移动/缩放/比例约束/边界钳制/最小尺寸/默认选区。
    /// 纯逻辑、无 UI 依赖，可单元测试；裁剪框控件把鼠标事件翻译为对它的调用。
    /// </summary>
    public class CropRegion
    {
        /// <summary>最小选区尺寸（显示坐标 px），防手滑裁出空图</summary>
        public const double MinSize = 8;

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Width { get; private set; }
        public double Height { get; private set; }

        /// <summary>宽高比约束（宽/高）；null = 自由比例</summary>
        public double? AspectRatio { get; private set; }

        /// <summary>设置比例模式（null = 自由；传比例时按比例重置默认选区）</summary>
        public void SetAspectRatio(double? ratio, double boundsW, double boundsH)
        {
            AspectRatio = ratio;
            Reset(boundsW, boundsH);
        }

        /// <summary>重置为默认选区：自由 = 全图；固定比例 = 满足比例的最大中心区域</summary>
        public void Reset(double boundsW, double boundsH)
        {
            if (boundsW <= 0 || boundsH <= 0)
            {
                X = Y = Width = Height = 0;
                return;
            }
            if (!AspectRatio.HasValue)
            {
                X = 0; Y = 0; Width = boundsW; Height = boundsH;
                return;
            }
            double ratio = AspectRatio.Value;
            double w, h;
            if (boundsW / boundsH > ratio) { h = boundsH; w = h * ratio; }
            else { w = boundsW; h = w / ratio; }
            X = (boundsW - w) / 2;
            Y = (boundsH - h) / 2;
            Width = w;
            Height = h;
        }

        /// <summary>整体移动（边界钳制）</summary>
        public void Move(double dx, double dy, double boundsW, double boundsH)
        {
            if (boundsW <= 0 || boundsH <= 0) return;
            double nx = Math.Clamp(X + dx, 0, Math.Max(0, boundsW - Width));
            double ny = Math.Clamp(Y + dy, 0, Math.Max(0, boundsH - Height));
            X = nx;
            Y = ny;
        }

        /// <summary>
        /// 按手柄用指针绝对位置（图片显示坐标系）调整选区：锚定边不动，拖动边跟随指针，
        /// 最小尺寸 + 边界钳制 + 比例约束。
        /// 2026-08-16 修复：固定比例下拖"边"只能向内缩小、不能向外扩大——原实现比例约束只有
        /// "收缩主导维"分支（超比例就把被拖维缩回原值），向外拖时宽度被缩回 h×ratio 而纹丝不动。
        /// 现改为"按被拖边决定主导维，另一维按比例联动"：拖右边 → 宽=指针-左，高=宽/ratio（top 锚定）；
        /// 联动出的另一维越界时反向收缩被拖维，保证比例与边界同时成立。
        /// </summary>
        public void ResizeTo(CropHandle handle, double pointerX, double pointerY, double boundsW, double boundsH)
        {
            if (boundsW <= 0 || boundsH <= 0 || handle == CropHandle.None) return;

            double left = X, top = Y, right = X + Width, bottom = Y + Height;

            if (!AspectRatio.HasValue)
            {
                // 自由比例：锚定边不动，被拖边跟随指针；最小尺寸 + 边界钳制
                double newL = left, newT = top, newR = right, newB = bottom;
                if (handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft) newL = pointerX;
                if (handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight) newR = pointerX;
                if (handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight) newT = pointerY;
                if (handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight) newB = pointerY;

                newL = Math.Clamp(newL, 0, Math.Max(0, right - MinSize));
                newR = Math.Clamp(newR, Math.Min(boundsW, left + MinSize), boundsW);
                newT = Math.Clamp(newT, 0, Math.Max(0, bottom - MinSize));
                newB = Math.Clamp(newB, Math.Min(boundsH, top + MinSize), boundsH);

                X = newL; Y = newT; Width = newR - newL; Height = newB - newT;
                return;
            }

            // ── 固定比例：按被拖边决定主导维，另一维联动（锚定边固定） ──
            double ratio = AspectRatio.Value;
            double w, h;

            switch (handle)
            {
                case CropHandle.Right:
                    // 锚定左+上：宽=指针-左，高=宽/ratio（top 固定，bottom 随动）；高越界→反向收缩宽
                    w = Math.Clamp(pointerX - left, MinSize, boundsW - left);
                    h = w / ratio;
                    if (top + h > boundsH) { h = boundsH - top; w = h * ratio; }
                    X = left; Y = top;
                    break;
                case CropHandle.Left:
                    // 锚定右+上：宽=右-指针，高=宽/ratio（top 固定）
                    w = Math.Clamp(right - pointerX, MinSize, right);
                    h = w / ratio;
                    if (top + h > boundsH) { h = boundsH - top; w = h * ratio; }
                    X = right - w; Y = top;
                    break;
                case CropHandle.Bottom:
                    // 锚定左+上：高=指针-上，宽=高×ratio（left 固定）
                    h = Math.Clamp(pointerY - top, MinSize, boundsH - top);
                    w = h * ratio;
                    if (left + w > boundsW) { w = boundsW - left; h = w / ratio; }
                    X = left; Y = top;
                    break;
                case CropHandle.Top:
                    // 锚定左+下：高=下-指针，宽=高×ratio（left 固定）
                    h = Math.Clamp(bottom - pointerY, MinSize, bottom);
                    w = h * ratio;
                    if (left + w > boundsW) { w = boundsW - left; h = w / ratio; }
                    X = left; Y = bottom - h;
                    break;
                case CropHandle.TopLeft:
                    w = Math.Clamp(right - pointerX, MinSize, right);
                    h = Math.Clamp(bottom - pointerY, MinSize, bottom);
                    ApplyCornerRatio(ref w, ref h, ratio);
                    X = right - w; Y = bottom - h; // 锚定右下
                    break;
                case CropHandle.TopRight:
                    w = Math.Clamp(pointerX - left, MinSize, boundsW - left);
                    h = Math.Clamp(bottom - pointerY, MinSize, bottom);
                    ApplyCornerRatio(ref w, ref h, ratio);
                    X = left; Y = bottom - h; // 锚定左下
                    break;
                case CropHandle.BottomLeft:
                    w = Math.Clamp(right - pointerX, MinSize, right);
                    h = Math.Clamp(pointerY - top, MinSize, boundsH - top);
                    ApplyCornerRatio(ref w, ref h, ratio);
                    X = right - w; Y = top; // 锚定右上
                    break;
                default: // BottomRight
                    w = Math.Clamp(pointerX - left, MinSize, boundsW - left);
                    h = Math.Clamp(pointerY - top, MinSize, boundsH - top);
                    ApplyCornerRatio(ref w, ref h, ratio);
                    X = left; Y = top; // 锚定左上
                    break;
            }

            Width = w;
            Height = h;
        }

        /// <summary>角手柄比例对齐：主导维（较"超出比例"的一维）锚定，另一维按比例联动</summary>
        private static void ApplyCornerRatio(ref double w, ref double h, double ratio)
        {
            if (w <= 0 || h <= 0) return;
            if (w / h > ratio) w = h * ratio;  // 太宽 → 宽收窄（高不变）
            else h = w / ratio;                // 太高 → 高收窄（宽不变）
        }
    }
}
