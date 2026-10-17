// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// LAN 窗口"已连接设备"跟踪（三个窗口共用，2026-08-16 自各窗口重复代码抽取）。
    /// HTTP 无长连接，靠心跳判定设备在线/断开：
    ///  - <see cref="OnConnected"/>：任何授权请求到达时调用（记录心跳，IP 去重）；
    ///  - <see cref="OnLeft"/>：手机页 pagehide 经 /bye 上报（立即移除，无需等超时）；
    ///  - <see cref="Sweep"/>：周期调用，把超过 Timeout 无请求的设备移除（beacon 失败兜底）。
    /// 纯逻辑、不依赖 UI/Dispatcher（假定调用方已汇到同一线程，如窗口 Dispatcher），可单测；
    /// 定时器与界面刷新由窗口负责。
    /// </summary>
    public sealed class LanDeviceTracker
    {
        private readonly Dictionary<string, DateTime> _lastSeen = new();

        /// <summary>
        /// 默认心跳超时（10 秒）。取值依据：手机端每 3 秒 ping 一次，10 秒 = 容忍 3 次连续丢失，
        /// 足够抗 Wi-Fi 抖动，又不至于像原 30 秒那样让"切到后台"的用户干等。
        /// 手机端仅在页面可见时上报心跳（见 Resources/*_page.html）→ 切后台/锁屏即停止上报，
        /// 最长 Timeout + 巡检间隔（2 秒）≈ 12 秒内归零。
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        /// <summary>心跳超时：超过此时长无任何授权请求视为已断开（正常关页由 /bye 即时移除，此处仅兜底）</summary>
        public TimeSpan Timeout { get; }

        public LanDeviceTracker(TimeSpan timeout) => Timeout = timeout;

        /// <summary>以 <see cref="DefaultTimeout"/> 构造（三个 LAN 窗口使用，避免超时值散落多处）</summary>
        public LanDeviceTracker() : this(DefaultTimeout) { }

        /// <summary>当前在线设备数</summary>
        public int Count => _lastSeen.Count;

        /// <summary>记录设备心跳（授权请求到达）；空 IP 忽略</summary>
        public void OnConnected(string ip, DateTime? now = null)
        {
            if (string.IsNullOrEmpty(ip)) return;
            _lastSeen[ip] = now ?? DateTime.Now;
        }

        /// <summary>设备离开（页面关闭 /bye 上报）→ 立即移除；返回是否确实移除了该设备</summary>
        public bool OnLeft(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;
            return _lastSeen.Remove(ip);
        }

        /// <summary>移除超过 Timeout 无心跳的设备；返回是否有设备被移除（now 可注入便于测试）</summary>
        public bool Sweep(DateTime? now = null)
        {
            var cutoff = (now ?? DateTime.Now) - Timeout;
            bool removed = false;
            foreach (var kv in _lastSeen.ToList())
            {
                if (kv.Value < cutoff)
                {
                    _lastSeen.Remove(kv.Key);
                    removed = true;
                }
            }
            return removed;
        }

        /// <summary>清空全部（停止服务/重启会话时）</summary>
        public void Reset() => _lastSeen.Clear();
    }
}
