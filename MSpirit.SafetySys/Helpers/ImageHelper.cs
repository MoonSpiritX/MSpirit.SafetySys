// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.VisualBasic.FileIO;
using Serilog;
using SkiaSharp;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MS_SafetyManagementSystem.Helpers
{
    public static class ImageHelper
    {
        public static async Task<(string originalPath, string thumbPath)> SaveImageToRecordAsync(
            Stream sourceStream,
            string recordId,
            string originalFileName,
            string attachmentsRootPath,
            string projectId,
            string module,
            string yearMonth,
            string attachmentType,
            int displayOrder)
        {
            // 读取全部字节（用于判断格式和多次使用）
            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                await sourceStream.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            // 检测原始图片格式
            SKEncodedImageFormat originalFormat;
            using (var ms = new MemoryStream(imageBytes))
            using (var codec = SKCodec.Create(ms))
            {
                originalFormat = codec?.EncodedFormat ?? SKEncodedImageFormat.Jpeg;
            }

            // 构建目录结构
            string projectFolder = $"Project_{projectId}";
            string moduleYearMonth = $"{module}_{yearMonth}";
            string cardFolder = $"Card_{recordId}";
            string folderPath = Path.Combine(attachmentsRootPath, projectFolder, moduleYearMonth, cardFolder);
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string guidPart = Guid.NewGuid().ToString("N");
            string baseFileName = $"{attachmentType}-{displayOrder}_{guidPart}";
            string originalFileNameOnly;
            bool isOriginalJpeg = (originalFormat == SKEncodedImageFormat.Jpeg);

            // 处理原图（不变）
            if (isOriginalJpeg)
            {
                originalFileNameOnly = $"{baseFileName}.jpg";
                string originalFullPath = Path.Combine(folderPath, originalFileNameOnly);
                await File.WriteAllBytesAsync(originalFullPath, imageBytes);
            }
            else
            {
                originalFileNameOnly = $"{baseFileName}.jpg";
                string originalFullPath = Path.Combine(folderPath, originalFileNameOnly);
                using (var ms = new MemoryStream(imageBytes))
                using (var originalBitmap = SKBitmap.Decode(ms))
                {
                    SKBitmap targetBitmap;
                    if (originalBitmap.AlphaType != SKAlphaType.Opaque)
                    {
                        targetBitmap = new SKBitmap(originalBitmap.Width, originalBitmap.Height);
                        using (var canvas = new SKCanvas(targetBitmap))
                        {
                            canvas.Clear(SKColors.White);
                            canvas.DrawBitmap(originalBitmap, 0, 0);
                            canvas.Flush();
                        }
                    }
                    else
                    {
                        targetBitmap = originalBitmap;
                    }

                    using (var image = SKImage.FromBitmap(targetBitmap))
                    using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
                    using (var fs = File.Create(originalFullPath))
                    {
                        data.SaveTo(fs);
                    }

                    if (targetBitmap != originalBitmap)
                        targetBitmap.Dispose();
                }
            }

            // ========== 生成缩略图（支持透明背景填充白色） ==========
            string thumbFileNameOnly = $"{baseFileName}_thumb.jpg";
            string thumbFolderPath = Path.Combine(folderPath, "thumb");
            if (!Directory.Exists(thumbFolderPath))
                Directory.CreateDirectory(thumbFolderPath);
            string thumbFullPath = Path.Combine(thumbFolderPath, thumbFileNameOnly);

            using (var ms = new MemoryStream(imageBytes))
            {
                // 解码原始图片
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];

                // 计算缩放尺寸（与原逻辑完全一致）
                int newWidth, newHeight;
                if (frame.PixelWidth >= frame.PixelHeight)
                {
                    newWidth = 600;
                    newHeight = (int)(frame.PixelHeight * (600.0 / frame.PixelWidth));
                }
                else
                {
                    newHeight = 450;
                    newWidth = (int)(frame.PixelWidth * (450.0 / frame.PixelHeight));
                }

                // 1. 使用 Fant 模式缩放图像
                var scaleTransform = new ScaleTransform(
                    (double)newWidth / frame.PixelWidth,
                    (double)newHeight / frame.PixelHeight
                );
                var scaledBitmap = new TransformedBitmap(frame, scaleTransform);
                RenderOptions.SetBitmapScalingMode(scaledBitmap, BitmapScalingMode.Fant);

                // 2. 创建带白色背景的目标位图（尺寸与缩放后相同）
                var targetBitmap = new RenderTargetBitmap(newWidth, newHeight, 96, 96, PixelFormats.Pbgra32);
                var drawingVisual = new DrawingVisual();

                using (var dc = drawingVisual.RenderOpen())
                {
                    // 绘制白色背景（覆盖透明区域）—— 明确使用 System.Windows.Media.Brushes
                    dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, newWidth, newHeight));
                    // 绘制缩放后的图像（透明部分会被背景色替代）
                    dc.DrawImage(scaledBitmap, new Rect(0, 0, newWidth, newHeight));
                }

                targetBitmap.Render(drawingVisual);

                // 3. 编码为 JPEG 并保存
                var encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 80;
                encoder.Frames.Add(BitmapFrame.Create(targetBitmap));

                using (var fs = File.Create(thumbFullPath))
                {
                    encoder.Save(fs);
                }
            }

            string relativeOriginal = Path.Combine(projectFolder, moduleYearMonth, cardFolder, originalFileNameOnly);
            string relativeThumb = Path.Combine(projectFolder, moduleYearMonth, cardFolder, "thumb", thumbFileNameOnly);
            return (relativeOriginal, relativeThumb);
        }

        /// <summary>
        /// 生成图片到临时缓存目录（不更新数据库），返回原图和缩略图的完整物理路径。
        /// </summary>
        public static async Task<(string originalFullPath, string thumbFullPath)> SaveImageToCacheAsync(
            Stream sourceStream,
            string recordId,
            string originalFileName,
            string cacheRootPath,
            string attachmentType,
            int displayOrder)
        {
            // 构建缓存目录：cacheRootPath/recordId/
            string cacheFolder = Path.Combine(cacheRootPath, recordId);
            if (!Directory.Exists(cacheFolder))
                Directory.CreateDirectory(cacheFolder);

            // 生成唯一文件名
            string guidPart = Guid.NewGuid().ToString("N");
            string baseFileName = $"{attachmentType}_{guidPart}";
            string originalFileNameOnly = $"{baseFileName}.webp";
            string thumbFileNameOnly = $"{baseFileName}_thumb.webp";

            string originalFullPath = Path.Combine(cacheFolder, originalFileNameOnly);
            string thumbFullPath = Path.Combine(cacheFolder, thumbFileNameOnly);

            // 读取原始图片到字节数组
            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                await sourceStream.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            // 生成缩略图
            using (var ms = new MemoryStream(imageBytes))
            using (var skBitmap = SKBitmap.Decode(ms))
            {
                if (skBitmap == null)
                    throw new InvalidOperationException("无法解码图片");

                float srcRatio = (float)skBitmap.Width / skBitmap.Height;
                float destRatio = 300f / 225f;
                SKRect cropRect;
                if (srcRatio > destRatio)
                {
                    int newWidth = (int)(skBitmap.Height * destRatio);
                    int cropX = (skBitmap.Width - newWidth) / 2;
                    cropRect = new SKRect(cropX, 0, cropX + newWidth, skBitmap.Height);
                }
                else
                {
                    int newHeight = (int)(skBitmap.Width / destRatio);
                    int cropY = (skBitmap.Height - newHeight) / 2;
                    cropRect = new SKRect(0, cropY, skBitmap.Width, cropY + newHeight);
                }

                using var croppedBitmap = new SKBitmap(300, 225);
                using (var canvas = new SKCanvas(croppedBitmap))
                {
                    canvas.DrawBitmap(skBitmap, cropRect, new SKRect(0, 0, 300, 225));
                }
                using var thumbImage = SKImage.FromBitmap(croppedBitmap);
                using var thumbData = thumbImage.Encode(SKEncodedImageFormat.Webp, 80);
                using var thumbFs = File.Create(thumbFullPath);
                thumbData.SaveTo(thumbFs);
            }

            // 生成原图
            using (var ms = new MemoryStream(imageBytes))
            using (var skBitmap = SKBitmap.Decode(ms))
            {
                using var image = SKImage.FromBitmap(skBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Webp, 85);
                using var fs = File.Create(originalFullPath);
                data.SaveTo(fs);
            }

            return (originalFullPath, thumbFullPath);
        }

        /// <summary>
        /// 加载缩略图（从文件系统）
        /// </summary>
        public static async Task<System.Windows.Media.ImageSource?> LoadThumbnailAsync(string fullPath, int maxWidth = 300, int maxHeight = 300)
        {
            if (!File.Exists(fullPath))
                return null;

            using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = memoryStream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }

        /// <summary>
        /// 删除文件到回收站；若回收站不可用（如被组策略禁用）则回退为直接删除
        /// </summary>
        public static void DeleteToRecycleBin(string path)
        {
            try
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "移入回收站失败，改为直接删除: {Path}", path);
                try { File.Delete(path); } catch { /* 忽略 */ }
            }
        }
    }
}
