// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Services;
using Xunit;

namespace MSpirit.SafetySys.Tests
{
    /// <summary>
    /// LanFirewallService 纯逻辑测试：端口列表覆盖判断 + netsh 规则文本解析。
    /// （Process/netsh 外部调用部分不在此测试，靠手工冒烟验证。）
    /// </summary>
    public class LanFirewallServiceTests
    {
        // ==================== PortListCovers ====================

        [Theory]
        [InlineData("5000", 5000, true)]          // 单端口命中
        [InlineData("5000-5100", 5000, true)]     // 范围起点命中
        [InlineData("5000-5100", 5100, true)]     // 范围终点命中
        [InlineData("5000,5001,5002", 5000, true)]// 逗号列表命中
        [InlineData("Any", 5000, true)]           // Any 全放行
        [InlineData(" 5000 ", 5000, true)]        // 空白容忍
        [InlineData("6000", 5000, false)]         // 不相关端口
        [InlineData("5001-5100", 5000, false)]    // 范围不含 5000
        [InlineData("5001", 5000, false)]         // 单端口不含
        [InlineData("", 5000, false)]
        [InlineData("   ", 5000, false)]
        [InlineData("abc", 5000, false)]          // 非数字
        [InlineData("5000", 5001, false)]         // 反向：5000 规则不覆盖 5001
        public void PortListCovers_ReturnsExpected(string ports, int port, bool expected)
        {
            Assert.Equal(expected, LanFirewallService.PortListCovers(ports, port));
        }

        // ==================== RuleTextCoversStartPort ====================
        // 规则文本的端口范围从 LanPorts 常量生成——改 LanPorts.Start 后本组测试自动跟随
        // （历史教训：曾硬编码 "5000-5100"，Start 改 9100 后这些测试集体失效）。

        private static string BuildRuleText(string localPort) =>
            "Rule Name:                            斜月安全管理系统-LAN\r\n" +
            "----------------------------------------------------------------------\r\n" +
            "Description:                          允许手机扫码连接斜月安全管理系统批量录入服务\r\n" +
            "Enabled:                              Yes\r\n" +
            "Direction:                            In\r\n" +
            "Profiles:                             Domain,Private,Public\r\n" +
            "LocalIP:                              Any\r\n" +
            "RemoteIP:                             Any\r\n" +
            "Protocol:                             TCP\r\n" +
            $"LocalPort:                            {localPort}\r\n" +
            "RemotePort:                           Any\r\n" +
            "Action:                               Allow\r\n";

        /// <summary>覆盖当前 LanPorts.Start..FirewallEnd 的规则文本</summary>
        private static string TcpAllowRuleRange =>
            BuildRuleText($"{LanPorts.Start}-{LanPorts.FirewallEnd}");

        [Fact]
        public void RuleText_TcpAllowRange_CoversStartPort()
        {
            Assert.True(Covers(TcpAllowRuleRange));
        }

        [Fact]
        public void RuleText_TcpAllowSingleStartPort_CoversStartPort()
        {
            string text = BuildRuleText(LanPorts.Start.ToString());
            Assert.True(Covers(text));
        }

        [Fact]
        public void RuleText_TcpBlock_NotCovers()
        {
            string text = TcpAllowRuleRange.Replace("Action:                               Allow", "Action:                               Block");
            Assert.False(Covers(text));
        }

        [Fact]
        public void RuleText_UdpAllow_NotCovers()
        {
            string text = TcpAllowRuleRange.Replace("Protocol:                             TCP", "Protocol:                             UDP");
            Assert.False(Covers(text));
        }

        [Fact]
        public void RuleText_TcpAllowWrongPort_NotCovers()
        {
            // 范围不含 StartPort 的规则（如旧端口段）不得误判为已放行。
            // 端口值必须由常量派生：Start 曾被改为 6000，硬编码 "6000-6099" 恰好覆盖它导致误断言。
            string text = BuildRuleText($"{LanPorts.FirewallEnd + 1}-65535");
            Assert.False(Covers(text));
        }

