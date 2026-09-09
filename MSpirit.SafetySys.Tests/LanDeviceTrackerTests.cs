// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

// LanDeviceTracker（三个 LAN 窗口共用"已连接设备"跟踪）单元测试：
//  - 授权请求记录心跳、IP 去重；
//  - /bye 即时移除；
//  - 心跳超时兜底清理（Sweep）；
//  - 停止服务/重启会话 Reset。
// 时间全部可注入（now 参数），测试确定性、无 sleep。

using System;
using MS_SafetyManagementSystem.Services;
using Xunit;

namespace MSpirit.SafetySys.Tests
{
    public class LanDeviceTrackerTests
    {
        private static LanDeviceTracker NewTracker(TimeSpan timeout) => new(timeout);

        [Fact]
        public void OnConnected_记录心跳并计数()
        {
            var t = NewTracker(TimeSpan.FromSeconds(30));
            t.OnConnected("192.168.1.10", new DateTime(2026, 8, 16, 10, 0, 0));
            t.OnConnected("192.168.1.11", new DateTime(2026, 8, 16, 10, 0, 1));
            Assert.Equal(2, t.Count);
        }

        [Fact]
        public void OnConnected_同IP重复请求_去重不重复计数()
        {
            var t = NewTracker(TimeSpan.FromSeconds(30));
            t.OnConnected("192.168.1.10", new DateTime(2026, 8, 16, 10, 0, 0));
            t.OnConnected("192.168.1.10", new DateTime(2026, 8, 16, 10, 0, 3)); // 页面 3 秒轮询
            Assert.Equal(1, t.Count);
        }

        [Fact]
        public void OnConnected_空IP_忽略()
        {
            var t = NewTracker(TimeSpan.FromSeconds(30));
            t.OnConnected("", new DateTime(2026, 8, 16, 10, 0, 0));
            t.OnConnected(null!, new DateTime(2026, 8, 16, 10, 0, 0));
            Assert.Equal(0, t.Count);
        }

        [Fact]
        public void OnLeft_立即移除_返回true()
        {
            var t = NewTracker(TimeSpan.FromSeconds(30));
            t.OnConnected("192.168.1.10", new DateTime(2026, 8, 16, 10, 0, 0));
            Assert.True(t.OnLeft("192.168.1.10"));
            Assert.Equal(0, t.Count);
        }

        [Fact]
        public void OnLeft_未连接设备_返回false()
        {
            var t = NewTracker(TimeSpan.FromSeconds(30));
            Assert.False(t.OnLeft("192.168.1.99"));
            Assert.False(t.OnLeft(""));
        }

        [Fact]
        public void Sweep_超时设备被移除()
        {
            var t = NewTracker(TimeSpan.FromSeconds(20));
            var t0 = new DateTime(2026, 8, 16, 10, 0, 0);
            t.OnConnected("old", t0);                  // 心跳在 t0
            t.OnConnected("fresh", t0.AddSeconds(19)); // 1 秒前还在活动
            bool removed = t.Sweep(t0.AddSeconds(21)); // 现在 = t0+21s，cutoff = t0+1s → old 已超时、fresh 未超时
            Assert.True(removed);
            Assert.Equal(1, t.Count);                  // 只剩 fresh
        }

        [Fact]
        public void Sweep_未超时设备_不移除()
        {
            var t = NewTracker(TimeSpan.FromSeconds(20));
            var t0 = new DateTime(2026, 8, 16, 10, 0, 0);
            t.OnConnected("a", t0.AddSeconds(5));
            t.OnConnected("b", t0.AddSeconds(10));
            Assert.False(t.Sweep(t0.AddSeconds(15))); // 都在 20s 内
            Assert.Equal(2, t.Count);
        }

        [Fact]
        public void Sweep_恰好等于超时边界_不移除()
        {
            // 判定是 kv.Value < cutoff（严格小于），恰好在边界上视为仍在线
            var t = NewTracker(TimeSpan.FromSeconds(20));
            var t0 = new DateTime(2026, 8, 16, 10, 0, 0);
            t.OnConnected("edge", t0);
            Assert.False(t.Sweep(t0.AddSeconds(20))); // cutoff = t0，edge 的 lastSeen == cutoff → 不移除
            Assert.Equal(1, t.Count);
        }

        [Fact]
        public void Sweep_无设备_返回false()
        {
            var t = NewTracker(TimeSpan.FromSeconds(20));
            Assert.False(t.Sweep(new DateTime(2026, 8, 16, 10, 0, 0)));
        }

        [Fact]
        public void Reset_清空全部()
        {
            var t = NewTracker(TimeSpan.FromSeconds(20));
            t.OnConnected("a", new DateTime(2026, 8, 16, 10, 0, 0));
            t.OnConnected("b", new DateTime(2026, 8, 16, 10, 0, 0));
            t.Reset();
            Assert.Equal(0, t.Count);
            Assert.False(t.Sweep(new DateTime(2026, 8, 16, 10, 0, 5)));
        }

        [Fact]
        public void 心跳续期_防止被超时误删()
        {
            var t = NewTracker(TimeSpan.FromSeconds(20));
            var t0 = new DateTime(2026, 8, 16, 10, 0, 0);
            t.OnConnected("phone", t0);
            // 页面每 3 秒轮询续期
            for (int i = 1; i <= 8; i++)
            {
                Assert.False(t.Sweep(t0.AddSeconds(i * 3)));
                t.OnConnected("phone", t0.AddSeconds(i * 3));
            }
            Assert.Equal(1, t.Count);
        }
    }
}
