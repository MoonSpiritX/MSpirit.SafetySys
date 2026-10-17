// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

// HazardLanService（隐患 LAN 录入）完整业务流测试（第 3 步：补业务流落库链路，docs/LAN服务技术文档.md 已知缺口）。
// 真实 Kestrel + 真实临时 SQLite：
//   /upload-photo（暂存照片）→ /submit（校验表单 → 建/取日期分组 → 落库 Record → 附件落盘 → 清临时目录）。
//  + 错误路径：非法照片类型 / 非法年月 / 非法日期 / 非法级别 / 缺位置描述 / 未上传照片的标识。
// 只用随机空闲端口与 TestProject 临时库，绝不触碰真实数据目录。

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MS_SafetyManagementSystem.Services;
using Xunit;

namespace MSpirit.SafetySys.Tests
{
    [Collection("LanService")]
    public class HazardLanServiceFlowTests : IDisposable
    {
        private readonly TestProject _project;
        private const string YearMonth = "2026-08"; // 与 AddYearMonthGroup 名称匹配（2026年08月）

        public HazardLanServiceFlowTests()
        {
            _project = new TestProject();
        }

        public void Dispose() => _project.Dispose();

        private static int GetFreePort()
        {
            using var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }

        private static byte[] FakeImageBytes => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };

        private static async Task<JsonElement> PostJson(HttpClient http, string url, object body)
        {
            string json = JsonSerializer.Serialize(body);
            var resp = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        }

