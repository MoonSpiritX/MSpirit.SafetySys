// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Peoplemanagement;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// LAN HTTP 服务：手机扫码后通过浏览器拍照上传，实时 OCR 识别并录入人员信息。
    /// 基于 Kestrel (ASP.NET Core) 实现，零 UAC 绑定所有网卡 IP。
    /// </summary>
    public class LanService : IDisposable
    {
        private readonly ProjectDataService _dataService;
        private readonly IOcrService _ocrService;
        private readonly ConcurrentDictionary<string, PendingRecord> _pendingRecords = new();
        private readonly ConcurrentDictionary<string, SubmissionStatus> _submissionStatuses = new();
        private readonly Channel<string> _submissionChannel = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true });
        private readonly string _htmlPage;
        public const int MaxUploadBytes = 20 * 1024 * 1024;   // 图片上传上限 20MB
        public const int MaxJsonBodyBytes = 1024 * 1024;      // JSON 请求体上限 1MB
        public const int MaxLocationLength = 100;             // 隐患位置上限（字）
        public const int MaxDescriptionLength = 500;          // 隐患描述上限（字）
        public const int MaxMeasureLength = 500;              // 整改措施上限（字）
        private const int PendingTtlMinutes = 30;             // 待确认记录 TTL（分钟）
        private const int StatusTtlMinutes = 30;              // 提交状态记录 TTL（分钟）
        private int _port;
        private bool _running;
        private string _boundIpAddress = "127.0.0.1";
        private CancellationTokenSource? _cts;
        private WebApplication? _app;
        private string _token = "";

        /// <summary>当前服务所属部门信息（手机端通过 /ping 获取）</summary>
        public string GroupName { get; set; } = "";
        public string UnitName { get; set; } = "";

        /// <summary>确认记录后触发（参数：groupId, recordId）</summary>
        public event Action<string, string>? RecordConfirmed;

        /// <summary>收到新图片并完成 OCR 后触发（用于界面显示已录入计数）</summary>
        public event Action? PendingUpdated;

        public bool IsRunning => Volatile.Read(ref _running);
        public int Port => _port;
        public string BoundIpAddress => _boundIpAddress;
        public string Token => _token;
        public int PendingCount => _pendingRecords.Count;

        public LanService(ProjectDataService dataService, IOcrService ocrService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            _htmlPage = LoadEmbeddedHtml();
        }

        // ==================== 启动 / 停止 ====================

        public void Start(int port)
        {
            if (_running) return;

            _port = port;
            var lanIp = GetLocalIPAddress();
            _boundIpAddress = lanIp;

            _cts = new CancellationTokenSource();

            // 生成访问令牌（拼入二维码 URL，手机端所有请求需携带）
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            // Kestrel 绑定 http://0.0.0.0:{port}，零 UAC 即可接受所有网卡的请求
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = "LanPersonnelService",
                Args = Array.Empty<string>()
            });
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
            builder.WebHost.UseKestrel();

            _app = builder.Build();

            // 鉴权中间件：请求必须携带正确 token，否则一律 401（二维码 URL 已含 token）
            _app.Use(async (ctx, next) =>
            {
                if (!IsAuthorized(ctx))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "未授权访问，请重新扫描二维码" });
                    return;
                }
                await next();
            });

            // ── 路由映射（闭包捕获 this 实例，避免静态方法无法访问字段） ──
            var svc = this;
            _app.MapGet("/", (HttpContext ctx) => ServePage(ctx));
            _app.MapGet("/ping", (HttpContext ctx) => ctx.Response.WriteAsJsonAsync(new { ok = true, groupName = svc.GroupName, unitName = svc.UnitName }));
            _app.MapGet("/departments", (HttpContext ctx) => HandleDepartments(ctx));
            _app.MapPost("/upload", (HttpContext ctx) => HandleUpload(ctx));
            _app.MapPost("/submit", (HttpContext ctx) => HandleSubmit(ctx));
            _app.MapGet("/status", (HttpContext ctx) => HandleStatus(ctx));
            _app.MapPost("/discard", (HttpContext ctx) => HandleDiscard(ctx));

            // 启动 Kestrel：同步等待绑定完成，端口被占会在此抛出（供端口探测捕获下一个端口）。
            // 原 fire-and-forget 导致绑定失败不感知、多个服务窗口都显示同一端口（2026-08-12 修复）
            try
            {
                Task.Run(() => _app.StartAsync(_cts.Token)).GetAwaiter().GetResult();
            }
            catch
            {
                try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
                _app = null;
                throw;
            }

            // 启动提交消费者
            _ = SubmissionConsumerLoop(_cts.Token);

            Volatile.Write(ref _running, true);
            Log.Information("LanService started on port {Port} (Kestrel, bound 0.0.0.0)", port);
        }

        /// <summary>校验请求是否携带正确的访问令牌（恒定时间比较，防时序攻击）</summary>
        private bool IsAuthorized(HttpContext ctx)
        {
            if (string.IsNullOrEmpty(_token)) return false;
            string? supplied = ctx.Request.Query["token"].FirstOrDefault();
            if (string.IsNullOrEmpty(supplied)) return false;
            var a = Encoding.UTF8.GetBytes(supplied);
            var b = Encoding.UTF8.GetBytes(_token);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        public void Stop()
        {
            _cts?.Cancel();
            Volatile.Write(ref _running, false);

            var app = Interlocked.Exchange(ref _app, null);
            if (app != null)
            {
                // 不阻塞调用线程（Stop() 从 UI 线程调用），fire-and-forget 关闭 Kestrel
                _ = Task.Run(() => { try { app.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { /* 后台服务停止失败可忽略 */ } });
            }

            // 清理本实例未提交照片的临时目录（只删本实例 tempId 对应的目录，不影响其他窗口的服务）
            foreach (var pending in _pendingRecords.Values)
            {
                if (IsValidTempId(pending.TempId))
                    TryDeleteTempDirectory(Path.Combine(Path.GetTempPath(), "LanPersonnel", pending.TempId));
            }
            _pendingRecords.Clear();
            _submissionStatuses.Clear();
            Log.Information("LanService stopped");
        }

        // ==================== 路由处理 ====================

        /// <summary>返回拍照页面（禁止缓存，手机浏览器启发式缓存会加载旧页面）</summary>
        private async Task ServePage(HttpContext ctx)
        {
            ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(_htmlPage);
        }

        /// <summary>返回同单位下部门列表</summary>
        private async Task HandleDepartments(HttpContext ctx)
        {
            var groupId = ctx.Request.Query["groupId"].FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(groupId))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "缺少 groupId" });
                return;
            }

            try
            {
                var unitId = _dataService.GetPersonnelUnitGroupId(groupId);
                var allGroups = _dataService.GetAllGroups("");
                var departments = allGroups
                    .Where(g => g.ParentId == unitId)
                    .Select(g => new { id = g.Id, name = g.Name })
                    .ToList();

                await ctx.Response.WriteAsJsonAsync(new { ok = true, departments });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "LanService: 获取部门列表失败");
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "获取部门列表失败" });
            }
        }

        /// <summary>接收图片 → OCR 识别 → 暂存结果 → 返回识别字段</summary>
        private async Task HandleUpload(HttpContext ctx)
        {
            var query = ctx.Request.Query;
            var side = query["side"].FirstOrDefault() ?? "front";
            var tempId = query["tempId"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
            var groupId = query["groupId"].FirstOrDefault() ?? "";

            if (side != "front" && side != "back")
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "非法的照片面" });
                return;
            }
            if (!IsValidTempId(tempId))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "非法的 tempId" });
                return;
            }
            if (!IsValidPersonnelGroupId(groupId))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "非法的部门分组" });
                return;
            }

            // 读取 POST 体中的图片二进制数据（限量 20MB）
            byte[]? fileData = await ReadBodyWithLimitAsync(ctx, MaxUploadBytes);
            if (fileData == null)
            {
                ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "图片文件过大（上限 20MB）" });
                return;
            }

            if (fileData.Length == 0)
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "未收到图片数据" });
                return;
            }

            // 保存临时图片文件
            string tempDir = Path.Combine(Path.GetTempPath(), "LanPersonnel", tempId);
            Directory.CreateDirectory(tempDir);
            string fileName = $"upload_{DateTime.Now:yyyyMMddHHmmssfff}.jpg";
            string filePath = Path.Combine(tempDir, fileName);
            await File.WriteAllBytesAsync(filePath, fileData);

            // OCR 识别
            var blocks = await _ocrService.RecognizeAsync(filePath);
            var ocrResult = blocks.Count > 0 ? OcrService.ParseFields(blocks) : new OcrResult();

            // 自动检测正反面
            if (blocks.Count > 0)
            {
                var detectedSide = OcrService.DetectIdCardSide(blocks);
                if (detectedSide != null)
                {
                    string newSide = detectedSide == "Front" ? "front" : "back";
                    if (newSide != side)
                    {
                        Log.Information("HandleUpload: 自动纠正正反面 {OldSide}->{NewSide}", side, newSide);
                        side = newSide;
                    }
                }
            }

            // 暂存结果
            var pending = _pendingRecords.GetOrAdd(tempId, _ => new PendingRecord
            {
                TempId = tempId,
                GroupId = groupId,
                CreatedAt = DateTime.Now
            });

            if (side == "back")
            {
                pending.BackResult = ocrResult;
                pending.BackImagePath = filePath;
            }
            else
            {
                pending.FrontResult = ocrResult;
                pending.FrontImagePath = filePath;
            }

            PendingUpdated?.Invoke();
            CleanupStalePending();
            CleanupStaleSubmissionStatuses();

            await ctx.Response.WriteAsJsonAsync(new
            {
                ok = true,
                tempId,
                side,
                ocr = new
                {
                    name = ocrResult.Name,
                    gender = ocrResult.Gender,
                    age = ocrResult.Age,
                    birthDate = ocrResult.BirthDate,
                    idNumber = ocrResult.IDNumber,
                    nativePlace = ocrResult.NativePlace,
                    validFrom = ocrResult.ValidFrom,
                    validTo = ocrResult.ValidTo
                }
            });
        }

        /// <summary>提交录入</summary>
        private async Task HandleSubmit(HttpContext ctx)
        {
            string tempId;
            try
            {
                byte[]? bodyBytes = await ReadBodyWithLimitAsync(ctx, MaxJsonBodyBytes);
                if (bodyBytes == null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "请求体过大" });
                    return;
                }
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bodyBytes));
                tempId = doc.RootElement.GetProperty("tempId").GetString() ?? "";
            }
            catch
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "请求格式错误" });
                return;
            }

            if (!IsValidTempId(tempId))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "非法的 tempId" });
                return;
            }

            if (!_pendingRecords.TryGetValue(tempId, out _))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "未找到照片数据，请先拍照" });
                return;
            }

            // 幂等：首次提交才创建状态条目，重复提交直接拒绝（防止同一 tempId 二次入队导致状态被覆盖）
            if (!_submissionStatuses.TryAdd(tempId, new SubmissionStatus { Status = "processing" }))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "该照片已提交，请勿重复提交" });
                return;
            }

            await _submissionChannel.Writer.WriteAsync(tempId);
            await ctx.Response.WriteAsJsonAsync(new { ok = true, tempId });
        }

        /// <summary>查询录入状态</summary>
        private async Task HandleStatus(HttpContext ctx)
        {
            var tempId = ctx.Request.Query["tempId"].FirstOrDefault() ?? "";

            if (string.IsNullOrEmpty(tempId) || !_submissionStatuses.TryGetValue(tempId, out var status))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = true, status = "unknown" });
                return;
            }

            await ctx.Response.WriteAsJsonAsync(new
            {
                ok = true,
                status = status.Status,
                name = status.Name ?? "",
                gender = status.Gender ?? "",
                groupName = status.GroupName ?? "",
                error = status.ErrorMessage ?? ""
            });
        }

        /// <summary>丢弃当前临时记录</summary>
        private async Task HandleDiscard(HttpContext ctx)
        {
            string tempId;
            try
            {
                byte[]? bodyBytes = await ReadBodyWithLimitAsync(ctx, MaxJsonBodyBytes);
                if (bodyBytes == null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "请求体过大" });
                    return;
                }
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bodyBytes));
                tempId = doc.RootElement.GetProperty("tempId").GetString() ?? "";
            }
            catch
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "请求格式错误" });
                return;
            }

            if (!IsValidTempId(tempId))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "非法的 tempId" });
                return;
            }

            if (!string.IsNullOrEmpty(tempId) && _pendingRecords.TryRemove(tempId, out var pending))
            {
                var dir = Path.Combine(Path.GetTempPath(), "LanPersonnel", tempId);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }

            PendingUpdated?.Invoke();
            await ctx.Response.WriteAsJsonAsync(new { ok = true });
        }

        // ==================== 提交消费者 ====================

        private async Task SubmissionConsumerLoop(CancellationToken ct)
        {
            try
            {
                await foreach (var tempId in _submissionChannel.Reader.ReadAllAsync(ct))
                {
                    await ProcessSubmissionAsync(tempId);
                }
            }
            catch (OperationCanceledException) { /* 请求取消属正常流程 */ }
            catch (Exception ex)
            {
                Log.Warning(ex, "LanService consumer loop error");
            }
        }

        private async Task ProcessSubmissionAsync(string tempId)
        {
            try
            {
                if (!_pendingRecords.TryRemove(tempId, out var pending))
                {
                    _submissionStatuses[tempId].Status = "failed";
                    _submissionStatuses[tempId].ErrorMessage = "未找到临时记录";
                    return;
                }

                var merged = pending.GetMergedResult();
                var groupId = pending.GroupId;

                var existingRecords = _dataService.GetPersonnelRecordsByGroup(groupId);
                int maxDisplayIndex = existingRecords.Count > 0
                    ? existingRecords.Max(r => r.DisplayIndex)
                    : 0;

                var record = new Models.PersonnelRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    GroupId = groupId,
                    Title = merged.Name,
                    Gender = merged.Gender,
                    Age = merged.Age,
                    NativePlace = merged.NativePlace,
                    BirthDate = merged.BirthDate,
                    IDCardDocNumber = merged.IDNumber,
                    IDCardStart = merged.ValidFrom,
                    IDCardEnd = merged.ValidTo,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    YearMonth = DateTime.Now.ToString("yyyy-MM"),
                    DisplayIndex = maxDisplayIndex + 1
                };

                if (!_dataService.AddPersonnelRecord(record))
                {
                    _submissionStatuses[tempId].Status = "failed";
                    _submissionStatuses[tempId].ErrorMessage = "保存记录失败";
                    return;
                }

                try
                {
                    await MoveTempPhotosToAttachments(pending, record.Id);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "LanService: 附件保存失败，回滚记录 {Id}", record.Id);
                    try { _dataService.DeletePersonnelRecord(record.Id); }
                    catch (Exception rollbackEx) { Log.Error(rollbackEx, "LanService: 回滚删除人员记录失败 {Id}", record.Id); }
                    _submissionStatuses[tempId].Status = "failed";
                    _submissionStatuses[tempId].ErrorMessage = "图片附件保存失败";
                    return;
                }

                RecordConfirmed?.Invoke(groupId, record.Id);

                _submissionStatuses[tempId].Status = "completed";
                _submissionStatuses[tempId].Name = record.Title;
                _submissionStatuses[tempId].Gender = record.Gender;
                _submissionStatuses[tempId].RecordId = record.Id;
                _submissionStatuses[tempId].GroupName = GetGroupName(groupId);

                Log.Information("LanService: 后台录入完成 {Name}({Id})", record.Title, record.Id);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "LanService: 后台录入失败 tempId={TempId}", tempId);
                if (_submissionStatuses.TryGetValue(tempId, out var s))
                {
                    s.Status = "failed";
                    s.ErrorMessage = ex.Message;
                }
            }
        }

        // ==================== 附件处理 ====================

        private async Task MoveTempPhotosToAttachments(PendingRecord pending, string recordId)
        {
            var unitGroupId = _dataService.GetPersonnelUnitGroupId(pending.GroupId);
            string attachFolder = Path.Combine(
                _dataService.GetProjectsDateRoot(),
                $"Project_{_dataService.ProjectId}",
                $"Personnel_{unitGroupId}",
                $"Card_{recordId}"
            );
            Directory.CreateDirectory(attachFolder);

            if (pending.FrontImagePath != null && File.Exists(pending.FrontImagePath))
            {
                await MoveToAttachment(pending.FrontImagePath, attachFolder, "idcard", 1, recordId, pending.GroupId);
            }

            if (pending.BackImagePath != null && File.Exists(pending.BackImagePath))
            {
                await MoveToAttachment(pending.BackImagePath, attachFolder, "idcard", 2, recordId, pending.GroupId);
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "LanPersonnel", pending.TempId);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }

        private async Task MoveToAttachment(string sourcePath, string attachFolder, string prefix, int slotIndex, string recordId, string groupId)
        {
            string guid = Guid.NewGuid().ToString("N");
            string fileName = $"{prefix}_{slotIndex}_{guid}.jpg";
            string destPath = Path.Combine(attachFolder, fileName);
            await Task.Run(() => File.Copy(sourcePath, destPath, true));

            string thumbFileName = $"{prefix}_{slotIndex}_{guid}_thumb.jpg";
            string thumbFolder = Path.Combine(attachFolder, "thumb");
            if (!Directory.Exists(thumbFolder))
                Directory.CreateDirectory(thumbFolder);
            string thumbDestPath = Path.Combine(thumbFolder, thumbFileName);
            GenerateThumbnail(sourcePath, thumbDestPath);

            string unitGroupId = _dataService.GetPersonnelUnitGroupId(groupId);
            string relativePath = $"Project_{_dataService.ProjectId}/Personnel_{unitGroupId}/Card_{recordId}/{fileName}";
            string thumbRelativePath = $"Project_{_dataService.ProjectId}/Personnel_{unitGroupId}/Card_{recordId}/thumb/{thumbFileName}";

            var attachment = new Models.Attachment
            {
                Id = Guid.NewGuid().ToString("N"),
                RecordId = recordId,
                FileName = fileName,
                RelativePath = relativePath,
                ThumbnailPath = thumbRelativePath,
                AttachmentType = PersonnelConstants.AttachmentTypeIDCard,
                DisplayOrder = slotIndex,
                FileSize = new FileInfo(destPath).Length,
                MimeType = "image/jpeg"
            };

            _dataService.AddAttachment(attachment);
        }

        private static void GenerateThumbnail(string sourcePath, string thumbDestPath)
        {
            try
            {
                using var input = File.OpenRead(sourcePath);
                using var source = SkiaSharp.SKBitmap.Decode(input);
                if (source == null) return;

                float ratio = (float)300 / source.Width;
                int thumbW = 300;
                int thumbH = (int)(source.Height * ratio);
                if (thumbH < 1) thumbH = 1;

                using var thumbBg = new SkiaSharp.SKBitmap(thumbW, thumbH);
                using var thumbCanvas = new SkiaSharp.SKCanvas(thumbBg);
                using var resized = source.Resize(new SkiaSharp.SKImageInfo(thumbW, thumbH), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear));
                if (resized != null)
                {
                    thumbCanvas.Clear(SkiaSharp.SKColors.White);
                    thumbCanvas.DrawBitmap(resized, 0, 0);
                    using var thumbImage = SkiaSharp.SKImage.FromBitmap(thumbBg);
                    using var thumbData = thumbImage.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);
                    using var thumbFs = File.Create(thumbDestPath);
                    thumbData.SaveTo(thumbFs);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "生成缩略图失败: {Path}", sourcePath);
            }
        }

        // ==================== 工具方法 ====================

        private void CleanupStalePending()
        {
            var cutoff = DateTime.Now.AddMinutes(-PendingTtlMinutes);
            foreach (var kv in _pendingRecords)
            {
                if (kv.Value.CreatedAt < cutoff && _pendingRecords.TryRemove(kv.Key, out var stale))
                {
                    var dir = Path.Combine(Path.GetTempPath(), "LanPersonnel", stale.TempId);
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
            }
        }

        /// <summary>清理超过 TTL 的提交状态记录，防止 _submissionStatuses 无界增长</summary>
        private void CleanupStaleSubmissionStatuses()
        {
            var cutoff = DateTime.Now.AddMinutes(-StatusTtlMinutes);
            foreach (var kv in _submissionStatuses)
            {
                if (kv.Value.CreatedAt < cutoff)
                    _submissionStatuses.TryRemove(kv.Key, out _);
            }
        }

        private string GetGroupName(string groupId)
        {
            try
            {
                var groups = _dataService.GetAllGroups("");
                var group = groups.FirstOrDefault(g => g.Id == groupId);
                return group?.Name ?? groupId;
            }
            catch
            {
                return groupId;
            }
        }

        /// <summary>校验 groupId 是否为当前项目人员模块下的合法分组（防止写入任意/不存在的分组）</summary>
        private bool IsValidPersonnelGroupId(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return false;
            try
            {
                var groups = _dataService.GetAllGroups("");
                return groups.Any(g => g.Id == groupId && g.Module == ModuleNames.Personnel);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "LanService: 校验 groupId 失败 {GroupId}", groupId);
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>获取本机局域网 IP 地址（优先物理网卡出口，避免选中 VPN/隧道/虚拟网卡地址）</summary>
        public static string GetLocalIPAddress()
        {
            // 1) 优先：Up 状态、非环回/隧道、描述不含 VPN/Tunnel/Virtual 等关键字、
            //    且配置了默认网关（即实际局域网出口）的网卡的第一个 IPv4
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                string? desc = ni.Description;
                if (!string.IsNullOrEmpty(desc) &&
                    (desc.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                     desc.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                     desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                     desc.Contains("Tap", StringComparison.OrdinalIgnoreCase) ||
                     desc.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                     desc.Contains("WSL", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var props = ni.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue; // 无网关 = 非默认出口

                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !addr.Address.ToString().StartsWith("169.254."))
                    {
                        return addr.Address.ToString();
                    }
                }
            }

            // 2) 兜底：原逻辑（主机名 DNS 条目，跳过 169.254 链路本地）
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    if (ip.ToString().StartsWith("169.254.")) continue;
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }

        private static readonly Regex TempIdPattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

        /// <summary>校验客户端传入的标识符（tempId/photoId），仅允许字母数字下划线连字符，防路径穿越</summary>
        public static bool IsValidTempId(string? id) =>
            !string.IsNullOrEmpty(id) && TempIdPattern.IsMatch(id);

        /// <summary>校验照片类型（hazard/remedy）</summary>
        public static bool IsValidPhotoType(string? type) =>
            type is "hazard" or "remedy";

        private static readonly Regex YearMonthPattern = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
        private static readonly HashSet<string> HazardLevelSet = new(StringComparer.Ordinal)
        {
            "一般隐患", "较大隐患", "重大隐患"
        };

        /// <summary>校验年月格式（yyyy-MM，月份 01~12）</summary>
        public static bool IsValidYearMonth(string? ym) =>
            !string.IsNullOrEmpty(ym) && YearMonthPattern.IsMatch(ym);

        /// <summary>校验隐患级别（与手机端下拉选项一致）</summary>
        public static bool IsValidHazardLevel(string? level) =>
            !string.IsNullOrEmpty(level) && HazardLevelSet.Contains(level);

        /// <summary>
        /// 限制读取请求体，超过 maxBytes 返回 null（同时校验 ContentLength 与分块传输）
        /// </summary>
        public static async Task<byte[]?> ReadBodyWithLimitAsync(HttpContext ctx, int maxBytes)
        {
            if (ctx.Request.ContentLength > maxBytes) return null;

            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int total = 0;
            while (true)
            {
                int read = await ctx.Request.Body.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) break;
                total += read;
                if (total > maxBytes) return null;
                ms.Write(buffer, 0, read);
            }
            return ms.ToArray();
        }

        /// <summary>安全删除 LAN 临时目录（校验解析后路径仍位于系统临时目录内）</summary>
        public static void TryDeleteTempDirectory(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                string fullDir = Path.GetFullPath(dir);
                if (fullDir.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullDir))
                    Directory.Delete(fullDir, true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "清理 LAN 临时目录失败: {Dir}", dir);
            }
        }

        // ==================== HTML 页面 ====================

        private static string LoadEmbeddedHtml()
        {
            var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "lan_page.html");
            if (File.Exists(htmlPath))
                return File.ReadAllText(htmlPath, Encoding.UTF8);

            var srcPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\..\Resources\lan_page.html");
            if (File.Exists(srcPath))
                return File.ReadAllText(srcPath, Encoding.UTF8);

            Log.Warning("LanService: HTML page file not found, using fallback minimal page");
            return "<!DOCTYPE html><html><body><h2>页面加载失败</h2><p>请检查 Resources/lan_page.html 是否存在</p></body></html>";
        }
    }

    /// <summary>后台提交处理的状态跟踪</summary>
    public class SubmissionStatus
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Status { get; set; } = "processing";
        public string? Name { get; set; }
        public string? Gender { get; set; }
        public string? GroupName { get; set; }
        public string? RecordId { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
