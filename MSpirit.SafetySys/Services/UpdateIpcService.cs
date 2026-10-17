// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// 更新 IPC 服务：供更新器（Updater.exe）请求主程序"保存数据并退出"。
    /// 
    /// 背景：绿色版更新时，主程序 exe/dll 被进程占用无法覆盖。更新器检测到主程序运行时，
    /// 通过命名管道发送 save-exit 命令 → 主程序保存所有数据后正常退出 → 更新器继续替换文件。
    ///
    /// 协议（管道名固定，命令一行文本）：
    ///   管道名：MSpirit.SafetySys.Update
    ///   命令：save-exit（请求保存并退出）
    /// </summary>
    public static class UpdateIpcService
    {
        /// <summary>命名管道名（更新器侧必须一致）</summary>
        public const string PipeName = "MSpirit.SafetySys.Update";

        /// <summary>请求保存并退出的命令文本</summary>
        public const string CommandSaveExit = "save-exit";

        private static Thread? _listenerThread;
        private static volatile bool _running;

        /// <summary>
        /// 启动管道监听（后台线程，应用退出时随进程终止）。
        /// 应在 SingleInstanceGuard 成功、主窗口创建后调用。
        /// </summary>
        public static void Start()
        {
            if (_running) return;
            _running = true;

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "UpdateIpcListener",
            };
            _listenerThread.Start();
            Log.Information("[UpdateIpc] 管道监听已启动");
        }

        private static void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);

                    pipe.WaitForConnection();
                    Log.Information("[UpdateIpc] 收到管道连接");

                    string? command = null;
                    using (var reader = new StreamReader(pipe))
                    {
                        command = reader.ReadLine();
                    }

                    if (string.Equals(command, CommandSaveExit, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("[UpdateIpc] 收到 save-exit，请求保存数据并退出");
                        RequestSaveAndExit();
                    }
                    else
                    {
                        Log.Warning("[UpdateIpc] 未知命令：{Command}", command ?? "<null>");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[UpdateIpc] 监听异常（继续等待下一连接）");
                    Thread.Sleep(300); // 异常后稍作停顿，避免死循环空转
                }
            }
        }

        /// <summary>调度到 UI 线程执行"保存数据 + 正常退出"。</summary>
        private static void RequestSaveAndExit()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    // 主窗口保存视图状态 + 清焦点保存所有记录
                    if (Application.Current.MainWindow is MainWindow main)
                    {
                        main.SaveAndExitForUpdate();
                    }
                    else
                    {
                        Log.Warning("[UpdateIpc] 主窗口不可用，直接退出");
                        Application.Current.Shutdown();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[UpdateIpc] 保存并退出时异常，强制退出");
                    Application.Current.Shutdown();
                }
            });
        }
    }
}
