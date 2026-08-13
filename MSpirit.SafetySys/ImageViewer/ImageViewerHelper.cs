// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;   // PresentationSource.FromVisual

namespace MS_SafetyManagementSystem.ImageViewer
{
    /// <summary>
    /// 图片查看器辅助类：格式判断、模块图片索引构建、屏幕工作区获取。
    /// 2026-07-02 清理：缩略图解码已移至 ImageViewerThumbnailPanel.LoadThumbnailDirectAsync，
    ///           本类不再持有任何 SkiaSharp 解码方法。
    /// </summary>
    internal static class ImageViewerHelper
    {
        /// <summary>
        /// 支持的图片扩展名
        /// </summary>
        public static readonly string[] SupportedExtensions = {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp",
            ".tiff", ".webp", ".avif", ".jfif", ".tif"
        };

        /// <summary>
        /// 判断文件是否为支持的图片格式
        /// </summary>
        public static bool IsSupportedImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            foreach (var supported in SupportedExtensions)
            {
                if (ext == supported) return true;
            }
            return false;
        }

        /// <summary>
        /// 按业务分组构建跨记录卡的图片索引列表。
        /// 按 recordIds 顺序遍历各 Card_ 文件夹，收集所有原图（跳过 _thumb 缩略图），
        /// 每张卡内按文件名排序，返回完整列表及当前图片在其中的索引。
        /// </summary>
        /// <param name="baseDir">模块级目录，如 {root}/Project_{id}/Hazard_2026-06</param>
        /// <param name="recordIds">已排序的记录 ID 列表（按业务顺序）</param>
        /// <param name="currentFullPath">当前点击图片的完整路径，用于定位 startIndex</param>
        /// <returns>(全部图片路径列表, 当前图片在列表中的索引，-1 表示未找到)</returns>
        public static (List<string> AllImages, int CurrentIndex) BuildModuleImageIndex(
            string baseDir, List<string> recordIds, string currentFullPath)
        {
            var allImages = new List<string>();

            foreach (var recordId in recordIds)
            {
                string cardDir = Path.Combine(baseDir, $"Card_{recordId}");
                if (!Directory.Exists(cardDir)) continue;

                // GetFiles 不递归，天然不会扫到 thumb/ 子目录；
                // _thumb 后缀检查为双重保险，防止未来目录结构变化
                var images = Directory.GetFiles(cardDir)
                    .Where(f => IsSupportedImage(f) && !Path.GetFileName(f).Contains("_thumb"))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                allImages.AddRange(images);
            }

            int idx = allImages.FindIndex(f =>
                string.Equals(f, currentFullPath, StringComparison.OrdinalIgnoreCase));
            return (allImages, idx);
        }

        /// <summary>
        /// 获取当前屏幕的工作区尺寸（排除任务栏），返回值已转换为 WPF 设备无关坐标 (DIP)。
        /// Win32 MONITORINFO.rcWork 是物理像素 → 乘以 TransformFromDevice（像素→DIP 缩放因子）
        /// 转换为 WPF 坐标系。100% DPI 时 scale=1.0，150% 时 scale=0.667。
        /// </summary>
        public static Rect GetScreenWorkingArea(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                // 兜底：SystemParameters.WorkArea 已是 DIP，无需转换
                return new Rect(0, 0,
                    SystemParameters.WorkArea.Width,
                    SystemParameters.WorkArea.Height);
            }

            var monitorInfo = new NativeMethods.MONITORINFO();
            monitorInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            {
                // Win32 rcWork 是物理像素 → 转换为 WPF DIP
                // TransformFromDevice 是 像素→DIP 的缩放因子，例如 150% DPI 时 = 0.667
                var source = PresentationSource.FromVisual(window);
                double scaleX = 1.0, scaleY = 1.0;
                if (source != null)
                {
                    scaleX = source.CompositionTarget.TransformFromDevice.M11;
                    scaleY = source.CompositionTarget.TransformFromDevice.M22;
                }
                else
                {
                    // 窗口尚未 Show()，PresentationSource 不可用，物理像素无法转为 DIP。
                    // 回退到 SystemParameters（始终是正确 DIP 值），避免高 DPI 下图片偏右下。
                    return new Rect(
                        SystemParameters.WorkArea.Left,
                        SystemParameters.WorkArea.Top,
                        SystemParameters.WorkArea.Width,
                        SystemParameters.WorkArea.Height);
                }

                return new Rect(
                    monitorInfo.rcWork.Left   * scaleX,
                    monitorInfo.rcWork.Top    * scaleY,
                    (monitorInfo.rcWork.Right - monitorInfo.rcWork.Left) * scaleX,
                    (monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top) * scaleY);
            }

            return new Rect(0, 0,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height);
        }

        /// <summary>
        /// Win32 API 封装
        /// </summary>
        internal static class NativeMethods
        {
            public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct MONITORINFO
            {
                public int cbSize;
                public RECT rcMonitor;
                public RECT rcWork;
                public uint dwFlags;
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left, Top, Right, Bottom;
            }
        }
    }
}
