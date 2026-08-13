// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using MetadataExtractor.Formats.Exif;
using Serilog;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 手机图片传输服务：手机通过局域网把图片上传到电脑指定位置。
    /// 与人员/隐患 LAN 录入的本质差异——纯图片落盘，不建记录、不 OCR、不绑定项目分组：
    /// 上传 → 服务端读 EXIF 拍摄日期（MetadataExtractor）→ 按日期分文件夹（yyyy-MM-dd）→ 原图直写落盘。
    /// 复用 LanService 骨架：Kestrel 零 UAC 绑定、token 鉴权、20MB 上限、端口 5000-5099 自动探测。
    /// </summary>
    public class PhotoTransferService : IDisposable
    {
        public const int MaxUploadBytes = 20 * 1024 * 1024;   // 图片上传上限 20MB（与 LAN 录入一致）

        private WebApplication? _app;
        private CancellationTokenSource? _cts;
        private volatile bool _running;
        private int _port;
        private string _boundIpAddress = "127.0.0.1";
        private string _token = "";
        private string _rootFolder = "";
        private readonly string _htmlPage;

        /// <summary>接收一张图片时触发（参数：拍摄/生成时间、落盘文件名、字节数、完整落盘路径），供窗口更新列表与计数</summary>
        public event Action<DateTime, string, long, string>? PhotoReceived;

        public bool IsRunning => Volatile.Read(ref _running);
        public int Port => _port;
        public string BoundIpAddress => _boundIpAddress;
        public string Token => _token;

        public PhotoTransferService()
        {
            _htmlPage = LoadEmbeddedHtml();
        }

        // ==================== 启动 / 停止 ====================

        /// <summary>启动服务：rootFolder 为接收图片的根目录（接收后按日期分文件夹存放）</summary>
        public void Start(int port, string rootFolder)
        {
            if (_running) return;
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
                throw new ArgumentException("目标文件夹不存在", nameof(rootFolder));

            _port = port;
            _rootFolder = rootFolder;
            _boundIpAddress = GetLocalIPAddress();
            _cts = new CancellationTokenSource();
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = "PhotoTransferService",
                Args = Array.Empty<string>()
            });
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
            builder.WebHost.UseKestrel();
            _app = builder.Build();

            // 鉴权中间件：请求必须携带正确 token（二维码 URL 已含 token）
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
            _app.MapGet("/ping", (HttpContext ctx) => ctx.Response.WriteAsJsonAsync(new { ok = true }));
            _app.MapPost("/upload", (HttpContext ctx) => HandleUpload(ctx));

            try
            {
                // 同步等待 Kestrel 启动完成：端口被占等绑定失败会在此抛出，
                // 供调用方的端口探测（StartOnAvailablePort）捕获并尝试下一个端口。
                // （fire-and-forget 会导致"都显示 5000"的假象，2026-08-12 修复）
                Task.Run(() => _app.StartAsync(_cts.Token)).GetAwaiter().GetResult();
            }
            catch
            {
                // 清理半启动状态后重新抛出（端口占用等）
                try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
                _app = null;
                throw;
            }
            Volatile.Write(ref _running, true);
            Log.Information("PhotoTransferService started on port {Port} (Kestrel, bound 0.0.0.0, root={Root})", port, rootFolder);
        }

        public void Stop()
        {
            _cts?.Cancel();
            Volatile.Write(ref _running, false);

            var app = Interlocked.Exchange(ref _app, null);
            if (app != null)
            {
                _ = Task.Run(() => { try { app.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { } });
            }
            Log.Information("PhotoTransferService stopped");
        }

        /// <summary>校验请求 token（恒定时间比较，防时序攻击）</summary>
        private bool IsAuthorized(HttpContext ctx)
        {
            if (string.IsNullOrEmpty(_token)) return false;
            string? supplied = ctx.Request.Query["token"].FirstOrDefault();
            if (string.IsNullOrEmpty(supplied)) return false;
            var a = Encoding.UTF8.GetBytes(supplied);
            var b = Encoding.UTF8.GetBytes(_token);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        // ==================== 路由处理 ====================

        private async Task ServePage(HttpContext ctx)
        {
            // 禁止缓存：手机浏览器默认启发式缓存 HTML，会加载旧版页面导致新功能不生效
            ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(_htmlPage);
        }

        /// <summary>
        /// 处理图片上传：校验文件名白名单 → 读取原图字节（20MB 上限）→ 临时文件 →
        /// 读 EXIF 拍摄日期（无则兜底今天）→ 按日期分文件夹 → 防重名 → 原图落盘。
        /// 全程不重新编码，保留图片原始信息（EXIF 等）。
        /// </summary>
        private async Task HandleUpload(HttpContext ctx)
        {
            // 文件名白名单校验（原始文件名来自手机，必须先剥路径再校验）
            string fileName = Path.GetFileName(ctx.Request.Query["fileName"].FirstOrDefault() ?? "");
            if (string.IsNullOrEmpty(fileName) || !IsSupportedImageName(fileName))
            {
                await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "不支持的图片格式" });
                return;
            }

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

            // 写临时文件（读 EXIF 需要真实文件路径）
            string tempDir = Path.Combine(Path.GetTempPath(), "PhotoTransfer");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + Path.GetExtension(fileName));
            await File.WriteAllBytesAsync(tempFile, fileData);

            try
            {
                // 拍摄时间三层兜底：
                // 1) EXIF DateTimeOriginal（手机内拍摄时间，相机/手机直出照片最准）
                // 2) 手机文件生成时间 mtime（file.lastModified，无 EXIF 的截图/微信图等）
                // 3) 都取不到 → 上传当天
                DateTime shootTime = ReadExifShootTime(tempFile)
                                     ?? ParseMtimeMillis(ctx.Request.Query["mtime"].FirstOrDefault())
                                     ?? DateTime.Now;
                string dateFolder = shootTime.ToString("yyyy-MM-dd");
                // 日期来自解析值或 DateTime 格式化，天然合法；仍做防御性校验（防未来改代码引入原始输入）
                if (!IsValidDateFolder(dateFolder))
                    dateFolder = DateTime.Now.ToString("yyyy-MM-dd");

                string targetDir = Path.Combine(_rootFolder, dateFolder);
                Directory.CreateDirectory(targetDir);

                // 原图直写 + 重名自动加序号（复用图片处理的 GetUniquePath）
                string targetPath = ImageProcessViewModel.GetUniquePath(Path.Combine(targetDir, fileName));
                File.Move(tempFile, targetPath);

                string savedName = Path.GetFileName(targetPath);
                PhotoReceived?.Invoke(shootTime, savedName, fileData.Length, targetPath);
                await ctx.Response.WriteAsJsonAsync(new
                {
                    ok = true,
                    date = dateFolder,
                    fileName = savedName
                });
            }
            finally
            {
                // 移动成功后临时文件已不存在；失败时清理残留
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { /* 清理失败仅记日志由系统回收 */ }
                }
            }
        }

        // ==================== 工具方法 ====================

        /// <summary>读 EXIF 拍摄时间（DateTimeOriginal，手机内生成时间）；无 EXIF/读取失败返回 null</summary>
        public static DateTime? ReadExifShootTime(string filePath)
        {
            try
            {
                var directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(filePath);
                // 优先 SubIFD（拍摄参数区），其次 IFD0
                MetadataExtractor.Directory? dir = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                dir ??= directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                if (dir == null || !dir.ContainsTag(ExifDirectoryBase.TagDateTimeOriginal)) return null;

                // EXIF 日期为字符串描述，标准格式 "2026:08:12 18:00:00"（日期用冒号分隔）
                string? desc = dir.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);
                if (string.IsNullOrWhiteSpace(desc)) return null;
                if (DateTime.TryParseExact(desc, "yyyy:MM:dd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var dt)
                    || DateTime.TryParse(desc, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out dt))
                {
                    return dt;
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "读取 EXIF 拍摄时间失败（按无日期处理）：{Path}", filePath);
                return null;
            }
        }

        /// <summary>日期文件夹名校验（yyyy-MM-dd，防路径穿越；解析值也应过此关）</summary>
        public static bool IsValidDateFolder(string dateFolder) =>
            DateTime.TryParseExact(dateFolder, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _);

        /// <summary>
        /// 解析手机端 file.lastModified（毫秒时间戳，即手机上的文件生成日期）。
        /// 仅作为 EXIF 拍摄日期缺失时的兜底；非法/缺失/超出合理范围返回 null。
        /// 合理范围下限 = 1975-12-01（世界第一张数码照片，柯达 Steve Sasson 拍摄之日）：
        /// 手机上不存在早于数码影像诞生之日的照片文件，此下限兼顾纪念意义与语义自洽。
        /// 上限 = 当前+1 年（防错误/恶意值把文件归档到奇怪日期）。
        /// </summary>
        public static DateTime? ParseMtimeMillis(string? mtime)
        {
            if (!long.TryParse(mtime, out long ms) || ms <= 0) return null;
            DateTime dt;
            try
            {
                dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            }
            catch
            {
                return null;
            }
            // 1975-12-01（第一张数码照片）~ 当前+1 年
            if (dt < FirstDigitalPhotoDate || dt > DateTime.Now.AddYears(1)) return null;
            return dt;
        }

        /// <summary>1975-12-01：世界第一张数码照片拍摄之日（柯达 Steve Sasson）</summary>
        public static readonly DateTime FirstDigitalPhotoDate = new(1975, 12, 1);

        /// <summary>图片文件名白名单（与主程序 IsImageFile 支持集合一致）</summary>
        public static bool IsSupportedImageName(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tiff" or ".tif" or ".gif";
        }

        /// <summary>读取请求体（限量），超限返回 null</summary>
        public static async Task<byte[]?> ReadBodyWithLimitAsync(HttpContext ctx, int maxBytes)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int total = 0;
            while (true)
            {
                int read = await ctx.Request.Body.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) break;
                total += read;
                if (total > maxBytes) return null;
                await ms.WriteAsync(buffer, 0, read);
            }
            return ms.ToArray();
        }

        /// <summary>获取本机局域网 IPv4 地址（多网卡取第一个非回环、非虚拟网卡）</summary>
        public static string GetLocalIPAddress()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return addr.Address.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "获取本机 IP 失败，使用回环地址");
            }
            return "127.0.0.1";
        }

        private static string LoadEmbeddedHtml()
        {
            var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "photo_transfer_page.html");
            if (File.Exists(htmlPath))
                return File.ReadAllText(htmlPath, Encoding.UTF8);

            var srcPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\..\Resources\photo_transfer_page.html");
            if (File.Exists(srcPath))
                return File.ReadAllText(srcPath, Encoding.UTF8);

            Log.Warning("PhotoTransferService: HTML page file not found, using fallback minimal page");
            return "<!DOCTYPE html><html><body><h2>页面加载失败</h2><p>请检查 Resources/photo_transfer_page.html 是否存在</p></body></html>";
        }

        public void Dispose() => Stop();
    }
}
