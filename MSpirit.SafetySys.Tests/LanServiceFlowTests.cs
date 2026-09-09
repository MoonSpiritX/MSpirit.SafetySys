// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

// LanService（人员 LAN 录入）完整业务流测试（第 3 步：补业务流落库链路，docs/LAN服务技术文档.md 已知缺口）。
// 真实 Kestrel + 真实临时 SQLite + FakeOCR（返回带姓名/身份证号的文字块）：
//   /upload（OCR 解析）→ /submit（入队）→ 后台消费者落库 PersonnelRecord + 附件落盘 → /status 轮询 completed。
//  + 错误路径：未上传即提交 / 非法部门分组 / 重复提交。
// 只用随机空闲端口与 TestProject 临时库，绝不触碰真实数据目录。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using Xunit;

namespace MSpirit.SafetySys.Tests
{
    [Collection("LanService")]
    public class LanServiceFlowTests : IDisposable
    {
        private readonly TestProject _project;
        private readonly FakeOcr _ocr = new();
        private readonly string _unitId;
        private readonly string _deptId;

        public LanServiceFlowTests()
        {
            _project = new TestProject();
            var unit = _project.AddPersonnelUnitGroup("测试单位");
            _unitId = unit.Id;
            var dept = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = _project.ProjectId,
                Module = ModuleNames.Personnel,
                ParentId = unit.Id,
                Name = "测试班组",
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
            };
            Assert.True(_project.Service.AddGroup(dept), "创建部门分组失败");
            _deptId = dept.Id;
        }

        public void Dispose() => _project.Dispose();

        /// <summary>FakeOCR：按需返回预设文字块（真实流程里图片内容无关，OCR 结果由本假实现决定）</summary>
        private sealed class FakeOcr : IOcrService
        {
            public List<OcrTextBlock> Blocks { get; set; } = new();
            public string Name => "FakeOcr";
            public bool IsAvailable => true;
            public Task<List<OcrTextBlock>> RecognizeAsync(string imagePath) => Task.FromResult(Blocks);
        }

        private static int GetFreePort()
        {
            using var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }

        private static async Task<JsonElement> WaitForStatus(HttpClient http, string baseUrl, string token, string tempId, string target, int tries = 50)
        {
            for (int i = 0; i < tries; i++)
            {
                var resp = await http.GetAsync($"{baseUrl}/status?token={token}&tempId={tempId}");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
                string status = json.GetProperty("status").GetString() ?? "";
                if (status == "failed")
                    Assert.Fail($"提交处理失败（tempId={tempId}）：{json.GetProperty("error").GetString()}");
                if (status == target)
                    return json.Clone();
                await Task.Delay(200);
            }
            Assert.Fail($"等待 /status 达到 {target} 超时（tempId={tempId}）");
            return default;
        }

