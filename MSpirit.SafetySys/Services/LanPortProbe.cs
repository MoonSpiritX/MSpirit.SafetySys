// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using MS_SafetyManagementSystem.Common;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// LAN 服务端口探测（人员录入 / 隐患录入 / 手机传图三个窗口共用，2026-08-16 自各窗口
    /// StartOnAvailablePort 抽取统一）：从 <see cref="LanPorts.Start"/> 递增尝试启动服务，
    /// 返回成功绑定的端口；探测范围 [Start, <see cref="LanPorts.ProbeEnd"/>] 全部不可用返回 -1。
    ///
    /// 抽取原因（功能 Bug 修复，2026-08-16）：
    ///  - 三个窗口各自硬编码 `for (port = start; port &lt; start + 100; port++)`，与 LanPorts 常量
    ///    脱钩——改 LanPorts.Start 后探测上限不跟随，且探测范围与防火墙放行范围（Start..FirewallEnd）
    ///    可能错位；
    ///  - 原捕获集（IOException / InvalidOperationException / HttpListenerException）漏掉
    ///    **SocketException**：Kestrel 对"端口被占"抛 AddressInUseException（继承
    ///    InvalidOperationException，能捕获），但对 **Windows 保留/排除端口段**（Hyper-V、WSL、
    ///    WinNAT 等动态排除范围，bind 返回 WSAEACCES 10013）直接抛原始 SocketException——一旦
    ///    探测起点落在排除端口上，异常不被捕获、整个启动流程中止（"启动服务失败"弹窗），
    ///    而实际上下一个端口就能正常绑定。补充 SocketException 后探测才能如设计般继续；
    ///  - **手机浏览器不安全端口黑名单**（2026-08-16 实测定位）：Chromium 内核浏览器
    ///    （Chrome/Edge/微信/QQ 浏览器等）对内置黑名单端口（6000/X11、5060/5061/SIP、
    ///    6665-6669/IRC 等）直接拒绝发起连接（ERR_UNSAFE_PORT）——请求根本不会发出，
    ///    表现为"手机连不上、程序无请求日志、防火墙无弹窗"，而服务与防火墙均正常。
    ///    探测必须跳过这些端口（实测 6100 通、6000 不通）。
    /// </summary>
    public static class LanPortProbe
    {
        /// <summary>
        /// 手机浏览器（Chromium 内核）内置不安全端口黑名单（来源 Chromium net/base/port_util.cc
        /// kRestrictedPorts，截取本程序探测常用范围 5000-10100 内相关项）。
        /// 浏览器对黑名单端口直接拒绝连接，请求不发出——探测时必须跳过，否则"绑定成功但手机连不上"。
        /// </summary>
        private static readonly HashSet<int> BrowserRestrictedPorts = new()
        {
            5060, 5061,                                    // SIP
            6000,                                          // X11（本次 6000 连不上的真凶）
            6566,                                          // SAN 锁定
            6665, 6666, 6667, 6668, 6669,                  // IRC
            6697,                                          // IRC over TLS
            10080,                                         // Amanda
        };

        /// <summary>判断端口是否在手机浏览器不安全端口黑名单中（纯函数，可单测）</summary>
        public static bool IsBrowserRestrictedPort(int port) => BrowserRestrictedPorts.Contains(port);

        /// <summary>
        /// 判定异常是否为"端口不可用"类绑定失败（应继续尝试下一个端口）。
        ///  - AddressInUseException（Kestrel，端口被占）→ 继承 InvalidOperationException；
        ///  - IOException（Kestrel 绑定失败包装）；
        ///  - SocketException（Windows 保留/排除端口段，Kestrel 未包装直接抛出）；
        ///  - HttpListenerException（历史遗留形态，防回归）。
        /// 其余异常（OCR 初始化、配置错误、数据服务异常等）不属于端口占用，不得吞掉。
        /// </summary>
        public static bool IsBindFailure(Exception ex) =>
            ex is IOException or InvalidOperationException or SocketException or HttpListenerException;

        /// <summary>
        /// 从 startPort 起逐个调用 startAction(port) 尝试启动服务，返回成功绑定的端口号；
        /// [startPort, LanPorts.ProbeEnd] 全部不可用返回 -1。
        /// 自动跳过手机浏览器不安全端口黑名单（见 <see cref="IsBrowserRestrictedPort"/>）。
        /// </summary>
        /// <param name="startPort">探测起点（调用方传 LanPorts.Start）</param>
        /// <param name="startAction">对指定端口执行服务启动；成功返回（不抛异常）即视为绑定成功</param>
        public static int FindAvailable(int startPort, Action<int> startAction)
        {
            ArgumentNullException.ThrowIfNull(startAction);
            for (int port = startPort; port <= LanPorts.ProbeEnd; port++)
            {
                if (IsBrowserRestrictedPort(port))
                {
                    Log.Information("LanPortProbe: 端口 {Port} 在手机浏览器不安全端口黑名单（ERR_UNSAFE_PORT）中，跳过", port);
                    continue;
                }
                try
                {
                    startAction(port);
                    return port;
                }
                catch (Exception ex) when (IsBindFailure(ex))
                {
                    Log.Information("LanPortProbe: 端口 {Port} 不可用（{Type}: {Message}），尝试下一个",
                        port, ex.GetType().Name, ex.Message);
                }
            }
            return -1;
        }
    }
}
