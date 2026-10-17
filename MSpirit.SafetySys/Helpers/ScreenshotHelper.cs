// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MS_SafetyManagementSystem.Views;
using System;
using System.IO;

namespace MS_SafetyManagementSystem.Helpers
{
    public static class ScreenshotHelper
    {
        public static bool CaptureToFile(UIElement element, string defaultFileName, ref string? lastExportDirectory,
            int padding = 0, bool autoCrop = true, Action? onBeforeRender = null, Action? onAfterRender = null)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));

            var saveDialog = new SaveFileDialog
            {
                Title = "保存截图",
                Filter = "PNG 图片|*.png",
                FileName = defaultFileName,
                InitialDirectory = lastExportDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (saveDialog.ShowDialog() != true) return false;

            string? selectedDir = Path.GetDirectoryName(saveDialog.FileName);
            if (!string.IsNullOrEmpty(selectedDir)) lastExportDirectory = selectedDir;

            onBeforeRender?.Invoke();

            try
            {
                element.UpdateLayout();
                double width = element.RenderSize.Width;
                double height = element.RenderSize.Height;
                if (width <= 0 || height <= 0)
                {
                    if (element is FrameworkElement fe)
                    {
                        width = fe.ActualWidth;
                        height = fe.ActualHeight;
                    }
                }
                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException("元素尺寸无效，无法截图。");

                // 渲染原始图像
                var renderBitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                renderBitmap.Render(element);

                BitmapSource finalBitmap = renderBitmap;

                // 自动裁剪透明边缘
                if (autoCrop)
                {
                    finalBitmap = CropTransparentEdges(finalBitmap);
                }

                // 扩展透明边距
                if (padding > 0)
                {
                    finalBitmap = AddPadding(finalBitmap, padding);
                }

                // 保存
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(finalBitmap));
                using var fs = new FileStream(saveDialog.FileName, FileMode.Create);
                encoder.Save(fs);

                return true;
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"截图失败: {ex.Message}", "错误");
                return false;
            }
            finally
            {
                onAfterRender?.Invoke();
            }
        }

        /// <summary>
        /// 裁剪图像四周的透明边缘（Alpha == 0 的像素）
        /// </summary>
        private static BitmapSource CropTransparentEdges(BitmapSource source)
        {
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            if (width == 0 || height == 0) return source;

            // 获取像素数据
            int stride = (width * source.Format.BitsPerPixel + 7) / 8;
            byte[] pixels = new byte[height * stride];
            source.CopyPixels(pixels, stride, 0);

            int left = width, right = -1, top = height, bottom = -1;

            // 遍历像素，找到非透明区域边界（Alpha > 0）
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int byteIndex = y * stride + x * 4 + 3; // 假设 BGRA 格式，Alpha 在第 4 字节
                    if (byteIndex < pixels.Length && pixels[byteIndex] > 0)
                    {
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
                }
            }

            // 如果没有找到任何非透明像素，返回原图
            if (left > right || top > bottom) return source;

            int cropWidth = right - left + 1;
            int cropHeight = bottom - top + 1;
            if (cropWidth <= 0 || cropHeight <= 0) return source;

            // 裁剪
            var cropped = new CroppedBitmap(source, new Int32Rect(left, top, cropWidth, cropHeight));
            return cropped;
        }

        /// <summary>
        /// 在图像四周添加透明边距
        /// </summary>
        private static BitmapSource AddPadding(BitmapSource source, int padding)
        {
            int newWidth = source.PixelWidth + padding * 2;
            int newHeight = source.PixelHeight + padding * 2;
            var paddedBitmap = new RenderTargetBitmap(newWidth, newHeight, 96, 96, PixelFormats.Pbgra32);

            var drawingVisual = new DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, newWidth, newHeight));
                dc.DrawImage(source, new Rect(padding, padding, source.PixelWidth, source.PixelHeight));
            }
            paddedBitmap.Render(drawingVisual);
            return paddedBitmap;
        }
    }
}