        private static byte[] FakeImageBytes => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }; // 假图片字节（OCR 为 Fake）

        [Fact]
        public async Task 上传正面提交_OCR落库人员记录且附件落盘()
        {
            int port = GetFreePort();
            _ocr.Blocks = new List<OcrTextBlock>
            {
                new() { Text = "姓名张三" },
                new() { Text = "公民身份号码11010519491231002X" }, // 校验码合法的测试号（110105 北京朝阳，1949-12-31 女）
                new() { Text = "女" },
                new() { Text = "民族汉" }
            };
            using var service = new LanService(_project.Service, _ocr);
            service.Start(port);
            try
            {
                string baseUrl = $"http://127.0.0.1:{port}";
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string tempId = "flow-test-1";

                // 1. 上传正面 → OCR 返回姓名/身份证号
                var upload = await http.PostAsync(
                    $"{baseUrl}/upload?token={service.Token}&side=front&tempId={tempId}&groupId={_deptId}",
                    new ByteArrayContent(FakeImageBytes));
                Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
                var upJson = JsonDocument.Parse(await upload.Content.ReadAsStringAsync()).RootElement;
                Assert.True(upJson.GetProperty("ok").GetBoolean());
                Assert.Equal("张三", upJson.GetProperty("ocr").GetProperty("name").GetString());
                Assert.Equal("11010519491231002X", upJson.GetProperty("ocr").GetProperty("idNumber").GetString());
                Assert.Equal("女", upJson.GetProperty("ocr").GetProperty("gender").GetString());
                Assert.Equal("1949-12-31", upJson.GetProperty("ocr").GetProperty("birthDate").GetString());

                // 2. 提交 → 入队
                var submit = await http.PostAsync($"{baseUrl}/submit?token={service.Token}",
                    new StringContent($"{{\"tempId\":\"{tempId}\"}}", Encoding.UTF8, "application/json"));
                var subJson = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement;
                Assert.True(subJson.GetProperty("ok").GetBoolean());

                // 3. 轮询 /status 至 completed
                var status = await WaitForStatus(http, baseUrl, service.Token, tempId, "completed");
                Assert.Equal("张三", status.GetProperty("name").GetString());
                Assert.Equal("女", status.GetProperty("gender").GetString());

                // 4. 断言落库：PersonnelRecord（部门分组下唯一一条张三）
                var records = _project.Service.GetPersonnelRecordsByGroup(_deptId);
                var rec = Assert.Single(records.Where(r => r.Title == "张三"));
                Assert.Equal("女", rec.Gender);
                Assert.Equal("11010519491231002X", rec.IDCardDocNumber);
                Assert.Equal("1949-12-31", rec.BirthDate);
                Assert.Equal(1, rec.DisplayIndex); // 空部门首条 → DisplayIndex = 1

                // 5. 断言附件落盘 + 临时目录已清理
                string attachFolder = Path.Combine(_project.Service.GetProjectsDateRoot(),
                    $"Project_{_project.ProjectId}", $"Personnel_{_unitId}", $"Card_{rec.Id}");
                Assert.True(Directory.Exists(attachFolder), $"人员附件目录不存在: {attachFolder}");
                Assert.NotEmpty(Directory.GetFiles(attachFolder, "idcard_1_*.jpg"));
                Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "LanPersonnel", tempId)),
                    "提交后临时目录应被清理");
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task 提交_未先上传照片_返回未找到照片数据()
        {
            int port = GetFreePort();
            using var service = new LanService(_project.Service, _ocr);
            service.Start(port);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var submit = await http.PostAsync($"http://127.0.0.1:{port}/submit?token={service.Token}",
                    new StringContent("{\"tempId\":\"no-photo-1\"}", Encoding.UTF8, "application/json"));
                var json = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement;
                Assert.False(json.GetProperty("ok").GetBoolean());
                Assert.Contains("未找到照片数据", json.GetProperty("error").GetString());
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task 上传_非法部门分组_被拒绝()
        {
            int port = GetFreePort();
            using var service = new LanService(_project.Service, _ocr);
            service.Start(port);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                // 不存在的分组 id
                var resp = await http.PostAsync(
                    $"http://127.0.0.1:{port}/upload?token={service.Token}&side=front&tempId=t1&groupId=bogus-group",
                    new ByteArrayContent(FakeImageBytes));
                var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
                Assert.False(json.GetProperty("ok").GetBoolean());
                Assert.Contains("非法的部门分组", json.GetProperty("error").GetString());

                // Hazard 模块分组（非 Personnel）同样应被拒绝
                var ym = _project.AddYearMonthGroup("2026年08月");
                var resp2 = await http.PostAsync(
                    $"http://127.0.0.1:{port}/upload?token={service.Token}&side=front&tempId=t2&groupId={ym.Id}",
                    new ByteArrayContent(FakeImageBytes));
                var json2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync()).RootElement;
                Assert.False(json2.GetProperty("ok").GetBoolean());
                Assert.Contains("非法的部门分组", json2.GetProperty("error").GetString());
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task 重复提交_被拒绝且不重复落库()
        {
            int port = GetFreePort();
            _ocr.Blocks = new List<OcrTextBlock>
            {
                new() { Text = "姓名李四" },
                new() { Text = "公民身份号码11010519491231002X" },
                new() { Text = "女" }
            };
            using var service = new LanService(_project.Service, _ocr);
            service.Start(port);
            try
            {
                string baseUrl = $"http://127.0.0.1:{port}";
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string tempId = "dup-test-1";

                var upload = await http.PostAsync(
                    $"{baseUrl}/upload?token={service.Token}&side=front&tempId={tempId}&groupId={_deptId}",
                    new ByteArrayContent(FakeImageBytes));
                Assert.True(JsonDocument.Parse(await upload.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());

                var first = await http.PostAsync($"{baseUrl}/submit?token={service.Token}",
                    new StringContent($"{{\"tempId\":\"{tempId}\"}}", Encoding.UTF8, "application/json"));
                Assert.True(JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());

                // 第二次提交必须被拒绝（幂等）：
                //  - 若消费者尚未消费 → "该照片已提交，请勿重复提交"（_submissionStatuses.TryAdd 幂等登记）
                //  - 若消费者已消费完（_pendingRecords.TryRemove 后）→ "未找到照片数据，请先拍照"
                // 两条路径都不允许重复入队/重复落库——断言 ok=false 与最终只落库一条。
                var second = await http.PostAsync($"{baseUrl}/submit?token={service.Token}",
                    new StringContent($"{{\"tempId\":\"{tempId}\"}}", Encoding.UTF8, "application/json"));
                var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
                Assert.False(secondJson.GetProperty("ok").GetBoolean());
                string secondError = secondJson.GetProperty("error").GetString() ?? "";
                Assert.True(
                    secondError.Contains("重复提交") || secondError.Contains("未找到照片数据"),
                    $"二次提交应被幂等拒绝，实际 error={secondError}");

                await WaitForStatus(http, baseUrl, service.Token, tempId, "completed");
                Assert.Single(_project.Service.GetPersonnelRecordsByGroup(_deptId)); // 只落库一条
            }
            finally
            {
                service.Stop();
            }
        }
    }
}
