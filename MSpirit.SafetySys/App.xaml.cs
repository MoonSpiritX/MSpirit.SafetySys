// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Data.Sqlite;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.ImageViewer;
using MS_SafetyManagementSystem.Services;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace MS_SafetyManagementSystem
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // ── 查看器独立进程模式 ──
            if (e.Args.Length >= 2 && e.Args[0] == "--viewer")
            {
                LaunchViewerProcess(e.Args[1]);
                // 不调用 Shutdown()——前台 STA 线程会保持进程存活，查看器关闭后自动退出
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Log.Error(ex, "未处理异常");
                MessageBox.Show($"未处理异常：{ex?.Message}\n{ex?.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            Dispatcher.UnhandledException += (sender, args) =>
            {
                Log.Error(args.Exception, "UI线程未处理异常");
                // 显示完整堆栈，便于定位（排查期保留；稳定后改回仅 Message）
                MessageBox.Show($"UI线程异常：{args.Exception}\n\n堆栈：{args.Exception.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            base.OnStartup(e);

            if (!SingleInstanceGuard.TryAcquire())
            {
                WindowHelper.BringExistingToFront("斜月安全管理系统");
                // 统一走退出管理器（此时未创建主窗口、无钩子，仅关闭应用）
                AppExitManager.RequestExit(ExitReason.SingleInstance);
                return;
            }

            // 日志初始化（必须最先：后续数据根解析本身就要写日志）
            InitLogging();

            Log.Information("=== 斜月安全管理系统启动 ===");

            // ── 数据根决策（2026-08-15）：记录 → 浅扫 → 首启 → 弹窗引导 ──
            // 解决拔盘/盘符变化导致数据根"漂移"问题：启动早期确定一次数据根并全局共享，
            // 各处通过 AppServiceProvider.GetDataPathService() 获取，不再各自 new 计算。
            string? rootPath = DataRootLocator.ResolveRootPath();
            if (rootPath == null)
            {
                // 疑似丢失（有记录但失效且浅扫无果）：弹窗引导用户恢复
                var recovery = new Views.DataRootRecoveryWindow(DataRootLocator.ReadRecordedRoot() ?? "");
                recovery.ShowDialog();
                if (recovery.SelectedRootPath != null)
                {
                    rootPath = recovery.SelectedRootPath;
                    DataRootLocator.WriteRecordedRoot(rootPath);
                }
                else if (recovery.UseNewLocation)
                {
                    rootPath = DataPathService.DefaultRootPath();
                    DataRootLocator.WriteRecordedRoot(rootPath);
                }
                else
                {
                    // 用户直接关闭窗口（未做选择）→ 保守用默认路径但不写记录，下次启动仍会提示
                    rootPath = DataPathService.DefaultRootPath();
                }
            }

            var dataPathService = new DataPathService(rootPath);
            AppServiceProvider.DataPathService = dataPathService;
            var settingsService = new SettingsService(dataPathService);
            AppServiceProvider.SettingsService = settingsService;

            // 缩略图迁移：旧版缩略图从 Card_{id}/ 移到 Card_{id}/thumb/  (2026-06-27)
            ThumbnailMigration.Run(dataPathService.RootPath);

            var main = new MainWindow(settingsService);
            main.Show();

            // 启动自动备份（兜底：上次异常退出时可能没有退出备份；在线备份模式，后台静默执行）
            System.Threading.Tasks.Task.Run(() => BackupService.RunStartupBackup(dataPathService));

            // 更新 IPC：供 Updater.exe 请求"保存数据并退出"（绿色版更新时避免文件占用）
            UpdateIpcService.Start();
        }

        /// <summary>
        /// 统一初始化 Serilog（主进程与查看器子进程共用，2026-10-15 合并原先两处重复代码）。
        /// 日志目录固定为 %USERPROFILE%\MSpirit.SafetySys\logs\：
        /// <para>
        /// ① 不写在程序目录旁——MSIX 下安装目录 C:\Program Files\WindowsApps 只读，日志会静默失败；
        /// </para>
        /// <para>
        /// ② 不写在 %LOCALAPPDATA%——MSIX 会重定向到包私有目录，卸载即清空，排障时拿不到旧日志。
        /// </para>
        /// 用户目录两不沾：启动第一行即可写、卸载后仍保留、绿色版与商店版行为一致。
        /// </summary>
        /// <param name="writeToConsole">是否同时输出到控制台（主进程 true，查看器子进程 false）</param>
        private static void InitLogging(bool writeToConsole = true)
        {
            var config = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning);

            try
            {
                string logPath = AppPaths.LogFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                config = config.WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    encoding: System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 日志目录不可写（极端权限受限）时降级为不落盘，绝不让日志问题阻断启动
                Console.WriteLine($"[日志] 无法写入日志文件，已降级为不落盘：{ex.Message}");
            }

            if (writeToConsole) config = config.WriteTo.Console();
            Log.Logger = config.CreateLogger();
        }

        /// <summary>
        /// 从临时 JSON 文件读取图片列表并启动内置查看器（独立进程模式）。
        /// 绕过 SingleInstanceGuard 和主窗口，专供 --viewer 子进程使用。
        /// </summary>
        private static void LaunchViewerProcess(string tempFilePath)
        {
            // 子进程也需初始化 Serilog，否则 Log.Information 静默失败（无 Logger 输出）
            InitLogging(writeToConsole: false);
            Log.Information("=== 图片查看器子进程启动 ===");

            try
            {
                if (!File.Exists(tempFilePath)) return;

                string json = File.ReadAllText(tempFilePath, System.Text.Encoding.UTF8);
                var data = JsonSerializer.Deserialize<ViewerLaunchData>(json);
                Log.Information($"[ImageViewer] LaunchViewerProcess: deserialized StartFullScreen={data?.StartFullScreen}, Paths.Count={data?.Paths?.Count}");
                if (data?.Paths == null || data.Paths.Count == 0) return;

                // 清理临时文件
                try { File.Delete(tempFilePath); } catch { /* 临时文件删除失败可忽略 */ }

                ImageViewerLauncher.ShowViewerAndWait(data.Paths, data.StartIndex, data.StartFullScreen);
                // 查看器 STA 线程已退出，统一走退出管理器关闭应用（子进程未注册钩子，仅 Shutdown）
                AppExitManager.RequestExit(ExitReason.ViewerProcess);
            }
            catch (Exception ex)
            {
                Log.Information($"[Thumb] LaunchViewerProcess 异常: {ex.Message}");
                MessageBox.Show($"图片查看器启动失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private class ViewerLaunchData
        {
            public List<string> Paths { get; set; } = new();
            public int StartIndex { get; set; }
            public bool StartFullScreen { get; set; } = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SingleInstanceGuard.Release();
            // 清理主数据库连接池（2026-08-10 目录调整：master.db 位于 ProjectsDate\）
            var dataPathService = AppServiceProvider.GetDataPathService();
            var masterDbPath = Path.Combine(dataPathService.RootPath, "ProjectsDate", "master.db");
            if (File.Exists(masterDbPath))
            {
                using var conn = new SqliteConnection($"Data Source={masterDbPath}");
                SqliteConnection.ClearPool(conn);
            }
            // 清理所有项目数据库连接池
            var projectsDateDir = Path.Combine(dataPathService.RootPath, "ProjectsDate");
            if (Directory.Exists(projectsDateDir))
            {
                foreach (var projectFolder in Directory.GetDirectories(projectsDateDir))
                {
                    var dbPath = Path.Combine(projectFolder, "data.db");
                    if (File.Exists(dbPath))
                    {
                        using var conn = new SqliteConnection($"Data Source={dbPath}");
                        SqliteConnection.ClearPool(conn);
                    }
                }
            }

            // 退出自动备份（主备份）：所有数据库连接已关闭，直接复制文件最可靠
            BackupService.RunShutdownBackup(dataPathService);

            Log.Information("斜月安全管理系统正常退出");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
