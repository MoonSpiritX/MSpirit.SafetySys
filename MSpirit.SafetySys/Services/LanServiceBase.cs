// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// LAN HTTP 服务公共基类（2026-08-16 抽取，三个服务 LanService / HazardLanService /
    /// PhotoTransferService 共用）。承载 Kestrel 托管骨架、token 鉴权中间件、请求入口日志、
    /// 已连接设备事件（DeviceConnected/DeviceLeft + /bye）、启动/停止流程与共享工具。
    ///
    /// 子类只需提供：ApplicationName / LogPrefix / ServiceDisplayName（差异点）、
    /// <see cref="MapRoutes"/>（业务路由）、可选 OnBeforeStart / OnStopped 钩子；
    /// 业务字段与路由处理（Handle*）留在子类。
    /// </summary>
    public abstract class LanServiceBase : IDisposable
    {
        // ── 共享状态（子类通过属性只读访问） ──
        protected int _port;
        protected bool _running;
        protected string _boundIpAddress = "127.0.0.1";
        protected CancellationTokenSource? _cts;
        protected WebApplication? _app;
        protected string _token = "";
        protected readonly string _htmlPage;
        // 实例级环境探测缓存（2026-08-16 日志瘦身）：端口探测重试（FindAvailable 逐个 Start）
        // 会多次进入 Start，每次枚举 42 张网卡 + 注册表/netsh 查询产生大量重复日志；
        // 网卡与网络类别在秒级不会变化，实例内缓存，Stop 时清空。
        private string? _cachedLanIp;
        private string? _cachedNetworkCategory;

        /// <summary>任何授权请求到达服务时触发（参数：来源 IP）——窗口据此统计"已连接设备"（按 IP 去重）</summary>
        public event Action<string>? DeviceConnected;

        /// <summary>手机页面关闭时上报"设备离开"（参数：来源 IP）——窗口立即从已连接设备移除，不等心跳超时</summary>
        public event Action<string>? DeviceLeft;

        public bool IsRunning => Volatile.Read(ref _running);
        public int Port => _port;
        public string BoundIpAddress => _boundIpAddress;
        public string Token => _token;

        public const int MaxUploadBytes = 20 * 1024 * 1024;   // 图片上传上限 20MB
        public const int MaxJsonBodyBytes = 1024 * 1024;      // JSON 请求体上限 1MB

        // ── 子类差异点 ──
        /// <summary>Kestrel 宿主 ApplicationName（日志/入口程序集标识）</summary>
        protected abstract string ApplicationName { get; }

        /// <summary>日志前缀（Lan / HazardLan / PhotoTransfer）</summary>
        protected abstract string LogPrefix { get; }

        /// <summary>服务中文名（启动日志：准备启动xx服务）</summary>
        protected abstract string ServiceDisplayName { get; }

        /// <summary>绑定前钩子（如校验参数、记录专属信息）</summary>
        protected virtual void OnBeforeStart() { }

        /// <summary>服务启动成功后钩子（如拉起子类后台任务，此时 _cts/_token 可用）</summary>
        protected virtual void OnStarted() { }

        /// <summary>停止后钩子（清理子类暂存/缓存）</summary>
        protected virtual void OnStopped() { }

        /// <summary>注册业务路由（/ 与 /bye 由基类负责）</summary>
        protected abstract void MapRoutes(WebApplication app);

        protected LanServiceBase(string htmlResourceName)
        {
            _htmlPage = LoadEmbeddedHtml(htmlResourceName);
        }

        // ==================== 启动 / 停止 ====================

        public void Start(int port)
        {
            if (_running) return;
            OnBeforeStart();
            _port = port;
            _boundIpAddress = _cachedLanIp ??= GetLocalIPAddress();
            _cts = new CancellationTokenSource();
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

            // ── 启动诊断日志：网络环境快照（定位"别人电脑上 LAN 不可用"用）──
            Log.Information("[{Prefix}] 准备启动{Service}服务：目标端口={Port}，token={Token}", LogPrefix, ServiceDisplayName, port, _token);
            Log.Information("[{Prefix}] 绑定地址（局域网 IP 选择结果）：{LanIp}", LogPrefix, _boundIpAddress);
            // 网络类别实例缓存（端口探测重试不重复查询注册表/netsh，见 _cachedNetworkCategory 注释）
            string? category = _cachedNetworkCategory ??= GetNetworkCategory();
            if (category != null)
                Log.Information("[{Prefix}] 当前网络类别：{Category}（公用网络下 Windows 防火墙默认拦截入站，是最常见故障点）", LogPrefix, category);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = ApplicationName,
                Args = Array.Empty<string>()
            });
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
            builder.WebHost.UseKestrel();
            _app = builder.Build();

            // 鉴权中间件：请求必须携带正确 token，否则一律 401（二维码 URL 已含 token）。
            // 同时承担"请求入口日志"职责：区分"手机请求根本没到服务（网络/防火墙层问题）"
            // 与"到了但被拒/处理失败（应用层问题）"。
            _app.Use(async (ctx, next) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "未知";
                string maskedQuery = MaskToken(ctx.Request.QueryString.Value);
                bool authorized = IsAuthorized(ctx);
                if (!authorized)
                {
                    sw.Stop();
                    Log.Warning("[{Prefix}] 拒绝未授权请求：{Method} {Path} 来源={RemoteIp} token={TokenState} 耗时={ElapsedMs}ms",
                        LogPrefix, ctx.Request.Method, maskedQuery, remoteIp,
                        string.IsNullOrEmpty(ctx.Request.Query["token"].FirstOrDefault()) ? "缺失" : "不匹配",
                        sw.ElapsedMilliseconds);
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "未授权访问，请重新扫描二维码" });
                    return;
                }
                // 仅授权请求（携带有效 token）计入"已连接设备"——服务重启后旧 token 失效，旧页面不计入
                DeviceConnected?.Invoke(ctx.Connection.RemoteIpAddress?.ToString() ?? "");
                await next();
                sw.Stop();
                // 请求入口日志只记业务路由；心跳 /ping（手机端每 3 秒一次）与 /bye 是高频噪音，
                // 不记（2026-08-16 日志瘦身）。"请求到没到服务"的判读仍由业务请求日志承担。
                string requestPath = ctx.Request.Path.Value ?? "";
                if (requestPath != "/ping" && requestPath != "/bye")
                {
                    Log.Information("[{Prefix}] 请求完成：{Method} {Path} 来源={RemoteIp} 状态={StatusCode} 耗时={ElapsedMs}ms",
                        LogPrefix, ctx.Request.Method, maskedQuery, remoteIp, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
                }
            });

            MapRoutes(_app);
            // 手机页面 pagehide 时 sendBeacon 上报"设备离开"（经鉴权中间件，旧 token 无效）；
            // 服务端据此立即移除已连接设备，无需等心跳超时
            _app.MapPost("/bye", (HttpContext ctx) =>
            {
                DeviceLeft?.Invoke(ctx.Connection.RemoteIpAddress?.ToString() ?? "");
                return ctx.Response.WriteAsJsonAsync(new { ok = true });
            });

            // 启动 Kestrel：同步等待绑定完成，端口被占会在此抛出（供端口探测捕获下一个端口）。
            // 原 fire-and-forget 导致绑定失败不感知、多个服务窗口都显示同一端口（2026-08-12 修复）
            try
            {
                Task.Run(() => _app.StartAsync(_cts.Token)).GetAwaiter().GetResult();
            }
            catch
            {
                Log.Debug("[{Prefix}] 端口绑定失败，清理半启动服务", LogPrefix);
                try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch (Exception disposeEx) { Log.Debug(disposeEx, "[{Prefix}] 清理半启动服务失败（忽略）", LogPrefix); }
                _app = null;
                throw;
            }

            Volatile.Write(ref _running, true);
            Log.Information("[{Prefix}] 服务已启动：端口={Port}（绑定 0.0.0.0，lanIp={LanIp}，token={Token}）",
                LogPrefix, port, _boundIpAddress, _token);

            OnStarted();
        }

        public virtual void Stop()
        {
            _cts?.Cancel();
            Volatile.Write(ref _running, false);

            var app = Interlocked.Exchange(ref _app, null);
            if (app != null)
            {
                // 不阻塞调用线程（Stop() 从 UI 线程调用），fire-and-forget 关闭 Kestrel
                _ = Task.Run(() => { try { app.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { /* 后台服务停止失败可忽略 */ } });
            }

            OnStopped();
            // 清空实例缓存：下次 Start 重新探测（网络环境可能已变化）
            _cachedLanIp = null;
            _cachedNetworkCategory = null;
            Log.Information("[{Prefix}] 服务已停止（端口 {Port}）", LogPrefix, _port);
        }

        public void Dispose() => Stop();

        // ==================== 共享路由处理 ====================

        /// <summary>校验请求是否携带正确的访问令牌（恒定时间比较，防时序攻击）</summary>
        protected bool IsAuthorized(HttpContext ctx)
        {
            if (string.IsNullOrEmpty(_token)) return false;
            string? supplied = ctx.Request.Query["token"].FirstOrDefault();
            if (string.IsNullOrEmpty(supplied)) return false;
            var a = Encoding.UTF8.GetBytes(supplied);
            var b = Encoding.UTF8.GetBytes(_token);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>把查询串中的 token 值打码（只保留 token 存在性，不泄露完整值）</summary>
        protected static string MaskToken(string? query)
        {
            if (string.IsNullOrEmpty(query)) return "";
            return System.Text.RegularExpressions.Regex.Replace(query, @"(?<=[?&]token=)[^&]*", "***");
        }

        /// <summary>返回手机页面（禁止缓存，手机浏览器启发式缓存会加载旧页面）</summary>
        protected async Task ServePage(HttpContext ctx)
        {
            ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(_htmlPage);
            Log.Debug("[{Prefix}] 提供录入页面：来源={Ip} 字节={Bytes}", LogPrefix, ctx.Connection.RemoteIpAddress, Encoding.UTF8.GetByteCount(_htmlPage));
        }

        // ==================== 共享工具 ====================

        /// <summary>读取嵌入式 HTML 页面（输出目录 → 源码目录相对路径回退）</summary>
        protected static string LoadEmbeddedHtml(string resourceName)
        {
            var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", resourceName);
            if (File.Exists(htmlPath))
                return File.ReadAllText(htmlPath, Encoding.UTF8);

            var srcPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\..\Resources", resourceName);
            if (File.Exists(srcPath))
                return File.ReadAllText(srcPath, Encoding.UTF8);

            Log.Warning("LanServiceBase: HTML page file not found: {Name}, using fallback minimal page", resourceName);
            return "<!DOCTYPE html><html><body><h2>页面加载失败</h2><p>请检查 Resources 页面文件是否存在</p></body></html>";
        }

        /// <summary>获取本机局域网 IP 地址（优先物理网卡出口，避免选中 VPN/隧道/虚拟网卡地址）。
        /// 本方法同时输出网卡枚举详单日志，用于定位"二维码 IP 选错/选到虚拟网卡"类问题。</summary>
        public static string GetLocalIPAddress()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            Log.Information("[Lan] 网卡枚举：共发现 {Count} 个网络接口", interfaces.Length);
            foreach (var ni in interfaces)
            {
                string desc = ni.Description ?? "";
                string? skipReason = null;
                if (ni.OperationalStatus != OperationalStatus.Up)
                    skipReason = $"状态={ni.OperationalStatus}（非 Up）";
                else if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    skipReason = "回环网卡";
                else if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    skipReason = "隧道网卡";
                else if (!string.IsNullOrEmpty(desc) &&
                         (desc.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                          desc.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                          desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                          desc.Contains("Tap", StringComparison.OrdinalIgnoreCase) ||
                          desc.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                          desc.Contains("WSL", StringComparison.OrdinalIgnoreCase)))
                    skipReason = "描述含虚拟网卡关键字";

                var props = ni.GetIPProperties();
                int gatewayCount = props.GatewayAddresses.Count;
                var ipv4List = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList();

                if (skipReason != null)
                {
                    Log.Debug("[Lan] 网卡「{Name}」跳过：{Reason}（类型={Type}，IPv4=[{Ipv4}]）",
                        ni.Name, skipReason, ni.NetworkInterfaceType, string.Join(", ", ipv4List));
                    continue;
                }

                if (gatewayCount == 0)
                {
                    Log.Debug("[Lan] 网卡「{Name}」跳过：无默认网关（非局域网出口）（描述={Desc}，IPv4=[{Ipv4}]）",
                        ni.Name, desc, string.Join(", ", ipv4List));
                    continue;
                }

                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !addr.Address.ToString().StartsWith("169.254."))
                    {
                        Log.Information("[Lan] 选中局域网 IP：{Ip}（网卡「{Name}」描述={Desc} 类型={Type} 网关数={GatewayCount}）",
                            addr.Address, ni.Name, desc, ni.NetworkInterfaceType, gatewayCount);
                        return addr.Address.ToString();
                    }
                }
            }

            var host = Dns.GetHostEntry(Dns.GetHostName());
            Log.Warning("[Lan] 网卡枚举未命中候选（可能无有效局域网出口），走兜底：主机名 DNS 共 {Count} 条 IPv4",
                host.AddressList.Count(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork));
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    if (ip.ToString().StartsWith("169.254.")) continue;
                    Log.Warning("[Lan] 兜底选中 IP：{Ip}", ip);
                    return ip.ToString();
                }
            }
            Log.Warning("[Lan] 未找到任何局域网 IP，回退 127.0.0.1（二维码将不可被手机访问）");
            return "127.0.0.1";
        }

        /// <summary>
        /// 读取当前网络的 Windows 防火墙类别（公用/专用/域）。公用网络下 Windows 防火墙默认
        /// 阻止入站连接——LAN 服务"别人电脑上不可用"的头号原因，此信息用于日志直接给出线索。
        /// 实现：`netsh advfirewall monitor show currentprofile`（普通权限可执行，语义为"当前活动
        /// 配置文件"，比注册表方案更准确）。
        /// 2026-08-16 核实：原注册表 NetworkList\Profiles 方案在本程序的实际运行权限
        /// （非管理员，"零 UAC 绑定"设计）下**必然抛 SecurityException**——该键默认只授予
        /// SYSTEM 与管理员的读权限（实测 reg.exe 同样 Access denied），且其语义是"最近连接的网络"
        /// 而非"当前活动网络"。故注册表分支已删除（2026-08-16），netsh 为唯一方案。
        /// </summary>
        public static string? GetNetworkCategory()
        {
            try
            {
                string? profile = LanFirewallService.GetCurrentFirewallProfile();
                if (profile != null)
                {
                    string result = $"当前网络类别={CategoryText(profile)}";
                    Log.Information("[Lan] 网络类别判定：{Result}", result);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Lan] netsh 读取网络类别失败（忽略，不影响服务）");
            }
            return null;
        }

        private static string CategoryText(string profile) => profile switch
        {
            "Public" => "公用网络（Public）",
            "Private" => "专用网络（Private）",
            "Domain" => "域网络（Domain）",
            _ => profile
        };

        /// <summary>限制读取请求体，超过 maxBytes 返回 null（同时校验 ContentLength 与分块传输）</summary>
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

        // ==================== 共享校验（三个服务 + 手机端入参校验） ====================

        private static readonly System.Text.RegularExpressions.Regex TempIdPattern =
            new("^[A-Za-z0-9_-]{1,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>校验客户端传入的标识符（tempId/photoId），仅允许字母数字下划线连字符，防路径穿越</summary>
        public static bool IsValidTempId(string? id) =>
            !string.IsNullOrEmpty(id) && TempIdPattern.IsMatch(id);

        /// <summary>校验照片类型（hazard/remedy）</summary>
        public static bool IsValidPhotoType(string? type) =>
            type is "hazard" or "remedy";

        private static readonly System.Text.RegularExpressions.Regex YearMonthPattern =
            new(@"^\d{4}-(0[1-9]|1[0-2])$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Collections.Generic.HashSet<string> HazardLevelSet =
            new(StringComparer.Ordinal) { "一般隐患", "较大隐患", "重大隐患" };

        /// <summary>校验年月格式（yyyy-MM，月份 01~12）</summary>
        public static bool IsValidYearMonth(string? ym) =>
            !string.IsNullOrEmpty(ym) && YearMonthPattern.IsMatch(ym);

        /// <summary>校验隐患级别（与手机端下拉选项一致）</summary>
        public static bool IsValidHazardLevel(string? level) =>
            !string.IsNullOrEmpty(level) && HazardLevelSet.Contains(level);
    }
}
