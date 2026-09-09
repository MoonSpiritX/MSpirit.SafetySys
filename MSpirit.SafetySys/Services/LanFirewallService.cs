// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using MS_SafetyManagementSystem.Common;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// LAN 服务防火墙放行管理（2026-08-15 新增，方案 B）。
    ///
    /// 背景：LAN 服务（人员/隐患录入、手机传图）绑定 <see cref="Common.LanPorts"/> 端口，
    /// Windows 防火墙在公用网络下默认阻止所有入站——首次使用 LAN 服务的用户
    /// （无「斜月安全管理系统-LAN」放行规则）手机必然连不上。本服务在用户点击
    /// 「启动服务」时检测放行规则，缺失则弹引导窗口，经 UAC 一键执行 netsh 添加放行规则
    /// （用户零命令行操作）。
    ///
    /// 设计要点：
    ///  - 检测用 netsh 查询（普通权限可执行），两段式：固定规则名退出码法 + 全量扫描兜底；
    ///  - 添加用 netsh 提权执行（Verb=runas 触发 UAC），幂等（添加前二次检测）；
    ///  - 检测/添加失败均不阻断服务启动，仅提示（宁可少打扰，不让用户被卡住）。
    /// </summary>
    public static class LanFirewallService
    {
        /// <summary>放行规则名（与历史累积的 71 条规则同名，老用户检测命中后无感跳过）</summary>
        public const string RuleName = "斜月安全管理系统-LAN";

        /// <summary>LAN 服务端口起点（引用 Common/LanPorts，改端口只改那一处）</summary>
        public const int StartPort = LanPorts.Start;

        /// <summary>放行端口终点（引用 Common/LanPorts，探测上限留 1 位余量）</summary>
        public const int EndPort = LanPorts.FirewallEnd;

        // ── netsh 输出解析（2026-08-16 修复 P0：标签与值均本地化，必须中英双匹配）──
        // netsh 的 verbose 字段标签/取值随控制台代码页变化：UTF-8 代码页（如 PowerShell/启用 UTF-8 系统）
        // 输出英文（Rule Name:/Direction:/Action:...），中文系统默认 GBK 代码页输出中文
        // （规则名称:/方向:/操作:...），值同样本地化（入站=In、允许=Allow、阻止=Block、任何=Any）。
        // 此前硬编码英文 "Rule Name:" 拆分 → 中文输出下永远只拆出 1 块 → 规则检测全部失效（P0）。
        private static readonly Regex RuleBlockSplit = new(@"(?=(?:Rule Name|规则名称)\s*[:：])", RegexOptions.Compiled);
        private static readonly Regex DirectionField = new(@"(?:Direction|方向)\s*[:：]\s*(\S+)", RegexOptions.Compiled);
        private static readonly Regex ProtocolField = new(@"(?:Protocol|协议)\s*[:：]\s*(\S+)", RegexOptions.Compiled);
        private static readonly Regex LocalPortField = new(@"(?:LocalPort|本地端口)\s*[:：]\s*([^\r\n]+)", RegexOptions.Compiled);
        private static readonly Regex ProgramField = new(@"(?:Program|程序)\s*[:：]\s*([^\r\n]+)", RegexOptions.Compiled);
        private static readonly Regex ActionField = new(@"(?:Action|操作)\s*[:：]\s*(\S+)", RegexOptions.Compiled);
        private static readonly Regex ProfilesField = new(@"(?:Profiles|配置文件)\s*[:：]\s*([^\r\n]+)", RegexOptions.Compiled);

        /// <summary>
        /// 获取当前活动的 Windows 防火墙配置文件（Public/Private/Domain），用于判定放行规则是否
        /// 适用于当前网络。实现：`netsh advfirewall monitor show currentprofile`（普通权限可执行），
        /// 解析「Public Profile:/Private Profile:/Domain Profile:」（英文）或
        /// 「公用配置文件:/专用配置文件:/域配置文件:」（中文）标签；获取失败返回 null（调用方按宽松处理）。
        /// 2026-08-16 新增：替代注册表 NetworkList 方案（本机 OpenSubKey 抛 SecurityException）。
        /// 2026-08-16 日志瘦身：每次调用都会 spawn 一个 netsh monitor 进程且写日志，而网络配置文件
        /// 在 5 秒内不会变化——加短 TTL 缓存（含 null 结果，避免失败场景反复 spawn）。
        /// </summary>
        internal static string? GetCurrentFirewallProfile()
        {
            lock (ProfileCacheSync)
            {
                if (_profileCacheAt != default && DateTime.Now - _profileCacheAt < ProfileCacheTtl)
                    return _profileCachedValue;
            }

            string? result;
            try
            {
                string output = RunNetsh("advfirewall monitor show currentprofile");
                if (string.IsNullOrWhiteSpace(output)) result = null;
                else if (Regex.IsMatch(output, @"(?:Public Profile|公用配置文件)", RegexOptions.IgnoreCase)) result = "Public";
                else if (Regex.IsMatch(output, @"(?:Private Profile|专用配置文件)", RegexOptions.IgnoreCase)) result = "Private";
                else if (Regex.IsMatch(output, @"(?:Domain Profile|域配置文件)", RegexOptions.IgnoreCase)) result = "Domain";
                else
                {
                    Log.Warning("[Lan] 无法识别 netsh 当前配置文件输出：{Chars} 字符", output.Length);
                    result = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Lan] 读取当前防火墙配置文件失败");
                result = null;
            }

            lock (ProfileCacheSync)
            {
                _profileCachedValue = result;
                _profileCacheAt = DateTime.Now;
            }
            return result;
        }

        // ── 当前活动配置文件短缓存（2026-08-16 日志瘦身，见 GetCurrentFirewallProfile）──
        private static readonly object ProfileCacheSync = new();
        private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromSeconds(5);
        private static string? _profileCachedValue;
        private static DateTime _profileCacheAt;

        /// <summary>把本地化的配置文件取值归一化为英文（域/Domain → Domain，专用/Private → Private，公用/Public → Public）</summary>
        internal static string NormalizeProfile(string value) => value.Trim().ToLowerInvariant() switch
        {
            "domain" or "域" => "Domain",
            "private" or "专用" => "Private",
            "public" or "公用" => "Public",
            _ => value.Trim()
        };

        /// <summary>
        /// 判断规则 Profiles 列表（"Domain,Private,Public" / "域,专用,公用"）是否包含当前活动配置文件。
        /// currentProfile 为 null（无法获取）时返回 true（宽松，避免把本可用的放行误判为不可用）。
        /// </summary>
        internal static bool ProfileListContains(string profilesValue, string? currentProfile)
        {
            if (string.IsNullOrEmpty(currentProfile)) return true;
            if (string.IsNullOrWhiteSpace(profilesValue)) return false;
            foreach (var part in profilesValue.Split(','))
            {
                if (string.Equals(NormalizeProfile(part), currentProfile, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>把本地化的 Action 取值归一化为英文（允许/允/Allow → Allow，阻止/阻/Block → Block）</summary>
        internal static string NormalizeActionValue(string value) => value.Trim().ToLowerInvariant() switch
        {
            "allow" or "允许" or "允" => "Allow",
            "block" or "阻止" or "阻" => "Block",
            _ => value.Trim()
        };

        /// <summary>把本地化的 Direction 取值归一化为英文（入站/入/In → In，出站/出/Out → Out）。
        /// 2026-08-16 补充单字形态：netsh 中文输出对「仅指定程序、未显式指定 dir」的规则
        /// （如 TryAddProgramAllowRule 创建的 program 规则）会把方向写成单个「入」（U+5165），
        /// 其他规则才写全「入站」——漏掉该形态会导致 HasProgramAllowRule 永远命中=0、每次启动弹窗。</summary>
        internal static string NormalizeDirectionValue(string value) => value.Trim().ToLowerInvariant() switch
        {
            "in" or "入站" or "入" => "In",
            "out" or "出站" or "出" => "Out",
            _ => value.Trim()
        };

        /// <summary>按规则名标签（Rule Name:/规则名称:）切分规则块，供中英文输出共用</summary>
        internal static string[] SplitRuleBlocks(string output) => RuleBlockSplit.Split(output);

        /// <summary>统计文本中规则名标记出现次数（Rule Name: 或 规则名称），用于解码择优与自检</summary>
        internal static int CountRuleMarkers(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 0, idx = 0;
            while (idx < text.Length)
            {
                int en = text.IndexOf("Rule Name:", idx, StringComparison.Ordinal);
                int zh = text.IndexOf("规则名称", idx, StringComparison.Ordinal);
                int next;
                if (en < 0 && zh < 0) break;
                next = (en < 0) ? zh : (zh < 0) ? en : Math.Min(en, zh);
                count++;
                idx = next + 1;
            }
            return count;
        }

        /// <summary>
        /// 确保本程序具备 Windows 防火墙入站放行（返回 true = 手机可访问；false = 未自动放行，不阻断启动）。
        /// 流程（2026-08-16 重构为「程序级放行 + 原生弹窗」方案）：
        ///  0. **程序级阻止规则**（根因：防火墙弹窗点「取消」生成，优先于端口 Allow 规则，且会抑制原生弹窗）
        ///     → 引导用户 UAC 删除；
        ///  1. 已有放行（端口规则覆盖 Start，或程序级 Allow 规则）→ 直接可用；
        ///  2. 无任何放行规则 → 弹引导窗口：默认路径为**让 Windows 弹原生「是否允许此应用访问网络？」窗口**
        ///     （手机首个入站连接时自动弹出，用户点允许即生成程序级 Allow 规则，覆盖所有端口、改端口无需重设）；
        ///     另提供「立即放行」一键 UAC 写入程序级 Allow 规则（原生弹窗的确定性等价物）。
        /// </summary>
        public static bool EnsureFirewallAccess(Window? owner)
        {
            // 0) 程序级阻止规则（根因：防火墙弹窗点「取消」生成，优先于端口 Allow 规则）→ 需删除
            if (HasProgramBlockRule())
            {
                Log.Warning("[Lan] 检测到本程序的程序级防火墙阻止规则，需删除后才能被手机访问");
                var blockPrompt = new Views.LanFirewallPromptWindow(owner, programBlocked: true);
                if (blockPrompt.ShowDialog() != true)
                {
                    Log.Information("[Lan] 用户选择暂不解除程序级阻止，LAN 仍不可用");
                    return false;
                }
                if (!TryRemoveProgramBlockRules())
                {
                    Log.Warning("[Lan] 删除程序级阻止规则失败（UAC 取消或 netsh 失败），LAN 仍不可用");
                    return false;
                }
            }

            // 1) 已有放行（端口规则覆盖 Start，或程序级 Allow 规则）→ 直接可用
            if (HasAllowRule() || HasProgramAllowRule())
            {
                Log.Debug("[Lan] 已存在放行规则，跳过引导");
                return true;
            }

            // 2) 无任何放行规则 → 引导原生弹窗 / 一键程序级放行
            //    注意：不预写端口规则——一旦写入规则，Windows 的原生弹窗就不会出现。
            var prompt = new Views.LanFirewallPromptWindow(owner);
            if (prompt.ShowDialog() == true && TryAddProgramAllowRule())
            {
                Log.Information("[Lan] 已写入程序级放行规则（覆盖所有端口）");
                return true;
            }

            // 用户选「暂不」或 UAC 取消 → 不阻断：服务照常启动，
            // 手机首次连接时 Windows 会弹「是否允许此应用访问网络？」，点允许即放行。
            Log.Information("[Lan] 未自动写入放行规则：将在手机首次连接时由 Windows 弹窗放行");
            return false;
        }

        /// <summary>
        /// 检测本机是否存在覆盖 <see cref="StartPort"/>（= LanPorts.Start）的 TCP 入站 Allow 规则。
        /// 全量扫描 `show rule name=all dir=in verbose`，解析任意"TCP + Allow + LocalPort 覆盖 StartPort"
        /// 的规则——不限定规则名（程序引导/GUI/手动 netsh 任何方式添加的规则都识别），
        /// 且**按当前 LanPorts.Start 校验端口覆盖**（改端口后旧端口规则不会被误判为已放行，
        /// 2026-08-15 实测：改 Start=6000 后 5000-5100 规则不应算数，须引导放行新端口）。
        /// 规则不存在/检测失败均返回 false（会触发引导；添加幂等，重复添加无害）。
        /// </summary>
        public static bool HasAllowRule()
        {
            try
            {
                string output = RunNetsh("advfirewall firewall show rule name=all dir=in verbose");
                if (string.IsNullOrWhiteSpace(output))
                {
                    Log.Debug("[Lan] 全量扫描输出为空（无法检测），按未放行处理");
                    return false;
                }
                // netsh verbose 输出中规则块按规则名标签开头（中英文环境不同），逐个判断。
                // 注意：当前活动配置文件只查一次（GetCurrentFirewallProfile 会 spawn netsh monitor，
                // 若在 per-block 里调用 = 350 块 × 每块一次进程 → 分钟级卡顿，2026-08-16 修复）。
                string? currentProfile = GetCurrentFirewallProfile();
                var blocks = SplitRuleBlocks(output);
                bool hit = blocks.Any(b => RuleTextCoversStartPort(b, GetAppExecutablePath(), currentProfile));
                Log.Debug("[Lan] 全量扫描：块数={Blocks} 命中={Hit}（StartPort={Start}）", blocks.Length, hit, StartPort);
                return hit;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Lan] 检测防火墙放行规则失败（按未放行处理，将引导用户放行）");
                return false;
            }
        }

        /// <summary>
        /// UAC 提权执行 netsh 添加放行规则。返回 true 表示添加成功（或检测到规则已存在）。
        /// 用户取消 UAC（Win32Exception）返回 false。
        /// </summary>
        public static bool TryAddRule()
        {
            // 二次确认：若规则实际已存在（可能首次检测失败），跳过添加避免累积重复规则
            if (HasAllowRule())
            {
                Log.Debug("[Lan] 添加前检测到规则已存在，跳过");
                return true;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={StartPort}-{EndPort}",
                    Verb = "runas",          // 触发 UAC 提权
                    UseShellExecute = true,  // runas 需要 UseShellExecute
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null)
                {
                    Log.Warning("[Lan] 无法启动 netsh 添加规则");
                    return false;
                }
                if (!p.WaitForExit(30000))
                {
                    try { p.Kill(); } catch { /* 结束失败可忽略 */ }
                    Log.Warning("[Lan] 添加规则超时");
                    return false;
                }
                bool ok = p.ExitCode == 0;
                Log.Information("[Lan] 防火墙放行规则添加结果：{Ok}（端口 {Start}-{End}）", ok, StartPort, EndPort);
                return ok;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 用户点击 UAC「否」/ 取消
                Log.Information("[Lan] 用户取消 UAC，未放行防火墙");
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Lan] 添加防火墙放行规则失败");
                return false;
            }
        }

        /// <summary>
        /// 检测本机是否存在针对**本程序可执行文件**的入站 Block 规则（程序级阻止）。
        /// 来源：Windows 防火墙弹窗（程序首次监听入站时弹出）点「取消」即生成程序级 Block 规则，
        /// 且**程序级 Block 优先于端口级 Allow**（2026-08-15 实测，见 临时文档/LAN问题根因验证结论）——
        /// 存在该规则时，即使端口范围已放行，手机依然连不上（本机即有历史遗留的两条）。
        /// 且存在匹配规则时 Windows 不会弹「是否允许访问」的原生窗口（被静默拦截）。
        /// </summary>
        public static bool HasProgramBlockRule() => FindProgramRulePaths("Block").Count > 0;

        /// <summary>
        /// 检测本机是否存在针对本程序 exe 的入站 Allow 规则（程序级放行）。
        /// 存在即代表已具备程序级网络权限——覆盖**所有端口**，与 LanPorts.Start 无关
        /// （这正是 Windows 原生弹窗点「允许访问」后生成的规则形态）。
        /// </summary>
        public static bool HasProgramAllowRule() => FindProgramRulePaths("Allow").Count > 0;

        /// <summary>扫描全量规则，返回针对本程序的指定 action（Block/Allow）程序级入站规则中记录的 Program 路径列表（去重）</summary>
        internal static List<string> FindProgramRulePaths(string requiredAction)
        {
            var result = new List<string>();
            try
            {
                string exe = GetAppExecutablePath();
                if (string.IsNullOrWhiteSpace(exe))
                {
                    Log.Warning("[Lan] 无法确定本程序可执行文件路径，跳过程序级规则检测");
                    return result;
                }

                string output = RunNetsh("advfirewall firewall show rule name=all dir=in verbose");
                if (string.IsNullOrWhiteSpace(output))
                {
                    Log.Debug("[Lan] 全量扫描输出为空（无法检测程序级规则），按无规则处理");
                    return result;
                }
                var blocks = SplitRuleBlocks(output);
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var b in blocks)
                {
                    if (TryGetProgramRulePath(b, exe, requiredAction, out string? path))
                        paths.Add(path);
                }
                result = paths.ToList();
                Log.Debug("[Lan] 程序级规则扫描：action={Action} 块数={Blocks} 命中={Count}（程序={Exe}）",
                    requiredAction, blocks.Length, result.Count, exe);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Lan] 检测程序级规则失败（按无规则处理）");
            }
            return result;
        }

        /// <summary>
        /// UAC 提权执行 netsh 删除本程序的程序级阻止规则（按规则原文路径逐个删除）。
        /// 返回 true 表示已无程序级阻止（删除成功或删除前已无）。用户取消 UAC / netsh 失败返回 false。
        /// 说明：netsh delete rule 无 action 过滤，按 name=all + dir=in + program=&lt;路径&gt; 删除；
        /// 端口级放行规则（斜月安全管理系统-LAN，无 Program 字段）不受影响。
        /// </summary>
        public static bool TryRemoveProgramBlockRules()
        {
            var paths = FindProgramRulePaths("Block");
            if (paths.Count == 0)
            {
                Log.Debug("[Lan] 删除前检测无程序级阻止规则，跳过");
                return true;
            }

            foreach (string path in paths)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"advfirewall firewall delete rule name=all dir=in program=\"{path}\"",
                        Verb = "runas",          // 触发 UAC 提权
                        UseShellExecute = true,
                        CreateNoWindow = true,
                    };
                    using var p = Process.Start(psi);
                    if (p == null)
                    {
                        Log.Warning("[Lan] 无法启动 netsh 删除程序级阻止规则：{Path}", path);
                        return false;
                    }
                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { /* 结束失败可忽略 */ }
                        Log.Warning("[Lan] 删除程序级阻止规则超时：{Path}", path);
                        return false;
                    }
                    Log.Information("[Lan] 删除程序级阻止规则结果：退出码={Code}（路径={Path}）", p.ExitCode, path);
                    if (p.ExitCode != 0) return false;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Log.Information("[Lan] 用户取消 UAC，未删除程序级阻止规则");
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Lan] 删除程序级阻止规则失败：{Path}", path);
                    return false;
                }
            }

            return FindProgramRulePaths("Block").Count == 0;
        }

        /// <summary>
        /// UAC 提权写入**程序级 Allow 规则**（覆盖所有端口，与 LanPorts.Start 无关）。
        /// 等价于用户在 Windows 原生「是否允许此应用访问网络？」弹窗中点【允许访问】的落盘结果，
        /// 但确定性强（不依赖弹窗时序/用户注意力）。返回 true 表示已具备程序级放行。
        /// 幂等：写入前检测已有则跳过，避免重复 UAC。
        /// </summary>
        public static bool TryAddProgramAllowRule()
        {
            string exe = GetAppExecutablePath();
            if (string.IsNullOrWhiteSpace(exe))
            {
                Log.Warning("[Lan] 无法确定本程序可执行文件路径，无法写入程序级放行规则");
                return false;
            }
            if (HasProgramAllowRule())
            {
                Log.Debug("[Lan] 写入前已存在程序级放行规则，跳过");
                return true;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exe}\"",
                    Verb = "runas",          // 触发 UAC 提权
                    UseShellExecute = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null)
                {
                    Log.Warning("[Lan] 无法启动 netsh 写入程序级放行规则");
                    return false;
                }
                if (!p.WaitForExit(30000))
                {
                    try { p.Kill(); } catch { /* 结束失败可忽略 */ }
                    Log.Warning("[Lan] 写入程序级放行规则超时");
                    return false;
                }
                bool ok = p.ExitCode == 0;
                Log.Information("[Lan] 写入程序级放行规则结果：{Ok}（程序={Exe}）", ok, exe);
                return ok;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Log.Information("[Lan] 用户取消 UAC，未写入程序级放行规则");
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Lan] 写入程序级放行规则失败");
                return false;
            }
        }

        /// <summary>当前进程可执行文件路径（程序级规则检测/删除用；测试不依赖此值，纯函数走 TryGetProgramRulePath）</summary>
        internal static string GetAppExecutablePath()
        {
            try
            {
                return Environment.ProcessPath
                       ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                       ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>解析入站程序级 Block 规则（见 <see cref="TryGetProgramRulePath"/>）</summary>
        internal static bool TryGetProgramBlockPath(string ruleBlock, string exePath, out string? programPath) =>
            TryGetProgramRulePath(ruleBlock, exePath, "Block", out programPath);

        /// <summary>
        /// 解析一条 netsh 规则文本：若为针对指定程序路径的入站规则且 Action 为 requiredAction
        /// （Program 行与 exePath 忽略大小写一致、Direction=In），输出规则中记录的 Program 路径（原文）。
        /// 字段标签与取值支持中英双语言。纯函数，可单测。
        /// </summary>
        internal static bool TryGetProgramRulePath(string ruleBlock, string exePath, string requiredAction, out string? programPath)
        {
            programPath = null;
            if (string.IsNullOrWhiteSpace(ruleBlock) || string.IsNullOrWhiteSpace(exePath)) return false;

            var direction = DirectionField.Match(ruleBlock);
            var program = ProgramField.Match(ruleBlock);
            var action = ActionField.Match(ruleBlock);
            if (!direction.Success || !program.Success || !action.Success) return false;
            if (!string.Equals(NormalizeDirectionValue(direction.Groups[1].Value), "In", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(NormalizeActionValue(action.Groups[1].Value), requiredAction, StringComparison.OrdinalIgnoreCase)) return false;

            string path = program.Groups[1].Value.Trim();
            if (path.Length == 0) return false;
            if (!string.Equals(path, exePath.Trim(), StringComparison.OrdinalIgnoreCase)) return false;

            programPath = path;
            return true;
        }

        /// <summary>
        /// 判断一条 netsh 规则文本是否为覆盖指定端口的 TCP 入站 Allow 规则（纯函数，可单测）。
        /// 字段标签与取值支持中英双语言（见类头注释）。
        /// 2026-08-16 语义修正：
        ///  - **程序级规则（含 Program 行）必须 Program 匹配本程序 exe 才对本程序生效**——否则任意其他程序
        ///    （PowerShell/浏览器/系统服务）的「TCP+Allow+LocalPort=Any」规则都会让 HasAllowRule 误判为已放行，
        ///    干净机器上会静默跳过引导（P0 修复后暴露的潜在回归）；
        ///  - **必须校验 Profiles 包含当前活动配置文件**——一条仅适用于域/专用网络的残留规则在公用网络下
        ///    根本不生效，判"已放行"会跳过引导、不建程序级规则、实际无保护（2026-08-16 梳理的剩余隐患）。
        /// 无 Program 行的端口级规则（如历史「斜月安全管理系统-LAN」5000-5100）对所有程序生效，直接判定。
        /// 注意：本方法**不**查询当前配置文件（查询需 spawn netsh monitor，代价高）——
        /// currentProfile 由调用方查好传入（HasAllowRule 每轮扫描只查一次）。
        /// </summary>
        internal static bool RuleTextCoversStartPort(string ruleBlock, string appExePath, string? currentProfile)
        {
            if (string.IsNullOrWhiteSpace(ruleBlock)) return false;

            var action = ActionField.Match(ruleBlock);
            var protocol = ProtocolField.Match(ruleBlock);
            var ports = LocalPortField.Match(ruleBlock);
            if (!action.Success || !protocol.Success || !ports.Success) return false;
            if (!string.Equals(protocol.Groups[1].Value.Trim(), "TCP", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(NormalizeActionValue(action.Groups[1].Value), "Allow", StringComparison.OrdinalIgnoreCase)) return false;

            // 程序级规则只对本程序生效
            var program = ProgramField.Match(ruleBlock);
            if (program.Success)
            {
                string p = program.Groups[1].Value.Trim();
                if (p.Length == 0 || string.IsNullOrWhiteSpace(appExePath)) return false;
                if (!string.Equals(p, appExePath.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            }

            // 规则必须适用于当前活动配置文件（域/专用/公用）
            var profiles = ProfilesField.Match(ruleBlock);
            if (profiles.Success && !ProfileListContains(profiles.Groups[1].Value, currentProfile))
                return false;

            return PortListCovers(ports.Groups[1].Value, StartPort);
        }

        /// <summary>判断端口列表文本（"5000"、"5000-5100"、"5000,5001,5002"、"Any"/"任何"）是否覆盖指定端口（纯函数，可单测）</summary>
        public static bool PortListCovers(string localPorts, int port)
        {
            if (string.IsNullOrWhiteSpace(localPorts)) return false;
            foreach (var part in localPorts.Split(','))
            {
                string p = part.Trim();
                if (string.Equals(p, "Any", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p, "任何", StringComparison.Ordinal)) return true;

                var range = p.Split('-');
                if (range.Length == 1 && int.TryParse(range[0], out int single) && single == port) return true;
                if (range.Length == 2
                    && int.TryParse(range[0], out int lo)
                    && int.TryParse(range[1], out int hi)
                    && lo <= port && port <= hi)
                    return true;
            }
            return false;
        }

        private static string RunNetsh(string arguments)
        {
            // 通过 cmd 重定向到临时文件再读取，程序不直接捕获子进程管道。
            // 原因（2026-08-15 实测）：直接管道重定向（RedirectStandardOutput）在本运行环境
            // 拿到的输出异常（全量扫描 30 万字符却 Regex.Split 只出 1 块）；写文件方式更稳。
            // 注：块数=1 的真正根因已定位（2026-08-16）：netsh 输出标签本地化 + 解码选择不当，
            // 见 DecodeNetshText / 类头注释，现已按规则标记数择优解码并自检。
            string tempFile = Path.Combine(Path.GetTempPath(), $"lanfw_{Guid.NewGuid():N}.txt");
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c netsh {arguments} > \"{tempFile}\" 2>&1")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 netsh");
                if (!p.WaitForExit(15000))
                {
                    try { p.Kill(); } catch { /* 结束失败可忽略 */ }
                    Log.Warning("[Lan] netsh 执行超时：{Arguments}", arguments);
                    return "";
                }
                if (!File.Exists(tempFile)) return "";
                byte[] raw = File.ReadAllBytes(tempFile);
                string text = DecodeNetshText(raw);
                // 成功执行不再记日志（2026-08-16 日志瘦身）：同参数输出每次内容完全相同，
                // 全量扫描/配置文件查询是高频调用（每轮扫描各一次），成功结果由调用方日志
                // （块数/命中/配置文件判定/添加结果）体现；失败/超时/无规则标记自检告警仍保留，
                // 排障信号不丢失。

                // 自检：扫描命令的输出必须含规则名标记，否则解析必盲（历史 P0 的复发防线）
                if (arguments.Contains("show rule") && CountRuleMarkers(text) == 0)
                {
                    string hex = raw.Length > 0 ? Convert.ToHexString(raw.Take(Math.Min(80, raw.Length)).ToArray()) : "";
                    Log.Warning("[Lan] netsh 扫描输出未识别到规则标记（Rule Name:/规则名称:）！字节数={Bytes} 前80字节={Hex}",
                        raw.Length, hex);
                }
                return text;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Lan] netsh 执行失败：{Arguments}", arguments);
                return "";
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* 清理失败可忽略 */ }
            }
        }

        /// <summary>
        /// 解码 netsh 输出字节（2026-08-16 修复 P0）。输出编码随环境变化：
        ///  - UTF-8 代码页（PowerShell 等）→ 文件为 UTF-8，标签英文；
        ///  - 中文系统默认 GBK 代码页 → 文件为 GBK，标签中文（规则名称: 等）；
        ///  - 极端情况下重定向可能产生 UTF-16。
        /// 原实现"UTF-8 严格 → GBK 回退"有两个缺陷：① GBK 字节可能"碰巧"是合法 UTF-8，
        /// 严格解码不报错但中文全乱；② .NET 未注册 CodePages 时 GetEncoding(936) 直接抛异常，
        /// 落入宽松 UTF-8 兜底仍乱码。现改为：按 BOM 优先，逐个尝试候选编码，
        /// **取包含最多规则名标记（Rule Name:/规则名称:）的候选**——show rule 输出必有该标记，
        /// 标记数即解码正确性的直接判据，同时天然兼容中英文输出。
        /// </summary>
        internal static string DecodeNetshText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";

            // BOM 优先
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);          // UTF-8 BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);       // UTF-16LE BOM
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2); // UTF-16BE BOM

            string best = "";
            int bestMarkers = -1;
            foreach (var enc in CandidateDecoders())
            {
                try
                {
                    string candidate = enc.GetString(bytes);
                    int markers = CountRuleMarkers(candidate);
                    if (markers > bestMarkers)
                    {
                        bestMarkers = markers;
                        best = candidate;
                    }
                    if (markers > 10) break; // 命中足够多，几乎必为正确解码，提前终止
                }
                catch (DecoderFallbackException) { /* 该编码非法，跳过 */ }
                catch (ArgumentException) { /* 编码不可用（如 936 未注册），跳过 */ }
            }

            // 全部失败 → 宽松 UTF-8 兜底（宁可给乱码也不空手）
            if (bestMarkers < 0)
            {
                try { best = Encoding.UTF8.GetString(bytes); } catch { best = ""; }
            }
            return best;
        }

        private static IEnumerable<Encoding> CandidateDecoders()
        {
            yield return new UTF8Encoding(false, true);   // UTF-8 严格（英文标签输出）
            yield return GetGbkEncoding();                // GBK(936)（中文标签输出）
            yield return Encoding.Unicode;                // UTF-16LE
            yield return Encoding.BigEndianUnicode;       // UTF-16BE
        }

        private static Encoding GetGbkEncoding()
        {
            try { return Encoding.GetEncoding(936); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // .NET Core 默认不含非 UTF-8 代码页：未注册抛 ArgumentException，
                // 注册后仍无数据抛 NotSupportedException——两种情况都需注册提供程序（幂等）
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(936);
            }
        }
    }
}
