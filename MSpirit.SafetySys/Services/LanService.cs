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
    /// 托管骨架/鉴权/设备事件继承自 <see cref="LanServiceBase"/>（2026-08-16 抽取）。
    /// </summary>
    public class LanService : LanServiceBase
    {
        private readonly ProjectDataService _dataService;
        private readonly IOcrService _ocrService;
        private readonly ConcurrentDictionary<string, PendingRecord> _pendingRecords = new();
        private readonly ConcurrentDictionary<string, SubmissionStatus> _submissionStatuses = new();
        private readonly Channel<string> _submissionChannel = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true });
        public const int MaxLocationLength = 100;             // 隐患位置上限（字）
        public const int MaxDescriptionLength = 500;          // 隐患描述上限（字）
        public const int MaxMeasureLength = 500;              // 整改措施上限（字）
        private const int PendingTtlMinutes = 30;             // 待确认记录 TTL（分钟）
        private const int StatusTtlMinutes = 30;              // 提交状态记录 TTL（分钟）

        /// <summary>当前服务所属部门信息（手机端通过 /ping 获取）</summary>
        public string GroupName { get; set; } = "";
        public string UnitName { get; set; } = "";

        /// <summary>确认记录后触发（参数：groupId, recordId）</summary>
        public event Action<string, string>? RecordConfirmed;

        /// <summary>收到新图片并完成 OCR 后触发（用于界面显示已录入计数）</summary>
        public event Action? PendingUpdated;

        public int PendingCount => _pendingRecords.Count;

        protected override string ApplicationName => "LanPersonnelService";
        protected override string LogPrefix => "Lan";
        protected override string ServiceDisplayName => "人员录入";

        public LanService(ProjectDataService dataService, IOcrService ocrService) : base("lan_page.html")
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        }

        // ==================== 路由注册（Kestrel/鉴权骨架在 LanServiceBase.Start） ====================

        protected override void MapRoutes(WebApplication app)
        {
            var svc = this;
            app.MapGet("/", (HttpContext ctx) => ServePage(ctx));
            app.MapGet("/ping", (HttpContext ctx) => ctx.Response.WriteAsJsonAsync(new { ok = true, groupName = svc.GroupName, unitName = svc.UnitName }));
            app.MapGet("/departments", (HttpContext ctx) => HandleDepartments(ctx));
            app.MapPost("/upload", (HttpContext ctx) => HandleUpload(ctx));
            app.MapPost("/submit", (HttpContext ctx) => HandleSubmit(ctx));
            app.MapGet("/status", (HttpContext ctx) => HandleStatus(ctx));
            app.MapPost("/discard", (HttpContext ctx) => HandleDiscard(ctx));
        }

        protected override void OnStopped()
        {
            // 清理本实例未提交照片的临时目录（只删本实例 tempId 对应的目录，不影响其他窗口的服务）
            int pendingCount = _pendingRecords.Count;
            int statusCount = _submissionStatuses.Count;
            foreach (var pending in _pendingRecords.Values)
            {
                if (IsValidTempId(pending.TempId))
                    TryDeleteTempDirectory(Path.Combine(Path.GetTempPath(), "LanPersonnel", pending.TempId));
            }
            _pendingRecords.Clear();
            _submissionStatuses.Clear();
            Log.Information("LanService stopped（清理暂存 {PendingCount} 条、状态 {StatusCount} 条，端口 {Port}）",
                pendingCount, statusCount, _port);
        }

        protected override void OnStarted()
        {
            // 启动提交消费者（Kestrel 已就绪、_cts/_token 可用后）
            _ = SubmissionConsumerLoop(_cts!.Token);
        }

        // ==================== 路由处理 ====================

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
                Log.Debug("[Lan] 部门列表：groupId={GroupId} 单位={UnitId} 返回 {Count} 个部门", groupId, unitId, departments.Count);
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
            Log.Debug("[Lan] 收到上传：side={Side} tempId={TempId} groupId={GroupId}", side, tempId, groupId);

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
            Log.Debug("[Lan] 图片已暂存：{Path} 大小={Size}字节", filePath, fileData.Length);

            // OCR 识别
            var ocrSw = System.Diagnostics.Stopwatch.StartNew();
            var blocks = await _ocrService.RecognizeAsync(filePath);
            ocrSw.Stop();
            var ocrResult = blocks.Count > 0 ? OcrService.ParseFields(blocks) : new OcrResult();
            Log.Information("[Lan] OCR 完成：引擎={Engine} 块数={Count} 耗时={ElapsedMs}ms 姓名={Name} 身份证号={IdNumber}",
                _ocrService.Name, blocks.Count, ocrSw.ElapsedMilliseconds, ocrResult.Name, ocrResult.IDNumber);

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
                Log.Debug("[Lan] 请求 JSON 解析失败（返回格式错误）");
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "请求格式错误" });
                return;
            }
            Log.Debug("[Lan] 收到提交：tempId={TempId}", tempId);

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
                Log.Warning("[Lan] 重复提交被拒绝：tempId={TempId}", tempId);
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "该照片已提交，请勿重复提交" });
                return;
            }

            await _submissionChannel.Writer.WriteAsync(tempId);
            Log.Debug("[Lan] 提交已入队：tempId={TempId}", tempId);
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
            Log.Debug("[Lan] 状态查询：tempId={TempId} 状态={Status} 姓名={Name}", tempId, status.Status, status.Name);
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
                Log.Debug("[Lan] 请求 JSON 解析失败（返回格式错误）");
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
                Log.Debug("[Lan] 已丢弃暂存记录：tempId={TempId}", tempId);
            }
            else
            {
                Log.Debug("[Lan] 丢弃请求未找到暂存：tempId={TempId}", tempId);
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
                Log.Debug("[Lan] 后台录入开始：tempId={TempId} 合并结果 姓名={Name} 性别={Gender} 身份证号={IdNumber} 部门={GroupId}",
                    tempId, merged.Name, merged.Gender, merged.IDNumber, groupId);

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

                Log.Information("LanService: 后台录入完成 {Name}({Id}) 部门={GroupId} DisplayIndex={DisplayIndex}",
                    record.Title, record.Id, groupId, record.DisplayIndex);
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
                Log.Debug("[Lan] 查询分组名称失败，回退使用分组 ID");
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
