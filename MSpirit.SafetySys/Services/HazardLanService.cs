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
    /// </summary>
    public class HazardLanService : IDisposable
    {
        private readonly ProjectDataService _dataService;
        private readonly string _yearMonth;
        private readonly string _groupName;
        private readonly ConcurrentDictionary<string, string> _photoCache = new(); // tempId -> filePath
        private int _port;
        private bool _running;
        private string _boundIpAddress = "127.0.0.1";
        private CancellationTokenSource? _cts;
        private WebApplication? _app;
        private int _entryCount;
        private readonly string _htmlPage;
        private string _token = "";
        private const int StaleTempTtlMinutes = 30;   // 残留临时目录 TTL（分钟）

        public string GroupName => _groupName;
        public string YearMonth => _yearMonth;

        /// <summary>确认记录后触发（参数：groupId, recordId）</summary>
        public event Action<string, string>? RecordConfirmed;

        public bool IsRunning => Volatile.Read(ref _running);
        public int Port => _port;
        public string BoundIpAddress => _boundIpAddress;
        public string Token => _token;
        public int EntryCount => Volatile.Read(ref _entryCount);

        public HazardLanService(ProjectDataService dataService, string yearMonth, string groupName)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _yearMonth = yearMonth;
            _groupName = groupName;
            _htmlPage = LoadEmbeddedHtml();
        }

        // ==================== 启动 / 停止 ====================

        public void Start(int port)
        {
            if (_running) return;
            _port = port;

            var lanIp = LanService.GetLocalIPAddress();
            _boundIpAddress = lanIp;
            _cts = new CancellationTokenSource();

            // 生成访问令牌（拼入二维码 URL，手机端所有请求需携带）
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = "HazardLanService",
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

            var svc = this;
            _app.MapGet("/", (HttpContext ctx) => ServePage(ctx));
            _app.MapGet("/ping", (HttpContext ctx) =>
                ctx.Response.WriteAsJsonAsync(new { ok = true, groupName = svc._groupName, yearMonth = svc._yearMonth }));
            _app.MapGet("/dates", (HttpContext ctx) => HandleDates(ctx));
            _app.MapPost("/upload-photo", (HttpContext ctx) => HandleUploadPhoto(ctx));
            _app.MapPost("/submit", (HttpContext ctx) => HandleSubmit(ctx));

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

            // 清理之前崩溃/异常退出遗留的孤儿临时目录（超过 TTL 才删，不影响其他窗口较新的照片）
            CleanupStaleTempDirectories();

            Volatile.Write(ref _running, true);
            Log.Information("HazardLanService started on port {Port} (yearMonth={YM})", port, _yearMonth);
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
                _ = Task.Run(() => { try { app.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { /* 后台服务停止失败可忽略 */ } });
            }

            // 清理本实例未提交照片的临时目录（按缓存路径去重，不影响其他窗口的服务）
            foreach (var dir in _photoCache.Values
                         .Select(p => Path.GetDirectoryName(p))
                         .Where(d => !string.IsNullOrEmpty(d))
                         .Distinct())
            {
                LanService.TryDeleteTempDirectory(dir!);
            }
            _photoCache.Clear();
            Log.Information("HazardLanService stopped");
        }

        // ==================== 路由 ====================

        private async Task ServePage(HttpContext ctx)
        {
            // 禁止缓存：手机浏览器启发式缓存会加载旧页面导致新功能不生效
            ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(_htmlPage);
        }

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
        }

        /// <summary>接收隐患/整改照片，存到临时目录</summary>
        private async Task HandleUploadPhoto(HttpContext ctx)
        {
            var type = ctx.Request.Query["type"].FirstOrDefault() ?? "hazard";
            var tempId = ctx.Request.Query["tempId"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");

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

            Log.Information("HazardLanService: 录入完成 {Title}({Id})", record.Title, record.Id);
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

        public void Dispose() => Stop();

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

        private static string LoadEmbeddedHtml()
        {
            var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "hazard_lan_page.html");
            if (File.Exists(htmlPath))
                return File.ReadAllText(htmlPath, Encoding.UTF8);

            var srcPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Resources\hazard_lan_page.html");
            if (File.Exists(srcPath))
                return File.ReadAllText(srcPath, Encoding.UTF8);

            return "<!DOCTYPE html><html><body><h2>页面加载失败</h2></body></html>";
        }
    }
}
