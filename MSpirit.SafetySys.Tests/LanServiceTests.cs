// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

// LanService（人员 LAN 录入 HTTP 服务）单元测试 —— 此前无测试（docs/LAN服务技术文档.md 已知缺口）。
// 覆盖（2026-08-16，配合 LanPortProbe 抽取）：
//  - 端口被占时 Start 抛出的异常形态必须被 LanPortProbe.IsBindFailure 识别（回归点：
//    漏 SocketException 会令端口探测在第一个保留端口上整体中止 → "启动服务失败"弹窗）；
//  - 被占端口失败后，同一实例可在下一端口正常启动（端口探测流程）；
//  - 启动成功：IsRunning/Port/token 鉴权（带 token 200、缺 token 401）；
//  - Stop 后端口释放；
//  - 纯函数：tempId/年月/隐患级别/照片类型/请求体限量。
// 只用临时目录与随机空闲端口，绝不触碰真实数据目录与固定端口。

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Services;
using Xunit;

namespace MSpirit.SafetySys.Tests
{
    [CollectionDefinition("LanService", DisableParallelization = true)]
    public class LanServiceCollection { }

    [Collection("LanService")]
    public class LanServiceTests : IDisposable
    {
        private readonly TestProject _project;
        private readonly FakeOcr _ocr = new();

        public LanServiceTests()
        {
            _project = new TestProject();
        }

        public void Dispose() => _project.Dispose();

        private sealed class FakeOcr : IOcrService
        {
            public string Name => "FakeOcr";
            public bool IsAvailable => true;
            public Task<List<OcrTextBlock>> RecognizeAsync(string imagePath) =>
                Task.FromResult(new List<OcrTextBlock>());
        }

        /// <summary>申请一个当前空闲的 TCP 端口（绑定后立即释放；测试间有小概率竞态，已串行化本集合）</summary>
        private static int GetFreePort()
        {
            using var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }

        /// <summary>占用指定端口（返回持有者，Dispose 释放）。
        /// 必须绑定 0.0.0.0（Any）：真实服务占用（QQ/svchost 等）都是通配绑定；
        /// 若只绑 127.0.0.1，Kestrel 默认 SO_REUSEADDR 仍能绑定同端口 → 测不出"端口被占"。</summary>
        private static TcpListener OccupyPort(int port)
        {
            var l = new TcpListener(IPAddress.Any, port);
            l.Start();
            return l;
        }

