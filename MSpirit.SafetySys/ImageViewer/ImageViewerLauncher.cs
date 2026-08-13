// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.ImageViewer
{
    /// <summary>
    /// 图片查看器启动器 — 在独立 STA 线程中管理查看器窗口的生命周期。
    /// 支持启动时默认全屏或普通窗口模式。
    /// 单实例：同时只能打开一个查看器窗口。
    /// </summary>
    public static class ImageViewerLauncher
    {
        // ── 单实例状态 ──
        private static ImageViewerWindow? _currentViewer;
        private static Dispatcher? _viewerDispatcher;
        private static readonly object _viewerLock = new();

        /// <summary>
        /// 将图片列表序列化到临时 JSON 文件，启动独立子进程运行查看器。
        /// 子进程崩溃不影响主程序。
        /// </summary>
        public static void LaunchAsProcess(List<string> imagePaths, int startIndex, bool startFullScreen = true)
        {
            if (imagePaths == null || imagePaths.Count == 0) return;
            startIndex = Math.Clamp(startIndex, 0, imagePaths.Count - 1);

            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(),
                    $"ms_safety_viewer_{Guid.NewGuid():N}.json");
                var data = new { Paths = imagePaths, StartIndex = startIndex, StartFullScreen = startFullScreen };
                string json = JsonSerializer.Serialize(data);
                Log.Information($"[ImageViewer] LaunchAsProcess: json={json.Substring(0, Math.Min(json.Length, 200))}..., startFullScreen={startFullScreen}");
                File.WriteAllText(tempFile, json, System.Text.Encoding.UTF8);

                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                var psi = new ProcessStartInfo(exePath, $"--viewer \"{tempFile}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法启动图片查看器进程：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void ShowViewerAndWait(List<string> imagePaths, int startIndex, bool startFullScreen = true)
        {
            if (imagePaths == null || imagePaths.Count == 0) return;
            var thread = CreateViewerThread(imagePaths, startIndex, startFullScreen, asForeground: true);
            thread.Start();
            thread.Join();
        }

        private static Thread CreateViewerThread(List<string> imagePaths, int startIndex, bool startFullScreen, bool asForeground)
        {
            if (imagePaths == null || imagePaths.Count == 0) return null!;
            startIndex = Math.Clamp(startIndex, 0, imagePaths.Count - 1);

            var thread = new Thread(() =>
            {
                try
                {
                    // 关键：纯 STA 线程上新创建 Dispatcher 不会自动安装 SynchronizationContext，
                    // 导致 await 无法捕获 UI 线程上下文 + TaskScheduler.FromCurrentSynchronizationContext() 失败。
                    // 手动安装 DispatcherSynchronizationContext，确保后续 async/await 和 ContinueWith 正确调度。
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                    var window = new ImageViewerWindow();

                    // 注册到静态引用（单实例追踪）
                    lock (_viewerLock)
                    {
                        _currentViewer = window;
                        _viewerDispatcher = Dispatcher.CurrentDispatcher;
                    }

                    // 窗口关闭 → 清除静态引用、停止消息泵
                    window.Closed += (_, _) =>
                    {
                        lock (_viewerLock)
                        {
                            _currentViewer = null;
                            _viewerDispatcher = null;
                        }
                        Dispatcher.CurrentDispatcher.InvokeShutdown();
                    };

                    // 启动异步图片加载
                    _ = window.SetImageList(imagePaths, startIndex);

                    Log.Information($"[ImageViewer] CreateViewerThread: startFullScreen={startFullScreen}, imageCount={imagePaths.Count}, startIndex={startIndex}");

                    if (startFullScreen)
                    {
                        // 全屏模式：Show() 前预配置视觉，Show() 后立设直角+Opacity=1。
                        // DwmSetWindowAttribute 需要 DWM 已追踪该窗口（Show() 后）才会生效。
                        window.ConfigureForFullscreenLaunch();
                        window.PendingFullScreenLaunch = true;
                        window.Opacity = 0;
                        window.Show();
                        window.SetCornerPreference(ImageViewerWindow.DWMWCP_DONOTROUND);
                        window.Opacity = 1;
                        window.Dispatcher.InvokeAsync(async () =>
                        {
                            await window.AnimateFullscreenEntry();
                        });
                    }
                    else
                    {
                        // 普通模式：等首图加载 → ApplyInitialWindowSize 已定好尺寸 → Show 即居中正确
                        Log.Information("[ImageViewer] 进入普通模式路径，调用 WaitForFirstImage...");
                        window.WaitForFirstImage();
                        Log.Information("[ImageViewer] WaitForFirstImage 完成，调用 Show()...");
                        window.Show();
                        Log.Information("[ImageViewer] Show() 完成，进入 Dispatcher.Run()");
                    }

                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    lock (_viewerLock)
                    {
                        _currentViewer = null;
                        _viewerDispatcher = null;
                    }
                    System.Diagnostics.Debug.WriteLine($"图片查看器线程异常: {ex.Message}");
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = !asForeground;
            thread.Name = "ImageViewerThread";
            return thread;
        }
    }
}
