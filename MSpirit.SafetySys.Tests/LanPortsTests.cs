// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

// LanPorts 端口常量一致性守护：改 LanPorts.Start 后，探测范围 / 防火墙放行范围 /
// 提示文案 必须保持自洽，否则会出现"探测到的端口防火墙没放行"或"防火墙放行了探测不到"
// 的错位（历史教训：常量注释曾停留在 Start=6000 时代的 6099/6100）。

using MS_SafetyManagementSystem.Common;
using Xunit;

namespace MSpirit.SafetySys.Tests
{
    public class LanPortsTests
    {
        [Fact]
        public void Start_在合法端口范围内()
        {
            Assert.InRange(LanPorts.Start, 1, 65535);
        }

        [Fact]
        public void ProbeEnd_恒为Start加99()
        {
            // 探测范围 = [Start, Start+99]，共 100 个端口（与窗口"均被占用"提示一致）
            Assert.Equal(LanPorts.Start + 99, LanPorts.ProbeEnd);
            Assert.True(LanPorts.ProbeEnd >= LanPorts.Start);
        }

        [Fact]
        public void FirewallEnd_恒为Start加100且覆盖探测范围()
        {
            // 防火墙放行终点必须 ≥ 探测终点（放行范围覆盖探测范围，否则探测到的端口可能被防火墙拦截）
            Assert.Equal(LanPorts.Start + 100, LanPorts.FirewallEnd);
            Assert.True(LanPorts.FirewallEnd >= LanPorts.ProbeEnd);
        }

        [Fact]
        public void 端口范围不越界65535()
        {
            Assert.True(LanPorts.FirewallEnd <= 65535,
                $"FirewallEnd={LanPorts.FirewallEnd} 超出合法端口上限 65535，需下调 LanPorts.Start");
        }
    }
}