        [Fact]
        public void RuleText_MissingActionLine_NotCovers()
        {
            int idx = TcpAllowRuleRange.IndexOf("Action:");
            string text = TcpAllowRuleRange.Remove(idx, TcpAllowRuleRange.Length - idx);
            Assert.False(Covers(text));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void RuleText_Empty_NotCovers(string? text)
        {
            Assert.False(Covers(text));
        }

        /// <summary>测试辅助：以"当前配置文件=公用(Public)、程序=任意"显式调用判定（避免单参重载跑 netsh）</summary>
        private static bool Covers(string ruleBlock) =>
            LanFirewallService.RuleTextCoversStartPort(ruleBlock, @"F:\test\app.exe", "Public");

        // ==================== 当前端口现实（全部由 LanPorts 常量派生，不硬编码端口值） ====================
        // 教训（2026-08-16 连续两次踩坑）：把端口值硬编码进测试，LanPorts.Start 一改测试就翻车——
        // 5000→9100 打挂旧用例、9100→5100 打挂"当前端口现实"用例。本组断言全部由常量计算。

        [Fact]
        public void RuleText_当前放行范围_覆盖StartPort()
        {
            // 本程序放行规则的真实形态：LocalPort = Start..FirewallEnd → 必须覆盖 StartPort
            string rule = BuildRuleText($"{LanPorts.Start}-{LanPorts.FirewallEnd}");
            Assert.True(Covers(rule));
        }

        [Fact]
        public void RuleText_旧范围_不覆盖StartPort()
        {
            // 2026-08-15 约定：改 LanPorts.Start 后旧端口规则不得误判为已放行（须引导放行新端口）
            string oldRule = BuildRuleText((LanPorts.Start - 1).ToString());
            Assert.False(Covers(oldRule));
        }

        [Theory]
        [InlineData("9100", 9100, true)]
        [InlineData("9100-9200", 9100, true)]
        [InlineData("9100-9200", 9200, true)]
        [InlineData("9100-9200", 9199, true)]
        [InlineData("9000-9100", 9100, true)]   // 范围终点含目标端口
        [InlineData("9000-9099", 9100, false)]  // 范围不含目标端口
        [InlineData("9100-9200", 9201, false)]
        [InlineData("Any", 9100, true)]
        [InlineData("9100,9200", 9100, true)]
        [InlineData("9100,9200", 9150, false)]
        public void PortListCovers_范围解析不依赖Start(string ports, int port, bool expected)
        {
            // 纯解析测试：数值仅为样例，与 LanPorts.Start 无关
            Assert.Equal(expected, LanFirewallService.PortListCovers(ports, port));
        }

        [Fact]
        public void StartPort_跟随LanPorts常量()
        {
            Assert.Equal(LanPorts.Start, LanFirewallService.StartPort);
        }

        // ==================== 程序级阻止规则（TryGetProgramBlockPath） ====================
        // 夹具取自本机真实 netsh 输出：历史弹窗点「取消」生成的程序级 Block 规则
        // （路径为旧安装形态，全小写；Program 行存在、Direction=In、Action=Block）。

        private const string RealProgramBlockRule =
            "Rule Name:                            斜月安全管理系统\r\n" +
            "----------------------------------------------------------------------\r\n" +
            "Description:                          斜月安全管理系统\r\n" +
            "Enabled:                              Yes\r\n" +
            "Direction:                            In\r\n" +
            "Profiles:                             Public\r\n" +
            "Grouping:                             \r\n" +
            "LocalIP:                              Any\r\n" +
            "RemoteIP:                             Any\r\n" +
            "Protocol:                             TCP\r\n" +
            "LocalPort:                            Any\r\n" +
            "RemotePort:                           Any\r\n" +
            "Edge traversal:                       No\r\n" +
            "Program:                              F:\\【program】\\mspirit.safetysys\\bin\\debug.safetysys\\mspirit.safetysys.exe\r\n" +
            "InterfaceTypes:                       Any\r\n" +
            "Security:                             NotRequired\r\n" +
            "Rule source:                          Local Setting\r\n" +
            "Action:                               Block\r\n";

        [Fact]
        public void ProgramBlock_命中_输出规则原文路径()
        {
            bool ok = LanFirewallService.TryGetProgramBlockPath(
                RealProgramBlockRule,
                @"F:\【Program】\MSpirit.SafetySys\bin\Debug.SafetySys\MSpirit.SafetySys.exe", // 当前路径（大小写不同）
                out string? path);
            Assert.True(ok);
            Assert.Equal(@"F:\【program】\mspirit.safetysys\bin\debug.safetysys\mspirit.safetysys.exe", path);
        }

        [Fact]
        public void ProgramBlock_Allow规则_不命中()
        {
            string text = RealProgramBlockRule.Replace("Action:                               Block", "Action:                               Allow");
            Assert.False(LanFirewallService.TryGetProgramBlockPath(text, @"F:\x\app.exe", out _));
        }

        [Fact]
        public void ProgramBlock_出站规则_不命中()
        {
            string text = RealProgramBlockRule.Replace("Direction:                            In", "Direction:                            Out");
            Assert.False(LanFirewallService.TryGetProgramBlockPath(text, @"F:\x\app.exe", out _));
        }

        [Fact]
        public void ProgramBlock_其他程序路径_不命中()
        {
            Assert.False(LanFirewallService.TryGetProgramBlockPath(
                RealProgramBlockRule, @"D:\other\app.exe", out _));
        }

        [Fact]
        public void ProgramBlock_无Program行_不命中()
        {
            // 端口级放行规则（斜月安全管理系统-LAN）没有 Program 行，不应误判为程序级规则
            string text = RealProgramBlockRule.Replace(
                "Program:                              F:\\【program】\\mspirit.safetysys\\bin\\debug.safetysys\\mspirit.safetysys.exe\r\n",
                "");
            Assert.False(LanFirewallService.TryGetProgramBlockPath(text, @"F:\x\app.exe", out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ProgramBlock_空规则文本_不命中(string? text)
        {
            Assert.False(LanFirewallService.TryGetProgramBlockPath(text!, @"F:\x\app.exe", out _));
        }

        [Fact]
        public void ProgramBlock_空exePath_不命中()
        {
            Assert.False(LanFirewallService.TryGetProgramBlockPath(RealProgramBlockRule, "", out _));
        }

        [Fact]
        public void ProgramAllow_命中_输出规则原文路径()
        {
            // 程序级 Allow 规则（原生弹窗点【允许访问】生成的形态）→ 已放行所有端口
            string allowRule = RealProgramBlockRule.Replace("Action:                               Block", "Action:                               Allow");
            bool ok = LanFirewallService.TryGetProgramRulePath(
                allowRule,
                @"F:\【Program】\MSpirit.SafetySys\bin\Debug.SafetySys\MSpirit.SafetySys.exe",
                "Allow",
                out string? path);
            Assert.True(ok);
            Assert.Equal(@"F:\【program】\mspirit.safetysys\bin\debug.safetysys\mspirit.safetysys.exe", path);
        }

        [Fact]
        public void ProgramAllow规则_按Block解析_不命中()
        {
            string allowRule = RealProgramBlockRule.Replace("Action:                               Block", "Action:                               Allow");
            Assert.False(LanFirewallService.TryGetProgramRulePath(allowRule, @"F:\x\app.exe", "Block", out _));
        }

        [Fact]
        public void ProgramBlock规则_按Allow解析_不命中()
        {
            Assert.False(LanFirewallService.TryGetProgramRulePath(RealProgramBlockRule, @"F:\x\app.exe", "Allow", out _));
        }

        // ==================== ProgramPathEquals（2026-10-15：MSIX 盘符联接假阴性修复） ====================
        // 背景（实测，见 临时文档/R3-LAN防火墙MSIX适配方案.md §11.9）：
        //   系统盘不是唯一安装位置时，包实体落在 D:\WindowsApps\<包全名>\，
        //   而 C:\Program Files\WindowsApps\<包全名> 只是**目录联接**（ReparsePoint）。
        //   系统按清单 windows.firewallRules 自建的规则里 Program 记的是 **D:** 路径，
        //   进程自报的 Environment.ProcessPath 却是 **C:** 路径（实测 Id=14248）→
        //   原样字符串比对必然不等 → 检测假阴性 → 包内每次点「启动服务」都弹手动指引窗。
        //   修法：第 ③ 级「父目录名 == 当前包全名 且 文件名相同」，且**仅在包内启用**。
        // 注意：下面两个常量刻意保持与真实值同构（含发布者哈希），改动时同步。

        private const string PkgFullName = "Spirit.31431668B8E26F_1.2.2.0_x64__n4fgvp176crcr";
        private const string PkgExeOnC = @"C:\Program Files\WindowsApps\" + PkgFullName + @"\MSpirit.SafetySys.exe";
        private const string PkgExeOnD = @"D:\WindowsApps\" + PkgFullName + @"\MSpirit.SafetySys.exe";
        /// <summary>绿色版/开发实例的遗留规则路径（父目录名是 Debug.SafetySys，不是包全名）</summary>
        private const string GreenExe = @"F:\【Program】\MSpirit.SafetySys\bin\Debug.SafetySys\MSpirit.SafetySys.exe";

        /// <summary>构造一条程序级入站规则文本（英文标签形态，Program 行可控）</summary>
        private static string ProgramRuleBlock(
            string programPath,
            string action = "Allow",
            string protocol = "TCP",
            string profiles = "Domain,Private,Public") =>
            "Rule Name:                            斜月安全管理系统\r\n" +
            "Enabled:                              Yes\r\n" +
            "Direction:                            In\r\n" +
            $"Profiles:                             {profiles}\r\n" +
            "LocalIP:                              Any\r\n" +
            "RemoteIP:                             Any\r\n" +
            $"Protocol:                             {protocol}\r\n" +
            "LocalPort:                            Any\r\n" +
            "RemotePort:                           Any\r\n" +
            $"Program:                              {programPath}\r\n" +
            "InterfaceTypes:                       Any\r\n" +
            $"Action:                               {action}\r\n";

        [Fact]
        public void ProgramPathEquals_原样相同_大小写不同也算命中()
        {
            Assert.True(LanFirewallService.ProgramPathEquals(PkgExeOnC, PkgExeOnC.ToLowerInvariant()));
        }

        [Fact]
        public void ProgramPathEquals_正斜杠与尾随空白分隔符_规范化后命中()
        {
            Assert.True(LanFirewallService.ProgramPathEquals(
                @"C:/Program Files/WindowsApps/x.exe  ", @"C:\Program Files\WindowsApps\x.exe\"));
        }

        [Fact]
        public void ProgramPathEquals_盘符联接_规则记D盘进程报C盘_包内命中()
        {
            // 核心修复点：本机实测的真实形态
            Assert.True(LanFirewallService.ProgramPathEquals(PkgExeOnD, PkgExeOnC, PkgFullName));
            Assert.True(LanFirewallService.ProgramPathEquals(PkgExeOnC, PkgExeOnD, PkgFullName));
        }

        [Fact]
        public void ProgramPathEquals_盘符联接_非包内_不命中()
        {
            // 守护：第 ③ 级只在包内启用 → 非包内（绿色版/开发实例）行为与修复前完全一致
            Assert.False(LanFirewallService.ProgramPathEquals(PkgExeOnD, PkgExeOnC));
            Assert.False(LanFirewallService.ProgramPathEquals(PkgExeOnD, PkgExeOnC, null));
            Assert.False(LanFirewallService.ProgramPathEquals(PkgExeOnD, PkgExeOnC, "   "));
        }

        [Fact]
        public void ProgramPathEquals_包内_父目录不是本包全名_不命中()
        {
            // 守护：绿色版的遗留 Debug 放行规则不得被判成"包内已放行"
            // （它对本机包内进程其实毫无作用，漏判只是多弹一次窗；误判则会让手机连不上却无引导）
            Assert.False(LanFirewallService.ProgramPathEquals(GreenExe, PkgExeOnC, PkgFullName));
            Assert.False(LanFirewallService.ProgramPathEquals(GreenExe, PkgExeOnD, PkgFullName));
        }

        [Fact]
        public void ProgramPathEquals_包内_同一包目录但文件名不同_不命中()
        {
            Assert.False(LanFirewallService.ProgramPathEquals(
                @"D:\WindowsApps\" + PkgFullName + @"\Other.exe", PkgExeOnC, PkgFullName));
        }

        [Fact]
        public void ProgramPathEquals_不同目录的同名exe_不命中()
        {
            // 守护 2026-08-16 修的 P0：别的程序的规则不得被判为本程序放行
            Assert.False(LanFirewallService.ProgramPathEquals(@"D:\other\MSpirit.SafetySys.exe", PkgExeOnC, PkgFullName));
            Assert.False(LanFirewallService.ProgramPathEquals(@"D:\other\MSpirit.SafetySys.exe", PkgExeOnC));
        }

        [Theory]
        [InlineData(null, PkgExeOnC)]
        [InlineData("", PkgExeOnC)]
        [InlineData("   ", PkgExeOnC)]
        [InlineData(PkgExeOnC, "")]
        public void ProgramPathEquals_空路径_不命中(string? rulePath, string exePath)
        {
            Assert.False(LanFirewallService.ProgramPathEquals(rulePath, exePath, PkgFullName));
        }

        [Fact]
        public void TryGetProgramRulePath_规则记D盘进程报C盘_包内命中()
        {
            // 串起真实调用链：netsh 规则文本 → 解析 → 路径比对
            bool ok = LanFirewallService.TryGetProgramRulePath(
                ProgramRuleBlock(PkgExeOnD), PkgExeOnC, "Allow", out string? path, PkgFullName);
            Assert.True(ok);
            Assert.Equal(PkgExeOnD, path);   // 输出的是规则原文路径，供删除时原样回填
        }

        [Fact]
        public void TryGetProgramRulePath_同一条规则_非包内不命中()
        {
            Assert.False(LanFirewallService.TryGetProgramRulePath(
                ProgramRuleBlock(PkgExeOnD), PkgExeOnC, "Allow", out _, null));
        }

        [Fact]
        public void RuleTextCoversStartPort_程序级规则记D盘进程报C盘_包内命中()
        {
            // 第二个受影响的调用点：端口级扫描（这条规则 LocalPort=Any，覆盖 LanPorts.Start）
            Assert.True(LanFirewallService.RuleTextCoversStartPort(
                ProgramRuleBlock(PkgExeOnD), PkgExeOnC, "Public", PkgFullName));
        }

        [Fact]
        public void RuleTextCoversStartPort_同一条规则_非包内不命中()
        {
            Assert.False(LanFirewallService.RuleTextCoversStartPort(
                ProgramRuleBlock(PkgExeOnD), PkgExeOnC, "Public", null));
        }

        // ==================== P0 回归：netsh 输出本地化（中文标签/GBK 编码） ====================
        // 根因（2026-08-16 定位）：netsh 字段标签与取值随代码页本地化——中文系统默认 GBK 代码页下
        // 输出「规则名称:/方向:/本地端口:/程序:/操作:」与「入站/任何/允许」，值亦本地化；
        // 原实现硬编码英文 "Rule Name:" 拆分 + 仅 UTF-8/GBK 解码 → 中文输出下块数=1、检测全盲。
        // 下列夹具为真实 GBK 输出形态，验证解码择优 + 中英双标签解析。

        private static string ChineseRuleBlock(string localPort, string action, string program) =>
            "规则名称:                             斜月安全管理系统-LAN\r\n" +
            "----------------------------------------------------------------------\r\n" +
            "描述:                                 允许手机扫码连接斜月安全管理系统批量录入服务\r\n" +
            "已启用:                               是\r\n" +
            "方向:                                 入站\r\n" +
            "配置文件:                             域,专用,公用\r\n" +
            "本地 IP:                              任何\r\n" +
            "远程 IP:                              任何\r\n" +
            "协议:                                 TCP\r\n" +
            $"本地端口:                             {localPort}\r\n" +
            "远程端口:                             任何\r\n" +
            "程序:                                 " + program + "\r\n" +
            "接口类型:                             任何\r\n" +
            "安全:                                 NotRequired\r\n" +
            "规则源:                               本地设置\r\n" +
            $"操作:                                 {action}\r\n";

        private const string ChineseExePath = @"F:\【program】\mspirit.safetysys\bin\debug.safetysys\mspirit.safetysys.exe";

        [Fact]
        public void RuleTextCoversStartPort_中文标签_识别放行规则()
        {
            // 中文标签 + 中文值（允许/任何）+ 端口范围覆盖 Start + Program 匹配本程序 → 判定已放行
            string block = ChineseRuleBlock($"{LanPorts.Start}-{LanPorts.FirewallEnd}", "允许", ChineseExePath);
            Assert.True(LanFirewallService.RuleTextCoversStartPort(block, ChineseExePath, "Public"));
        }

        [Fact]
        public void RuleTextCoversStartPort_中文标签_操作阻止_不识别()
        {
            string block = ChineseRuleBlock($"{LanPorts.Start}-{LanPorts.FirewallEnd}", "阻止", ChineseExePath);
            Assert.False(LanFirewallService.RuleTextCoversStartPort(block, ChineseExePath, "Public"));
        }

        [Fact]
        public void RuleTextCoversStartPort_其他程序TCPAllowAny规则_不判为本程序放行()
        {
            // 2026-08-16 潜在回归：任意其他程序（PowerShell/浏览器等）的 TCP+Allow+LocalPort=Any 规则
            // 若被当作"本程序已放行"，干净机器会静默跳过引导。程序级规则必须 Program 匹配本程序 exe。
            string otherProgramRule =
                "Rule Name:                            PowerShell 7\r\n" +
                "----------------------------------------------------------------------\r\n" +
                "Enabled:                              Yes\r\n" +
                "Direction:                            In\r\n" +
                "Profiles:                             Public\r\n" +
                "Protocol:                             TCP\r\n" +
                "LocalPort:                            Any\r\n" +
                "Program:                              C:\\program files\\powershell\\7\\pwsh.exe\r\n" +
                "Action:                               Allow\r\n";
            Assert.False(LanFirewallService.RuleTextCoversStartPort(otherProgramRule, ChineseExePath, "Public"));

            // 无 Program 行的端口级规则（对所有程序生效）→ 判为已放行
            string portRule = BuildRuleText($"{LanPorts.Start}-{LanPorts.FirewallEnd}");
            Assert.True(LanFirewallService.RuleTextCoversStartPort(portRule, ChineseExePath, "Public"));
        }

        // ==================== Profiles（网络类型）校验（2026-08-16 剩余隐患修复） ====================
        // 规则 Profiles 必须包含当前活动配置文件，否则在公用/专用网络下根本不生效，
        // 判"已放行"会跳过引导、不建程序级规则、实际无保护。

        [Fact]
        public void RuleTextCoversStartPort_仅专用网络规则_公用网络下不判为放行()
        {
            // 英文标签 + Profiles=Private → 当前公用(Public)网络下不生效
            string rule = BuildRuleText($"{LanPorts.Start}-{LanPorts.FirewallEnd}")
                .Replace("Profiles:                             Domain,Private,Public", "Profiles:                             Private");
            Assert.False(LanFirewallService.RuleTextCoversStartPort(rule, @"F:\x\app.exe", "Public"));
        }

        [Fact]
        public void RuleTextCoversStartPort_含公用网络规则_公用网络下判为放行()
        {
            string rule = BuildRuleText($"{LanPorts.Start}-{LanPorts.FirewallEnd}");
            Assert.True(LanFirewallService.RuleTextCoversStartPort(rule, @"F:\x\app.exe", "Public"));
        }

        [Fact]
        public void RuleTextCoversStartPort_中文配置文件专用_公用网络下不判为放行()
        {
            // 中文标签 + 配置文件=专用 → 当前公用(Public)网络下不生效
            string block = ChineseRuleBlock($"{LanPorts.Start}-{LanPorts.FirewallEnd}", "允许", ChineseExePath)
                .Replace("配置文件:                             域,专用,公用", "配置文件:                             专用");
            Assert.False(LanFirewallService.RuleTextCoversStartPort(block, ChineseExePath, "Public"));
        }

        [Fact]
        public void RuleTextCoversStartPort_当前配置文件未知_宽松判定不误伤()
        {
            // currentProfile=null（无法获取）→ 宽松返回 true（避免把本可用的放行误判为不可用）
            string rule = BuildRuleText($"{LanPorts.Start}-{LanPorts.FirewallEnd}");
            Assert.True(LanFirewallService.RuleTextCoversStartPort(rule, @"F:\x\app.exe", null));
        }

        [Theory]
        [InlineData("Domain,Private,Public", "Public", true)]
        [InlineData("Domain,Private,Public", "Private", true)]
        [InlineData("Public", "Public", true)]
        [InlineData("Private", "Public", false)]
        [InlineData("Domain", "Public", false)]
        [InlineData("域,专用,公用", "Public", true)]   // 中文值
        [InlineData("专用", "Public", false)]
        [InlineData("域,专用,公用", "Domain", true)]
        [InlineData("域,专用,公用", null, true)]        // 当前未知 → 宽松
        public void ProfileListContains_包含判定(string profiles, string? current, bool expected)
        {
            Assert.Equal(expected, LanFirewallService.ProfileListContains(profiles, current));
        }

        [Theory]
        [InlineData("Public", "Public")]
        [InlineData("公用", "Public")]
        [InlineData("Private", "Private")]
        [InlineData("专用", "Private")]
        [InlineData("Domain", "Domain")]
        [InlineData("域", "Domain")]
        public void NormalizeProfile_中英归一化(string value, string expected)
        {
            Assert.Equal(expected, LanFirewallService.NormalizeProfile(value));
        }

        [Fact]
        public void TryGetProgramRulePath_中文标签_识别阻止规则()
        {
            string block = ChineseRuleBlock("任何", "阻止", ChineseExePath);
            bool ok = LanFirewallService.TryGetProgramRulePath(
                block, @"F:\【Program】\MSpirit.SafetySys\bin\Debug.SafetySys\MSpirit.SafetySys.exe", "Block", out string? path);
            Assert.True(ok);
            Assert.Equal(ChineseExePath, path);
        }

        [Fact]
        public void TryGetProgramRulePath_方向单字入_识别放行规则()
        {
            // 2026-08-16 netsh 中文怪癖回归：仅指定程序（无显式 dir 协议/端口）的规则，
            // 方向被写成单个「入」（U+5165）而非「入站」——漏认该形态会导致 HasProgramAllowRule 永远命中=0。
            string block = ChineseRuleBlock("任何", "允许", ChineseExePath)
                .Replace("方向:                                 入站", "方向:                                 入");
            bool ok = LanFirewallService.TryGetProgramRulePath(block, ChineseExePath, "Allow", out string? path);
            Assert.True(ok, "方向=「入」的单字形态未被识别（netsh 中文怪癖回归）");
            Assert.Equal(ChineseExePath, path);
        }

        [Fact]
        public void NormalizeDirectionValue_入与入站_均归一为In()
        {
            Assert.Equal("In", LanFirewallService.NormalizeDirectionValue("入站"));
            Assert.Equal("In", LanFirewallService.NormalizeDirectionValue("入"));
            Assert.Equal("In", LanFirewallService.NormalizeDirectionValue("In"));
            Assert.Equal("Out", LanFirewallService.NormalizeDirectionValue("出站"));
            Assert.Equal("Out", LanFirewallService.NormalizeDirectionValue("出"));
        }

        [Fact]
        public void PortListCovers_任何_视为Any()
        {
            Assert.True(LanFirewallService.PortListCovers("任何", LanPorts.Start));
        }

        [Fact]
        public void SplitRuleBlocks_中文标签_拆出多块()
        {
            string output = ChineseRuleBlock("任何", "允许", ChineseExePath)
                           + "\r\n" + ChineseRuleBlock("任何", "阻止", ChineseExePath);
            // lookahead 拆分会在开头留一个空元素（"" + 两块），非空块数为 2
            Assert.Equal(2, LanFirewallService.SplitRuleBlocks(output).Count(b => !string.IsNullOrWhiteSpace(b)));
        }

        [Fact]
        public void DecodeNetshText_GBK中文输出_还原规则标记()
        {
            // 中文系统默认代码页（chcp 936）下 netsh 写出的 GBK 字节；且该字节串常"碰巧"是合法 UTF-8
            // （严格 UTF-8 解码不报错但乱码、无标记）——必须按标记数择优选中 GBK。
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            string chinese = ChineseRuleBlock("任何", "允许", ChineseExePath);
            byte[] gbkBytes = Encoding.GetEncoding(936).GetBytes(chinese);

            string decoded = LanFirewallService.DecodeNetshText(gbkBytes);
            Assert.True(decoded.Contains("规则名称"), "GBK 解码未还原中文标签（P0 回归）");
            Assert.True(LanFirewallService.CountRuleMarkers(decoded) >= 1);
            // 解码结果必须能被继续解析（Program 匹配本程序路径）
            Assert.True(LanFirewallService.RuleTextCoversStartPort(decoded, ChineseExePath, "Public"));
        }

        [Fact]
        public void DecodeNetshText_UTF8英文输出_还原标记()
        {
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(RealProgramBlockRule);
            string decoded = LanFirewallService.DecodeNetshText(utf8Bytes);
            Assert.True(decoded.Contains("Rule Name:"));
            Assert.True(LanFirewallService.CountRuleMarkers(decoded) >= 1);
        }

        [Fact]
        public void DecodeNetshText_UTF16LE_还原标记()
        {
            byte[] utf16Bytes = Encoding.Unicode.GetBytes(RealProgramBlockRule);
            string decoded = LanFirewallService.DecodeNetshText(utf16Bytes);
            Assert.True(decoded.Contains("Rule Name:"), "UTF-16LE 解码未还原规则标记（解码择优失败）");
        }

        [Fact]
        public void DecodeNetshText_空输入_返回空串()
        {
            Assert.Equal("", LanFirewallService.DecodeNetshText(Array.Empty<byte>()));
            Assert.Equal("", LanFirewallService.DecodeNetshText(null!));
        }

        // ==================== 放行动作决策（DecideAction，2026-10-15 新增） ====================
        // R3：MSIX 包内无法自助提权（Process.Start(Verb=runas) 需 allowElevation，商店不批，
        // 且其语义是"启动时提权"而非"运行中按需提权"）→ "该走哪条路"必须按形态分流。
        // DecideAction 是纯函数，覆盖全部组合；包内分支的 UI 文案不在单测范围（需真实环境/手工冒烟）。

        [Fact]
        public void DecideAction_包外_无放行_走提权引导()
        {
            // 非打包形态（开发调试 / 未来商店外的 EXE、MSI 分发）：行为不退化
            Assert.Equal(LanFirewallService.FirewallAction.PromptAndElevate,
                LanFirewallService.DecideAction(packaged: false, hasProgramBlock: false, hasAllow: false));
        }

        [Fact]
        public void DecideAction_包外_有阻止规则_走提权引导()
        {
            Assert.Equal(LanFirewallService.FirewallAction.PromptAndElevate,
                LanFirewallService.DecideAction(packaged: false, hasProgramBlock: true, hasAllow: false));
        }

        [Fact]
        public void DecideAction_包外_已有放行_直接放行()
        {
            Assert.Equal(LanFirewallService.FirewallAction.Proceed,
                LanFirewallService.DecideAction(packaged: false, hasProgramBlock: false, hasAllow: true));
        }

        [Fact]
        public void DecideAction_包内_无放行_走手动指引()
        {
            // 核心新增：包内绝不弹 UAC 引导，改为手动指引窗
            Assert.Equal(LanFirewallService.FirewallAction.PromptManualOnly,
                LanFirewallService.DecideAction(packaged: true, hasProgramBlock: false, hasAllow: false));
        }

        [Fact]
        public void DecideAction_包内_有阻止规则_走手动指引()
        {
            // 死锁场景（商店版首次连接点「取消」生成阻止规则，程序自身删不掉）→ 仍走手动指引，绝不提权
            Assert.Equal(LanFirewallService.FirewallAction.PromptManualOnly,
                LanFirewallService.DecideAction(packaged: true, hasProgramBlock: true, hasAllow: false));
        }

        [Fact]
        public void DecideAction_包内_已有放行_直接放行()
        {
            // R3a 生效（清单 windows.firewallRules 的规则被检测命中）即走此路：零交互
            Assert.Equal(LanFirewallService.FirewallAction.Proceed,
                LanFirewallService.DecideAction(packaged: true, hasProgramBlock: false, hasAllow: true));
        }

        [Fact]
        public void DecideAction_有阻止规则时_即使有放行也不得直接放行()
        {
            // 程序级 Block 优先于放行规则（2026-08-15 实测）→ hasAllow 不得越过阻止判定
            Assert.NotEqual(LanFirewallService.FirewallAction.Proceed,
                LanFirewallService.DecideAction(packaged: false, hasProgramBlock: true, hasAllow: true));
            Assert.NotEqual(LanFirewallService.FirewallAction.Proceed,
                LanFirewallService.DecideAction(packaged: true, hasProgramBlock: true, hasAllow: true));
        }

        [Fact]
        public void IsRunningPackaged_调用不抛异常()
        {
            // 值不稳定（测试宿主非包内；若本机设了调试开关 MS_SPIRIT_FORCE_PACKAGED=1 则为 true），
            // 故只做"安全降级不抛异常"保护（GetCurrentPackageFullName 不可用时须回落 false）。
            _ = LanFirewallService.IsRunningPackaged();
        }

        // ==================== 强制手动指引调试开关（TryForceManualPrompt，2026-10-15 新增） ====================
        // 为什么需要：本机若已存在放行规则，DecideAction 会走 Proceed，手动指引窗**根本不弹**；
        // 而禁用规则同样无效（检测逻辑不读 Enabled 字段，netsh 照列禁用规则）→ 未打包时
        // 只能靠该开关绕过全部检测，才能目视验证窗口排版/按钮/文案。

        [Fact]
        public void TryForceManualPrompt_未设开关_返回false()
        {
            // 显式清空（而非依赖外部环境）：用户为目视验证设过该变量后跑测试，本用例仍须成立
            WithEnv("MS_SPIRIT_FORCE_MANUAL_PROMPT", null, () =>
            {
                Assert.False(LanFirewallService.TryForceManualPrompt(out bool programBlocked));
                Assert.False(programBlocked);
            });
        }

        [Fact]
        public void TryForceManualPrompt_设为1_返回true且按非阻止形态()
        {
            WithEnv("MS_SPIRIT_FORCE_MANUAL_PROMPT", "1", () =>
            {
                Assert.True(LanFirewallService.TryForceManualPrompt(out bool programBlocked));
                Assert.False(programBlocked);
            });
        }

        [Fact]
        public void TryForceManualPrompt_设为block_返回true且按阻止形态()
        {
            WithEnv("MS_SPIRIT_FORCE_MANUAL_PROMPT", "block", () =>
            {
                Assert.True(LanFirewallService.TryForceManualPrompt(out bool programBlocked));
                Assert.True(programBlocked);
            });
        }

        [Fact]
        public void TryForceManualPrompt_值忽略大小写与首尾空白()
        {
            WithEnv("MS_SPIRIT_FORCE_MANUAL_PROMPT", " BLOCK ", () =>
            {
                Assert.True(LanFirewallService.TryForceManualPrompt(out bool programBlocked));
                Assert.True(programBlocked);
            });
        }

        /// <summary>临时设置环境变量并在用例结束后还原（进程级全局状态，必须保证还原）</summary>
        private static void WithEnv(string name, string? value, Action body)
        {
            string? old = Environment.GetEnvironmentVariable(name);
            try
            {
                Environment.SetEnvironmentVariable(name, value);
                body();
            }
            finally
            {
                Environment.SetEnvironmentVariable(name, old);
            }
        }
    }
}
