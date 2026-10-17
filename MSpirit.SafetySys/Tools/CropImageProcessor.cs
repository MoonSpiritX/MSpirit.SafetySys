// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using Serilog;
using SkiaSharp;
using System;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 裁剪图像处理器（纯逻辑，可测）：EXIF 摆正后的尺寸读取 / 解码摆正（可选缩放）/ 像素裁剪。
    /// 预览与裁剪执行共用：预览用降采样（maxDimension），执行用全尺寸 + Subset。
    /// </summary>
    public static class CropImageProcessor
    {
        /// <summary>读取图片摆正后的原始像素尺寸（不解码全图；读取失败返回 null）</summary>
        public static (int Width, int Height)? GetCorrectedSize(string path)
        {
            try
            {
                using var data = SKData.Create(path);
                using var codec = SKCodec.Create(data);
                if (codec == null) return null;
                int w = codec.Info.Width, h = codec.Info.Height;
                if (IsRotatedOrigin(codec.EncodedOrigin)) (w, h) = (h, w);
                return (w, h);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "读取图片摆正尺寸失败：{Path}", path);
                return null;
            }
        }

        /// <summary>解码 + EXIF 摆正 + 可选等比缩放（maxDimension 限制最大边），返回独立位图</summary>
        public static SKBitmap? DecodeCorrected(string path, int? maxDimension = null)
        {
            try
            {
                using var data = SKData.Create(path);
                using var codec = SKCodec.Create(data);
                if (codec == null) return null;

                using var bitmap = SKBitmap.Decode(path);
                if (bitmap == null) return null;

                var corrected = ApplyExifCorrection(bitmap, codec.EncodedOrigin);

                if (maxDimension.HasValue && maxDimension.Value > 0 &&
                    (corrected.Width > maxDimension.Value || corrected.Height > maxDimension.Value))
                {
                    double scale = Math.Min(
                        (double)maxDimension.Value / corrected.Width,
                        (double)maxDimension.Value / corrected.Height);
                    int nw = Math.Max(1, (int)Math.Round(corrected.Width * scale));
                    int nh = Math.Max(1, (int)Math.Round(corrected.Height * scale));
                    // 注意：resized 是返回值，不能 using var（using 会在方法返回前释放它）
                    var resized = corrected.Resize(
                        new SKImageInfo(nw, nh),
                        new SKSamplingOptions(SKCubicResampler.Mitchell));
                    corrected.Dispose();
                    return resized;
                }
                return corrected;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "解码摆正图片失败：{Path}", path);
                return null;
            }
        }

        /// <summary>按像素选区裁剪（独立副本；越界钳制；无效返回 null）</summary>
        public static SKBitmap? Crop(SKBitmap source, int x, int y, int width, int height)
        {
            if (source == null || width <= 0 || height <= 0) return null;

            x = Math.Clamp(x, 0, source.Width - 1);
            y = Math.Clamp(y, 0, source.Height - 1);
            int right = Math.Min(source.Width, x + width);
            int bottom = Math.Min(source.Height, y + height);
            if (right <= x || bottom <= y) return null;

            int w = right - x;
            int h = bottom - y;
            var result = new SKBitmap(w, h);
            using (var canvas = new SKCanvas(result))
            using (var srcImage = SKImage.FromBitmap(source))
            using (var sub = srcImage.Subset(new SKRectI(x, y, right, bottom)))
            {
                canvas.DrawImage(sub, 0, 0); // 显式像素拷贝到独立位图（不共享任何原生内存）
            }
            return result;
        }

        private static bool IsRotatedOrigin(SKEncodedOrigin origin) =>
            origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        /// <summary>按 EXIF 方向摆正（1=TopLeft 正常；6=竖拍右转 90° 等；与方向转换同变换规则）</summary>
        public static SKBitmap ApplyExifCorrection(SKBitmap source, SKEncodedOrigin origin)
        {
            if (origin == SKEncodedOrigin.TopLeft || source == null)
            {
                // 显式像素拷贝为独立位图（避免共享原生内存导致源释放后悬空）
                var copy = new SKBitmap(source.Width, source.Height);
                using var canvas = new SKCanvas(copy);
                canvas.DrawBitmap(source, 0, 0);
                return copy;
            }

            var (angle, flipH, flipV) = origin switch
            {
                SKEncodedOrigin.TopRight => (0f, true, false),
                SKEncodedOrigin.BottomRight => (180f, false, false),
                SKEncodedOrigin.BottomLeft => (0f, false, true),
                SKEncodedOrigin.LeftTop => (90f, true, false),
                SKEncodedOrigin.RightTop => (90f, false, false),
                SKEncodedOrigin.RightBottom => (270f, true, false),
                SKEncodedOrigin.LeftBottom => (270f, false, false),
                _ => (0f, false, false)
            };

            int w = angle % 180 == 0 ? source.Width : source.Height;
            int h = angle % 180 == 0 ? source.Height : source.Width;
            var result = new SKBitmap(w, h);
            using var canvas2 = new SKCanvas(result);
            canvas2.Translate(w / 2f, h / 2f);
            canvas2.RotateDegrees(angle);
            if (flipH || flipV)
                canvas2.Scale(flipH ? -1 : 1, flipV ? -1 : 1);
            canvas2.DrawBitmap(source, -source.Width / 2f, -source.Height / 2f);
            return result;
        }
    }
}