        [Fact]
        public async Task 上传照片提交_落库隐患记录且附件落盘()
        {
            int port = GetFreePort();
            string recordId = "";
            using var service = new HazardLanService(_project.Service, YearMonth, "2026年08月");
            service.RecordConfirmed += (groupId, rid) => recordId = rid;
            service.Start(port);
            try
            {
                string baseUrl = $"http://127.0.0.1:{port}";
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string hazardTempId = "photo-hazard-1";
                string remedyTempId = "photo-remedy-1";

                // 1. 上传隐患照片（暂存到临时目录）
                var up = await http.PostAsync(
                    $"{baseUrl}/upload-photo?token={service.Token}&type=hazard&tempId={hazardTempId}",
                    new ByteArrayContent(FakeImageBytes));
                var upJson = JsonDocument.Parse(await up.Content.ReadAsStringAsync()).RootElement;
                Assert.True(upJson.GetProperty("ok").GetBoolean());

                // 2. 上传整改照片
                var up2 = await http.PostAsync(
                    $"{baseUrl}/upload-photo?token={service.Token}&type=remedy&tempId={remedyTempId}",
                    new ByteArrayContent(FakeImageBytes));
                Assert.True(JsonDocument.Parse(await up2.Content.ReadAsStringAsync()).RootElement.GetProperty("ok").GetBoolean());

                // 3. 提交表单 → 落库
                var submit = await PostJson(http, $"{baseUrl}/submit?token={service.Token}", new
                {
                    groupId = "unit-group-id",          // 仅透传事件，不参与落库
                    date = 15,
                    location = "配电箱未接地",
                    level = "一般隐患",
                    description = "配电箱外壳未做接地保护",
                    measure = "重新敷设接地线并检测",
                    hazardPhotoId = hazardTempId,
                    remedyPhotoId = remedyTempId
                });
                Assert.True(submit.GetProperty("ok").GetBoolean());
                string rid = submit.GetProperty("recordId").GetString() ?? "";
                Assert.False(string.IsNullOrEmpty(rid));
                Assert.Equal(rid, recordId); // RecordConfirmed 事件携带同一 recordId

                // 4. 断言落库：日期分组下唯一一条记录
                string dateGroupId = _project.Service.GetOrCreateDateGroup(YearMonth, 15, null);
                var records = _project.Service.GetRecordsByGroup(dateGroupId);
                var rec = Assert.Single(records);
                Assert.Equal("[一般隐患] 配电箱未接地", rec.Title);
                Assert.Equal("一般隐患", rec.HazardLevel);
                Assert.Equal("配电箱未接地", rec.Location);
                Assert.Equal("配电箱外壳未做接地保护", rec.Description);
                Assert.Equal("重新敷设接地线并检测", rec.Measure);
                Assert.Equal(YearMonth, rec.YearMonth);
                Assert.Equal("2026-08-15", JsonDocument.Parse(rec.ExtraData).RootElement.GetProperty("FullDate").GetString());

                // 5. 断言附件落盘 + 临时目录已清理
                string attachFolder = Path.Combine(_project.Service.GetProjectsDateRoot(),
                    $"Project_{_project.ProjectId}", $"Hazard_{YearMonth}", $"Card_{rid}");
                Assert.True(Directory.Exists(attachFolder), $"隐患附件目录不存在: {attachFolder}");
                Assert.Single(Directory.GetFiles(attachFolder, "Hazard-1_*.jpg"));
                Assert.Single(Directory.GetFiles(attachFolder, "Remedy-2_*.jpg"));
                Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "HazardLan", hazardTempId)),
                    "提交后隐患照片临时目录应被清理");
                Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "HazardLan", remedyTempId)),
                    "提交后整改照片临时目录应被清理");
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task 上传_非法照片类型_被拒绝()
        {
            int port = GetFreePort();
            using var service = new HazardLanService(_project.Service, YearMonth, "2026年08月");
            service.Start(port);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var resp = await http.PostAsync(
                    $"http://127.0.0.1:{port}/upload-photo?token={service.Token}&type=front&tempId=t1",
                    new ByteArrayContent(FakeImageBytes));
                var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
                Assert.False(json.GetProperty("ok").GetBoolean());
                Assert.Contains("非法的照片类型", json.GetProperty("error").GetString());
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task 提交_非法日期_被拒绝()
        {
            int port = GetFreePort();
            using var service = new HazardLanService(_project.Service, YearMonth, "2026年08月");
            service.Start(port);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                // 2026-08 只有 31 天
                var submit = await PostJson(http, $"http://127.0.0.1:{port}/submit?token={service.Token}", new
                {
                    groupId = "g",
                    date = 32,
                    location = "x",
                    level = "一般隐患",
                    description = "y",
                    measure = "z",
                    hazardPhotoId = "valid-id-1",
                    remedyPhotoId = "valid-id-2"
                });
                Assert.False(submit.GetProperty("ok").GetBoolean());
                Assert.Contains("日期无效", submit.GetProperty("error").GetString());
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task 提交_非法隐患级别_被拒绝()
        {
            int port = GetFreePort();
            using var service = new HazardLanService(_project.Service, YearMonth, "2026年08月");
            service.Start(port);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var submit = await PostJson(http, $"http://127.0.0.1:{port}/submit?token={service.Token}", new
                {
                    groupId = "g",
                    date = 15,
                    location = "x",
                    level = "轻微隐患", // 非法级别
                    description = "y",
                    measure = "z",
                    hazardPhotoId = "valid-id-1",
                    remedyPhotoId = "valid-id-2"
                });
                Assert.False(submit.GetProperty("ok").GetBoolean());
                Assert.Contains("隐患级别无效", submit.GetProperty("error").GetString());
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task 提交_缺少位置或描述_被拒绝()
        {
            int port = GetFreePort();
            using var service = new HazardLanService(_project.Service, YearMonth, "2026年08月");
            service.Start(port);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var submit = await PostJson(http, $"http://127.0.0.1:{port}/submit?token={service.Token}", new
                {
                    groupId = "g",
                    date = 15,
                    location = "",
                    level = "一般隐患",
                    description = "",
                    measure = "z",
                    hazardPhotoId = "valid-id-1",
                    remedyPhotoId = "valid-id-2"
                });
                Assert.False(submit.GetProperty("ok").GetBoolean());
                Assert.Contains("不能为空", submit.GetProperty("error").GetString());
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task 提交_照片标识未上传过_附件跳过但记录仍落库()
        {
            int port = GetFreePort();
            using var service = new HazardLanService(_project.Service, YearMonth, "2026年08月");
            service.Start(port);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var submit = await PostJson(http, $"http://127.0.0.1:{port}/submit?token={service.Token}", new
                {
                    groupId = "g",
                    date = 20,
                    location = "洞口未盖板",
                    level = "较大隐患",
                    description = "临边洞口无防护盖板",
                    measure = "加装盖板并设警示",
                    hazardPhotoId = "never-uploaded-1",  // 合法格式但从未上传
                    remedyPhotoId = "never-uploaded-2"
                });
                Assert.True(submit.GetProperty("ok").GetBoolean());

                string dateGroupId = _project.Service.GetOrCreateDateGroup(YearMonth, 20, null);
                var rec = Assert.Single(_project.Service.GetRecordsByGroup(dateGroupId));
                Assert.Equal("[较大隐患] 洞口未盖板", rec.Title);

                // 未上传照片 → HandleSubmit 会先 CreateDirectory 再按缓存跳过保存（见 SavePhotoFromTemp 调用处），
                // 因此附件目录可能被创建，但目录内不得有任何 Hazard/Remedy 附件文件。
                string attachFolder = Path.Combine(_project.Service.GetProjectsDateRoot(),
                    $"Project_{_project.ProjectId}", $"Hazard_{YearMonth}", $"Card_{rec.Id}");
                if (Directory.Exists(attachFolder))
                {
                    Assert.Empty(Directory.GetFiles(attachFolder, "Hazard-*.jpg"));
                    Assert.Empty(Directory.GetFiles(attachFolder, "Remedy-*.jpg"));
                }
            }
            finally
            {
                service.Stop();
            }
        }
    }
}