        [Fact]
        public async Task Start_空闲端口_服务运行且token鉴权生效()
        {
            int port = GetFreePort();
            using var service = new LanService(_project.Service, _ocr);
            service.Start(port);

            try
            {
                Assert.True(service.IsRunning);
                Assert.Equal(port, service.Port);
                Assert.False(string.IsNullOrEmpty(service.Token));
                Assert.False(string.IsNullOrEmpty(service.BoundIpAddress));

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

                // 带 token → 200（/ping 为服务存活探针）
                var ok = await http.GetAsync($"http://127.0.0.1:{port}/ping?token={service.Token}");
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

                // 缺 token → 401
                var noToken = await http.GetAsync($"http://127.0.0.1:{port}/ping");
                Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);

                // 错误 token → 401
                var badToken = await http.GetAsync($"http://127.0.0.1:{port}/ping?token=wrong");
                Assert.Equal(HttpStatusCode.Unauthorized, badToken.StatusCode);
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public void Start_端口被占_抛出的异常必须被端口探测识别()
        {
            // 2026-08-16 回归点：若 Kestrel 对端口被占抛出不在 LanPortProbe.IsBindFailure 集合内的
            // 异常形态（例如 Windows 保留端口的原始 SocketException），端口探测将整体中止，
            // 表现为"启动服务失败"弹窗（即用户改端口测试时"跑不通"的一种形态）。
            int port = GetFreePort();
            using var holder = OccupyPort(port); // 先占用
            using var service = new LanService(_project.Service, _ocr);

            Exception? thrown = null;
            try
            {
                service.Start(port);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.NotNull(thrown);
            Assert.True(LanPortProbe.IsBindFailure(thrown!),
                $"端口被占时 LanService.Start 抛出 {thrown!.GetType().FullName}，LanPortProbe 无法识别，" +
                "端口探测会在第一个被占端口上中止（功能性 Bug），需扩展 IsBindFailure 捕获集。");
        }

        [Fact]
        public async Task Start_被占端口失败后_同一实例下一端口可正常启动()
        {
            // 端口探测流程的真实还原：先失败、后成功（用两个独立空闲端口，避免相邻端口被本机服务占用）
            int busyPort = GetFreePort();
            int freePort = GetFreePort();
            using var holder = OccupyPort(busyPort);
            using var service = new LanService(_project.Service, _ocr);

            Assert.ThrowsAny<Exception>(() => service.Start(busyPort));
            Assert.False(service.IsRunning); // 失败后不得残留"运行中"状态

            service.Start(freePort); // 下一端口正常绑定
            try
            {
                Assert.True(service.IsRunning);
                Assert.Equal(freePort, service.Port);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var resp = await http.GetAsync($"http://127.0.0.1:{freePort}/ping?token={service.Token}");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public void Stop_后端口释放_可被其他进程重新绑定()
        {
            int port = GetFreePort();
            var service = new LanService(_project.Service, _ocr);
            service.Start(port);
            Assert.True(service.IsRunning);

            service.Stop();
            Assert.False(service.IsRunning);

            // Stop() 的 Kestrel 关闭是 fire-and-forget（设计：UI 线程不阻塞），端口释放异步完成，
            // 故轮询等待重新绑定成功（最长 5 秒）。
            bool rebound = false;
            for (int i = 0; i < 50 && !rebound; i++)
            {
                try
                {
                    using var reholder = OccupyPort(port);
                    rebound = reholder.Server.IsBound;
                }
                catch
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
            Assert.True(rebound, "Stop() 后 5 秒内端口未被释放（Kestrel 关闭可能失败）");
        }

        [Fact]
        public void Start_再次调用_幂等不重启()
        {
            int port = GetFreePort();
            using var service = new LanService(_project.Service, _ocr);
            service.Start(port);
            string tokenAfterFirst = service.Token;
            service.Start(port); // 已运行，应直接返回（不重建服务、不换 token）
            Assert.True(service.IsRunning);
            Assert.Equal(port, service.Port);
            Assert.Equal(tokenAfterFirst, service.Token);
            service.Stop();
        }

        // ==================== 纯函数校验 ====================

        [Theory]
        [InlineData("abc123", true)]
        [InlineData("ABC_123-def", true)]
        [InlineData("a/b", false)]
        [InlineData("..", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("a b", false)]
        public void IsValidTempId_校验标识符防穿越(string? id, bool expected)
        {
            Assert.Equal(expected, LanService.IsValidTempId(id));
        }

        [Theory]
        [InlineData("2026-08", true)]
        [InlineData("2026-12", true)]
        [InlineData("2026-8", false)]
        [InlineData("2026-00", false)]
        [InlineData("2026-13", false)]
        [InlineData("202608", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidYearMonth_校验年月(string? ym, bool expected)
        {
            Assert.Equal(expected, LanService.IsValidYearMonth(ym));
        }

        [Theory]
        [InlineData("一般隐患", true)]
        [InlineData("较大隐患", true)]
        [InlineData("重大隐患", true)]
        [InlineData("轻微隐患", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidHazardLevel_校验隐患级别(string? level, bool expected)
        {
            Assert.Equal(expected, LanService.IsValidHazardLevel(level));
        }

        [Theory]
        [InlineData("hazard", true)]
        [InlineData("remedy", true)]
        [InlineData("front", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsValidPhotoType_校验照片类型(string? type, bool expected)
        {
            Assert.Equal(expected, LanService.IsValidPhotoType(type));
        }

        [Fact]
        public async Task ReadBodyWithLimitAsync_限量读取()
        {
            // 不超过上限 → 返回完整字节
            var ctx1 = new DefaultHttpContext
            {
                Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes("hello")) }
            };
            var r1 = await LanService.ReadBodyWithLimitAsync(ctx1, 100);
            Assert.Equal("hello", Encoding.UTF8.GetString(r1!));

            // 超过上限 → 返回 null
            var ctx2 = new DefaultHttpContext
            {
                Request = { Body = new MemoryStream(Encoding.UTF8.GetBytes("hello")) }
            };
            var r2 = await LanService.ReadBodyWithLimitAsync(ctx2, 3);
            Assert.Null(r2);

            // ContentLength 声明超限 → 直接 null（不读流）
            var ctx3 = new DefaultHttpContext();
            ctx3.Request.ContentLength = 1000;
            ctx3.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
            var r3 = await LanService.ReadBodyWithLimitAsync(ctx3, 100);
            Assert.Null(r3);
        }
    }
}
