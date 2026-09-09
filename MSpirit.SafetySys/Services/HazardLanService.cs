// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// 隐患手机录入 HTTP 服务（基于 Kestrel，零 UAC 绑定）。
    /// 手机扫码拍照录入隐患信息，不涉及 OCR。
    /// 托管骨架/鉴权/设备事件继承自 <see cref="LanServiceBase"/>（2026-08-16 抽取）。
    /// </summary>
    public class HazardLanService : LanServiceBase
    {
        private readonly ProjectDataService _dataService;
        private readonly string _yearMonth;
        private readonly string _groupName;
        private readonly ConcurrentDictionary<string, string> _photoCache = new(); // tempId -> filePath
        private int _entryCount;
        private const int StaleTempTtlMinutes = 30;   // 残留临时目录 TTL（分钟）

        public string GroupName => _groupName;
        public string YearMonth => _yearMonth;

        /// <summary>确认记录后触发（参数：groupId, recordId）</summary>
        public event Action<string, string>? RecordConfirmed;

        public int EntryCount => Volatile.Read(ref _entryCount);

        protected override string ApplicationName => "HazardLanService";
        protected override string LogPrefix => "HazardLan";
        protected override string ServiceDisplayName => "隐患录入";

        public HazardLanService(ProjectDataService dataService, string yearMonth, string groupName) : base("hazard_lan_page.html")
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _yearMonth = yearMonth;
            _groupName = groupName;
        }

        // ==================== 路由注册（Kestrel/鉴权骨架在 LanServiceBase.Start） ====================

        protected override void MapRoutes(WebApplication app)
        {
            var svc = this;
            app.MapGet("/", (HttpContext ctx) => ServePage(ctx));
            app.MapGet("/ping", (HttpContext ctx) =>
                ctx.Response.WriteAsJsonAsync(new { ok = true, groupName = svc._groupName, yearMonth = svc._yearMonth }));
            app.MapGet("/dates", (HttpContext ctx) => HandleDates(ctx));
            app.MapPost("/upload-photo", (HttpContext ctx) => HandleUploadPhoto(ctx));
            app.MapPost("/submit", (HttpContext ctx) => HandleSubmit(ctx));
        }

        protected override void OnStarted()
        {
            // 清理之前崩溃/异常退出遗留的孤儿临时目录（超过 TTL 才删，不影响其他窗口较新的照片）
            CleanupStaleTempDirectories();
        }

        protected override void OnStopped()
        {
            // 清理本实例未提交照片的临时目录（按缓存路径去重，不影响其他窗口的服务）
            int cachedCount = _photoCache.Count;
            foreach (var dir in _photoCache.Values
                         .Select(p => Path.GetDirectoryName(p))
                         .Where(d => !string.IsNullOrEmpty(d))
                         .Distinct())
            {
                TryDeleteTempDirectory(dir!);
            }
            _photoCache.Clear();
            Log.Information("HazardLanService stopped（清理照片缓存 {CachedCount} 项，端口 {Port}）", cachedCount, _port);
        }

        // ==================== 路由 ====================

        /// <summary>返回当月日期列表（1~当月天数）</summary>
        private async Task HandleDates(HttpContext ctx)
        {
            var ym = ctx.Request.Query["yearMonth"].FirstOrDefault() ?? _yearMonth;
            if (!LanService.IsValidYearMonth(ym))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "年月格式无效" });
                return;
            }
            int daysInMonth = DateTime.DaysInMonth(
                int.Parse(ym.Substring(0, 4)),
                int.Parse(ym.Substring(5, 2)));
            var today = DateTime.Now;
            int defaultDay = (ym == $"{today:yyyy-MM}") ? today.Day : daysInMonth;

            var dates = Enumerable.Range(1, daysInMonth)
                .Select(d => new { day = d, label = $"{ym.Substring(5, 2)}月{d}日" })
                .ToList();

            await ctx.Response.WriteAsJsonAsync(new { ok = true, dates, defaultDay });
            Log.Debug("[HazardLan] 日期列表：yearMonth={YM} 天数={Days} 默认日={DefaultDay}", ym, daysInMonth, defaultDay);
        }

        /// <summary>接收隐患/整改照片，存到临时目录</summary>
        private async Task HandleUploadPhoto(HttpContext ctx)
        {
            var type = ctx.Request.Query["type"].FirstOrDefault() ?? "hazard";
            var tempId = ctx.Request.Query["tempId"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
            Log.Debug("[HazardLan] 收到照片上传：type={Type} tempId={TempId}", type, tempId);

            if (!LanService.IsValidPhotoType(type))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "非法的照片类型" });
                return;
            }
            if (!LanService.IsValidTempId(tempId))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "非法的 tempId" });
                return;
            }

            byte[]? fileData = await LanService.ReadBodyWithLimitAsync(ctx, LanService.MaxUploadBytes);
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

            string tempDir = Path.Combine(Path.GetTempPath(), "HazardLan", tempId);
            Directory.CreateDirectory(tempDir);
            string fileName = $"{type}_{DateTime.Now:yyyyMMddHHmmssfff}.jpg";
            string filePath = Path.Combine(tempDir, fileName);
            await File.WriteAllBytesAsync(filePath, fileData);

            _photoCache[$"{tempId}_{type}"] = filePath;
            Log.Debug("[HazardLan] 照片已暂存：type={Type} tempId={TempId} 路径={Path} 大小={Size}字节", type, tempId, filePath, fileData.Length);

            // 上传时顺带自愈：清除历史崩溃残留（与人员服务 CleanupStalePending 同口径）
            CleanupStaleTempDirectories();

            await ctx.Response.WriteAsJsonAsync(new { ok = true, tempId, type, fileName });
        }

        /// <summary>提交隐患记录：收集表单 + 照片 → 写入 DB</summary>
        private async Task HandleSubmit(HttpContext ctx)
        {
            byte[]? bodyBytes = await LanService.ReadBodyWithLimitAsync(ctx, LanService.MaxJsonBodyBytes);
            if (bodyBytes == null)
            {
                ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "请求体过大" });
                return;
            }

            JsonElement json;
            try { json = JsonDocument.Parse(Encoding.UTF8.GetString(bodyBytes)).RootElement; }
            catch { await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "请求格式错误" }); return; }

            var groupId = json.GetProperty("groupId").GetString() ?? "";
            var dateDay = json.GetProperty("date").GetInt32();
            var location = json.GetProperty("location").GetString() ?? "";
            var level = json.GetProperty("level").GetString() ?? "";
            var description = json.GetProperty("description").GetString() ?? "";
            var measure = json.GetProperty("measure").GetString() ?? "";
            var hazardPhotoId = json.GetProperty("hazardPhotoId").GetString() ?? "";
            var remedyPhotoId = json.GetProperty("remedyPhotoId").GetString() ?? "";
            Log.Debug("[HazardLan] 收到提交：groupId={GroupId} 日期={Day} 级别={Level} 位置={Location} 描述长度={DescLen} 措施长度={MeasLen}",
                groupId, dateDay, level, location, description.Length, measure.Length);

            if (!LanService.IsValidTempId(hazardPhotoId) || !LanService.IsValidTempId(remedyPhotoId))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "非法的照片标识" });
                return;
            }

            if (string.IsNullOrEmpty(location) || string.IsNullOrEmpty(description))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "隐患位置和描述不能为空" });
                return;
            }

            // 日期范围校验（1~当月天数）
            if (!LanService.IsValidYearMonth(_yearMonth))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "年月格式无效" });
                return;
            }
            int daysInMonth = DateTime.DaysInMonth(
                int.Parse(_yearMonth.Substring(0, 4)),
                int.Parse(_yearMonth.Substring(5, 2)));
            if (dateDay < 1 || dateDay > daysInMonth)
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = $"日期无效（本月 1~{daysInMonth} 日）" });
                return;
            }

            // 级别枚举校验
            if (!LanService.IsValidHazardLevel(level))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "隐患级别无效" });
                return;
            }

            // 文本长度校验
            if (location.Length > LanService.MaxLocationLength)
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = $"隐患位置过长（上限 {LanService.MaxLocationLength} 字）" });
                return;
            }
            if (description.Length > LanService.MaxDescriptionLength)
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = $"隐患描述过长（上限 {LanService.MaxDescriptionLength} 字）" });
                return;
            }
            if (measure.Length > LanService.MaxMeasureLength)
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = $"整改措施过长（上限 {LanService.MaxMeasureLength} 字）" });
                return;
            }

            // 计算完整日期，查找或创建对应的日期分组
            string fullDate = $"{_yearMonth}-{dateDay:D2}";
            string recordGroupId = _dataService.GetOrCreateDateGroup(_yearMonth, dateDay, null);
            Log.Debug("[HazardLan] 分组就绪：fullDate={FullDate} recordGroupId={GroupId}", fullDate, recordGroupId);

            // 创建隐患记录（设置专用属性字段，RecordCardView XAML 直接双向绑定这些属性）
            var record = new Models.Record
            {
                Id = Guid.NewGuid().ToString("N"),
                GroupId = recordGroupId,
                Title = $"[{level}] {location}",
                Content = $"隐患描述：{description}\n整改措施：{measure}",
                Description = description,
                Measure = measure,
                Location = location,
                HazardType = level,
                HazardLevel = level,
                YearMonth = _yearMonth,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                DisplayIndex = _dataService.GetMaxDisplayIndex(recordGroupId) + 1,
                Status = "进行"
            };

            // 设置扩展属性（通过 ExtraData JSON）
            var extraData = new Dictionary<string, object>
            {
                ["HazardLocation"] = location,
                ["HazardLevel"] = level,
                ["HazardDescription"] = description,
                ["Measure"] = measure,
                ["FullDate"] = fullDate
            };
            record.ExtraData = JsonSerializer.Serialize(extraData);

            if (!_dataService.AddRecord(record))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "保存记录失败" });
                return;
            }

            // 保存照片附件
            try
            {
                string attachFolder = Path.Combine(
                    _dataService.GetProjectsDateRoot(),
                    $"Project_{_dataService.ProjectId}",
                    $"Hazard_{_yearMonth}",
                    $"Card_{record.Id}"
                );
                Directory.CreateDirectory(attachFolder);

                if (_photoCache.TryGetValue($"{hazardPhotoId}_hazard", out var hazardPath) && File.Exists(hazardPath))
                    await SavePhotoFromTemp(hazardPath, attachFolder, "Hazard", 1, record.Id);
                if (_photoCache.TryGetValue($"{remedyPhotoId}_remedy", out var remedyPath) && File.Exists(remedyPath))
                    await SavePhotoFromTemp(remedyPath, attachFolder, "Remedy", 2, record.Id);

                // 清理临时文件
                var tempHazardDir = Path.Combine(Path.GetTempPath(), "HazardLan", hazardPhotoId);
                var tempRemedyDir = Path.Combine(Path.GetTempPath(), "HazardLan", remedyPhotoId);
                if (Directory.Exists(tempHazardDir)) try { Directory.Delete(tempHazardDir, true); } catch { /* 临时目录清理失败可忽略 */ }
                if (Directory.Exists(tempRemedyDir)) try { Directory.Delete(tempRemedyDir, true); } catch { /* 临时目录清理失败可忽略 */ }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "HazardLanService: 附件保存失败，回滚记录 {Id}", record.Id);
                try { _dataService.DeleteRecord(record.Id); }
                catch (Exception rollbackEx) { Log.Error(rollbackEx, "HazardLanService: 回滚删除隐患记录失败 {Id}", record.Id); }
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "图片附件保存失败" });
                return;
            }

            Interlocked.Increment(ref _entryCount);
            RecordConfirmed?.Invoke(groupId, record.Id);

            Log.Information("HazardLanService: 录入完成 {Title}({Id}) 年月={YM} 日期={FullDate} 已录入总数={EntryCount}",
                record.Title, record.Id, _yearMonth, fullDate, _entryCount);
            await ctx.Response.WriteAsJsonAsync(new
            {
                ok = true,
                recordId = record.Id,
                title = record.Title
            });
        }

        private async Task SavePhotoFromTemp(string sourcePath, string attachFolder, string type, int displayOrder, string recordId)
        {
            string guid = Guid.NewGuid().ToString("N");
            string fileName = $"{type}-{displayOrder}_{guid}.jpg";
            string destPath = Path.Combine(attachFolder, fileName);
            await Task.Run(() => File.Copy(sourcePath, destPath, true));

            // 生成缩略图
            string thumbFileName = $"{type}-{displayOrder}_{guid}_thumb.jpg";
            string thumbFolder = Path.Combine(attachFolder, "thumb");
            Directory.CreateDirectory(thumbFolder);
            string thumbDestPath = Path.Combine(thumbFolder, thumbFileName);
            GenerateThumbnail(destPath, thumbDestPath);

            var fi = new FileInfo(destPath);
            var attachment = new Models.Attachment
            {
                Id = Guid.NewGuid().ToString("N"),
                RecordId = recordId,
                FileName = fileName,
                RelativePath = $"Project_{_dataService.ProjectId}/Hazard_{_yearMonth}/Card_{recordId}/{fileName}",
                ThumbnailPath = $"Project_{_dataService.ProjectId}/Hazard_{_yearMonth}/Card_{recordId}/thumb/{thumbFileName}",
                AttachmentType = type,
                DisplayOrder = displayOrder,
                FileSize = fi.Length,
                MimeType = "image/jpeg"
            };
            _dataService.AddAttachment(attachment);
        }

        /// <summary>
        /// 清理崩溃/异常退出遗留的孤儿临时目录：只删除最后写入超过 TTL 的子目录，
        /// 不影响本实例或其他窗口仍在使用的较新照片。
        /// </summary>
        private void CleanupStaleTempDirectories()
        {
            try
            {
                string hazardTempRoot = Path.Combine(Path.GetTempPath(), "HazardLan");
                if (!Directory.Exists(hazardTempRoot)) return;

                var cutoff = DateTime.Now.AddMinutes(-StaleTempTtlMinutes);
                foreach (var dir in Directory.GetDirectories(hazardTempRoot))
                {
                    try
                    {
                        if (Directory.GetLastWriteTime(dir) < cutoff)
                            LanService.TryDeleteTempDirectory(dir);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "HazardLanService: 清理残留目录失败: {Dir}", dir);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "HazardLanService: 扫描残留临时目录失败");
            }
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
    }
}
