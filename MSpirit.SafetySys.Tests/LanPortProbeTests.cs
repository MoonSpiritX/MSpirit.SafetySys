// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

// LanPortProbe（三窗口共用端口探测）单元测试：
//  - 探测范围 = [startPort, LanPorts.ProbeEnd]，不越界、不跳过（黑名单端口除外）；
//  - 捕获集必须包含 Kestrel 绑定失败的全部异常形态（占用→AddressInUseException/IOException、
//    Windows 保留端口→SocketException、历史形态→HttpListenerException），
//    非端口占用异常不得吞掉（2026-08-16 回归点：漏 SocketException 会导致探测起点落在
//    保留端口上时整个启动流程中止）；
//  - **手机浏览器不安全端口黑名单**（2026-08-16 定位）：Chromium 内核浏览器拒绝连接
//    6000（X11）/5060/5061/6665-6669 等端口（ERR_UNSAFE_PORT），探测必须跳过，
//    否则"绑定成功但手机连不上"。当前 LanPorts.Start=6000 恰为黑名单端口，测试用 SafeStart 规避。
// 纯逻辑测试：用假 startAction 模拟端口占用，不启动真实服务。

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Services;
using Xunit;

namespace MSpirit.SafetySys.Tests
{
    public class LanPortProbeTests
    {
        /// <summary>取 [LanPorts.Start, ProbeEnd] 内首个连续 3 个端口均非浏览器黑名单的起点
        /// （Start+1/+2 被占的用例需要连续三个可探测端口）</summary>
        private static int SafeStart()
        {
            for (int p = LanPorts.Start; p <= LanPorts.ProbeEnd; p++)
            {
                if (!LanPortProbe.IsBrowserRestrictedPort(p) &&
                    !LanPortProbe.IsBrowserRestrictedPort(p + 1) &&
                    !LanPortProbe.IsBrowserRestrictedPort(p + 2))
                    return p;
            }
            return LanPorts.Start;
        }

        [Fact]
        public void FindAvailable_首端口空闲_直接返回起点端口()
        {
            int start = SafeStart();
            int called = 0;
            int port = LanPortProbe.FindAvailable(start, p => { called++; /* 不抛异常 = 绑定成功 */ });
            Assert.Equal(start, port);
            Assert.Equal(1, called); // 只尝试了一次
        }

        [Fact]
        public void FindAvailable_前两端口被占_返回第三个可用端口()
        {
            int start = SafeStart();
            int port = LanPortProbe.FindAvailable(start, p =>
            {
                if (p <= start + 1) throw new IOException($"端口 {p} 被占");
            });
            Assert.Equal(start + 2, port);
        }

        [Fact]
        public void FindAvailable_占用抛AddressInUse式InvalidOperationException_继续探测()
        {
            // Kestrel 对端口被占抛 AddressInUseException（继承 InvalidOperationException）
            int start = SafeStart();
            int port = LanPortProbe.FindAvailable(start, p =>
            {
                if (p == start) throw new InvalidOperationException("address already in use");
            });
            Assert.Equal(start + 1, port);
        }

        [Fact]
        public void FindAvailable_保留端口抛SocketException_继续探测()
        {
            // 2026-08-16 回归点：Windows 保留/排除端口段（Hyper-V/WSL/WinNAT）bind 返回
            // WSAEACCES(10013)，Kestrel 直接抛 SocketException——原捕获集漏掉它导致启动中止。
            int start = SafeStart();
            int port = LanPortProbe.FindAvailable(start, p =>
            {
                if (p == start) throw new SocketException(10013);
            });
            Assert.Equal(start + 1, port);
        }

        [Fact]
        public void FindAvailable_抛HttpListenerException_继续探测()
        {
            int start = SafeStart();
            int port = LanPortProbe.FindAvailable(start, p =>
            {
                if (p == start) throw new HttpListenerException(10048, "被占");
            });
            Assert.Equal(start + 1, port);
        }

        [Fact]
        public void FindAvailable_全范围被占_返回负一()
        {
            int start = SafeStart();
            int attempts = 0;
            int port = LanPortProbe.FindAvailable(start, _ =>
            {
                attempts++;
                throw new IOException("被占");
            });
            Assert.Equal(-1, port);
            // 恰好探测整个 [start, ProbeEnd] 区间（黑名单端口不计入尝试）
            int expected = Enumerable.Range(start, LanPorts.ProbeEnd - start + 1)
                .Count(p => !LanPortProbe.IsBrowserRestrictedPort(p));
            Assert.Equal(expected, attempts);
        }

