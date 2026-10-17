// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>程序退出原因（记录到日志，便于排查谁触发了退出）</summary>
    public enum ExitReason
    {
        TrayMenu,       // 托盘菜单「退出程序」
        UpdateRequest,  // 更新器 IPC save-exit（保存数据并退出）
        SingleInstance, // 单实例冲突（本实例让位退出）
        ViewerProcess,  // 图片查看器子进程模式
    }

    /// <summary>
    /// 程序退出管理器：所有退出路径的统一入口。
    /// 职责：防重入 → 依序执行各模块注册的退出清理钩子（保存记录/视图状态、停止服务等，
    /// 逐个异常隔离、互不拖累）→ 关闭 Application。
    ///
    /// 为什么需要它：程序退出时"要保存什么/停什么"会随模块增多而膨胀（隐患、人员、LAN 服务、
    /// 未来的第 3、第 N 个模块），如果每个退出入口各自写一遍必然漏。统一由本管理器调度：
    /// 新增模块需要退出清理时，在启动处调用 <see cref="RegisterCleanup"/> 登记即可，无需改动任何退出入口。
    ///
    /// App.OnExit 仍是最终兜底（清 SQLite 连接池、退出自动备份、释放单实例锁、关闭日志），本管理器不重复这些。
    /// </summary>
    public static class AppExitManager
    {
        private static readonly List<Action> Cleanups = new();
        private static readonly object Sync = new();
        private static int _exiting; // 0=空闲 1=退出流程已开始（Interlocked 防重入）

        /// <summary>
        /// 注册退出清理钩子（幂等：同一委托只注册一次，重复注册会被忽略）。
        /// 建议在窗口/服务启动处（如构造函数）调用。
        /// </summary>
        public static void RegisterCleanup(Action cleanup)
        {
            if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
            lock (Sync)
            {
                if (!Cleanups.Contains(cleanup))
                    Cleanups.Add(cleanup);
            }
        }

        /// <summary>请求退出：防重入 → 依序执行清理钩子（异常隔离）→ 关闭应用。任意线程可调用（内部调度到 UI 线程）。</summary>
        public static void RequestExit(ExitReason reason = ExitReason.TrayMenu)
        {
            // 非 UI 线程调用时调度到 UI 线程（钩子可能操作 WPF 控件/绑定），再执行核心流程
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => RunExitCore(reason));
                return;
            }
            RunExitCore(reason);
        }

        private static void RunExitCore(ExitReason reason)
        {
            // 防重入：同进程只允许一次完整退出流程（更新器重复请求、托盘连点等场景直接忽略）
            if (Interlocked.Exchange(ref _exiting, 1) == 1)
            {
                Log.Information("[AppExit] 退出流程已在执行，忽略本次请求（{Reason}）", reason);
                return;
            }

            Log.Information("[AppExit] 开始退出流程：{Reason}", reason);
            try
            {
                // 快照清理列表：执行期间注册列表可能被钩子修改，遍历快照避免集合被修改异常
                List<Action> actions;
                lock (Sync)
                {
                    actions = new List<Action>(Cleanups);
                }

                foreach (var action in actions)
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        // 单个钩子失败不阻断退出流程，也不影响后续钩子
                        Log.Warning(ex, "[AppExit] 退出清理钩子执行失败（忽略，继续）");
                    }
                }
            }
            finally
            {
                Application.Current?.Shutdown();
            }
        }

        /// <summary>仅单元测试用：重置静态状态（清空钩子与退出标志），避免测试间相互污染。</summary>
        internal static void ResetForTests()
        {
            lock (Sync)
            {
                Cleanups.Clear();
            }
            Interlocked.Exchange(ref _exiting, 0);
        }
    }
}
