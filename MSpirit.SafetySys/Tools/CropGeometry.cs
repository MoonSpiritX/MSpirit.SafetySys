// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 裁剪坐标换算（纯静态，可测）：Image Uniform 显示 ↔ 图片像素坐标。
    /// 预览为降采样显示，裁剪基于原始（摆正后）像素——显示选区 × 缩放因子映射回像素。
    /// </summary>
    public static class CropGeometry
    {
        /// <summary>像素选区（裁剪执行用，可直接转 SKRectI(left, top, right, bottom)）</summary>
        public readonly record struct PixelRect(int X, int Y, int Width, int Height);

        /// <summary>
        /// 计算 Uniform 显示变换：图片在可用区域内等比缩放后的缩放因子与偏移（居中）。
        /// 返回 (scale, offsetX, offsetY)。
        /// </summary>
        public static (double Scale, double OffsetX, double OffsetY) ComputeUniformTransform(
            double availWidth, double availHeight, double imageWidth, double imageHeight)
        {
            if (imageWidth <= 0 || imageHeight <= 0 || availWidth <= 0 || availHeight <= 0)
                return (1, 0, 0);
            double scale = Math.Min(availWidth / imageWidth, availHeight / imageHeight);
            double offsetX = (availWidth - imageWidth * scale) / 2;
            double offsetY = (availHeight - imageHeight * scale) / 2;
            return (scale, offsetX, offsetY);
        }

        /// <summary>显示坐标 → 图片像素坐标（反变换，含比例因子换算；未做图片边界钳制）</summary>
        public static (double X, double Y) DisplayToImage(double displayX, double displayY,
            double scale, double offsetX, double offsetY)
        {
            return ((displayX - offsetX) / scale, (displayY - offsetY) / scale);
        }

        /// <summary>显示选区 → 像素选区（取整 + 钳制到图片范围，保证不越界）</summary>
        public static PixelRect DisplayRectToPixelRect(
            double displayX, double displayY, double displayWidth, double displayHeight,
            double scale, double offsetX, double offsetY, int imageWidth, int imageHeight)
        {
            if (scale <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return new PixelRect(0, 0, 0, 0);

            double px = (displayX - offsetX) / scale;
            double py = (displayY - offsetY) / scale;
            double pw = displayWidth / scale;
            double ph = displayHeight / scale;

            int x = Math.Max(0, (int)Math.Floor(px));
            int y = Math.Max(0, (int)Math.Floor(py));
            int right = Math.Min(imageWidth, (int)Math.Ceiling(px + pw));
            int bottom = Math.Min(imageHeight, (int)Math.Ceiling(py + ph));
            int w = Math.Max(0, right - x);
            int h = Math.Max(0, bottom - y);
            return new PixelRect(x, y, w, h);
        }

        /// <summary>
        /// 裁剪框选区（CropRegion 坐标域：**相对图片显示区域左上角，不含 offset**）→ 像素选区。
        /// 2026-08-16 修复点：CropOverlay.GetPixelSelection 原直接调 <see cref="DisplayRectToPixelRect"/> 并传入
        /// region 坐标，而该函数期望"控件坐标"（含 offset）——region 坐标被二次减去 offset，当图片在预览区
        /// 非满幅（Stretch=Uniform 留白，offsetX/offsetY&gt;0）时像素选区整体向左上偏移 offset/scale 像素，
        /// 裁剪结果与框选位置不一致。本方法把"加 offset"收进函数内，坐标域由签名强制，调用方不可能再传错。
        /// </summary>
        public static PixelRect RegionRectToPixelRect(
            double regionX, double regionY, double regionWidth, double regionHeight,
            double scale, double offsetX, double offsetY, int imageWidth, int imageHeight)
        {
            return DisplayRectToPixelRect(
                regionX + offsetX, regionY + offsetY, regionWidth, regionHeight,
                scale, offsetX, offsetY, imageWidth, imageHeight);
        }

        /// <summary>判断显示坐标是否落在图片显示区域内</summary>
        public static bool IsInsideImage(double x, double y, double scale, double offsetX, double offsetY,
            double imageWidth, double imageHeight)
        {
            double right = offsetX + imageWidth * scale;
            double bottom = offsetY + imageHeight * scale;
            return x >= offsetX && x <= right && y >= offsetY && y <= bottom;
        }
    }
}