        [Fact]
        public void FindAvailable_不会探测超出ProbeEnd的端口()
        {
            int start = SafeStart();
            int maxTried = -1;
            LanPortProbe.FindAvailable(start, p =>
            {
                maxTried = p;
                throw new IOException("被占");
            });
            Assert.True(maxTried <= LanPorts.ProbeEnd, $"探测越界：maxTried={maxTried} > ProbeEnd={LanPorts.ProbeEnd}");
            Assert.True(maxTried >= LanPorts.ProbeEnd - 3, $"探测提前终止：maxTried={maxTried}（ProbeEnd={LanPorts.ProbeEnd}）");
        }

        [Fact]
        public void FindAvailable_非端口占用异常_向上抛出不吞掉()
        {
            // 如 OCR 初始化失败 / 数据服务异常等不属于端口占用，必须让上层看到
            Assert.Throws<ApplicationException>(() =>
                LanPortProbe.FindAvailable(SafeStart(), _ => throw new ApplicationException("业务异常")));
        }

        [Fact]
        public void FindAvailable_空startAction_抛参数异常()
        {
            Assert.Throws<ArgumentNullException>(() => LanPortProbe.FindAvailable(SafeStart(), null!));
        }

        // ==================== 手机浏览器不安全端口黑名单（2026-08-16 定位） ====================

        [Theory]
        [InlineData(6000, true)]   // X11 —— 本次"6000 连不上"的真凶
        [InlineData(5060, true)]   // SIP
        [InlineData(5061, true)]   // SIP-TLS
        [InlineData(6665, true)]   // IRC
        [InlineData(6667, true)]   // IRC
        [InlineData(6697, true)]   // IRC-TLS
        [InlineData(6100, false)]  // 实测 6100 可连
        [InlineData(5000, false)]
        [InlineData(7000, false)]
        [InlineData(6001, false)]
        public void IsBrowserRestrictedPort_黑名单判定(int port, bool expected)
        {
            Assert.Equal(expected, LanPortProbe.IsBrowserRestrictedPort(port));
        }

        [Fact]
        public void FindAvailable_浏览器黑名单端口_自动跳过()
        {
            // 取 [Start, ProbeEnd] 内第一个黑名单端口（当前 Start=6000 必命中）；无则本用例不适用
            int restricted = Enumerable.Range(LanPorts.Start, LanPorts.ProbeEnd - LanPorts.Start + 1)
                .FirstOrDefault(LanPortProbe.IsBrowserRestrictedPort);
            if (restricted == 0) return;

            int? next = Enumerable.Range(restricted + 1, LanPorts.ProbeEnd - restricted)
                .FirstOrDefault(p => !LanPortProbe.IsBrowserRestrictedPort(p));
            if (next == 0) return;

            bool attempted = false;
            int port = LanPortProbe.FindAvailable(restricted, p => { attempted = p == restricted; });
            Assert.Equal(next.Value, port); // 跳过黑名单端口，绑定下一个可用端口
            Assert.False(attempted);        // 黑名单端口从未被尝试（请求都不会发出，绑定毫无意义）
        }

        [Theory]
        [InlineData(typeof(IOException), true)]
        [InlineData(typeof(InvalidOperationException), true)]
        [InlineData(typeof(SocketException), true)]
        [InlineData(typeof(HttpListenerException), true)]
        [InlineData(typeof(ApplicationException), false)]
        [InlineData(typeof(ArgumentException), false)]
        public void IsBindFailure_分类判定(Type type, bool expected)
        {
            // SocketException / HttpListenerException 无单字符串构造函数，单独构造；其余用 (string) 构造函数
            Exception ex = type == typeof(SocketException)
                ? new SocketException(10013)
                : type == typeof(HttpListenerException)
                    ? new HttpListenerException(10048, "被占")
                    : (Exception)Activator.CreateInstance(type, "test")!;
            Assert.Equal(expected, LanPortProbe.IsBindFailure(ex));
        }
    }
}
