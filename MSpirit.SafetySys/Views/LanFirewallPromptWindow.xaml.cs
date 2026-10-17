// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Serilog;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Services;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>
    /// 防火墙放行引导窗口（由 <see cref="LanFirewallService.EnsureFirewallAccess"/> 弹出），三种形态：
    ///  - **默认**（manualOnly=false / programBlocked=false）：无任何放行规则时提示。默认路径为**让 Windows
    ///    弹原生「是否允许此应用访问网络？」窗口**（手机首次连接时自动弹出，点允许即生成程序级
    ///    放行规则、覆盖所有端口）；也可点「立即放行」一键 UAC 写入程序级规则（确定性等价物）。
    ///  - **程序级阻止**（programBlocked=true）：检测到本程序被程序级阻止规则拦截时提示（阻止优先于端口
    ///    放行，且会抑制原生弹窗），确定 = 删除阻止规则（随后触发 UAC），取消 = 暂不。
    ///  - **包内手动指引**（manualOnly=true，2026-10-15 新增，R3 方案）：MSIX 商店版**无法自助提权**
    ///    （详见 `LanFirewallService.IsRunningPackaged` 注释）→ 不提供「立即放行 / 立即解除阻止」，
    ///    改为「打开防火墙设置 / 重新检测 / 复制命令」+ 手动步骤；且**先让用户用手机试连**——
    ///    清单规则可能已生效、只是检测未命中（Program 字段比对的假阴性），直接引导改防火墙可能纯属多余。
    /// </summary>
    public partial class LanFirewallPromptWindow : FluentWindow
    {
        /// <summary>提示文字配色：中性灰（进行中/操作提示）、警示红（未命中）</summary>
        private static readonly Brush HintNormalBrush = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
        private static readonly Brush HintWarnBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));

        /// <summary>放行端口区间文案（不写死数字，统一引用 Common/LanPorts）</summary>
        private static string PortRangeText => $"{LanPorts.Start}-{LanPorts.FirewallEnd}";

        public LanFirewallPromptWindow(Window? owner, bool programBlocked = false, bool manualOnly = false)
        {
            InitializeComponent();
            if (owner != null)
                Owner = owner;

            if (manualOnly) ApplyManualOnlyText(programBlocked);
            else if (programBlocked) ApplyBlockedText();
            else ApplyDefaultText();

            // 包内形态：隐藏一键提权按钮，切到手动指引按钮行
            MainButtonRow.Visibility = manualOnly ? Visibility.Collapsed : Visibility.Visible;
            ManualButtonRow.Visibility = manualOnly ? Visibility.Visible : Visibility.Collapsed;
            StepExtraText.Visibility = manualOnly ? Visibility.Visible : Visibility.Collapsed;

            AllowButton.Click += (s, e) => { DialogResult = true; Close(); };
            SkipButton.Click += (s, e) => { DialogResult = false; Close(); };

            OpenFirewallSettingsButton.Click += (s, e) => OpenFirewallSettings();
            CopyCommandButton.Click += (s, e) => CopyNetshCommand(programBlocked);
            RecheckButton.Click += RecheckButton_Click;
            ManualCloseButton.Click += (s, e) => { DialogResult = false; Close(); };
        }

        // ── 文案 ──

        /// <summary>默认形态（2026-08-16 起的文案，逐字保留）</summary>
        private void ApplyDefaultText()
        {
            PortIntroText.Text =
                "手机通过局域网连接本程序，需要 Windows 防火墙允许本程序访问网络。\n\n" +
                "推荐方式：启动服务后用手机扫码连接，首次连接时 Windows 会弹出" +
                "『是否允许此应用访问网络？』，勾选网络类型并点【允许访问】即可" +
                "（生成程序级放行规则，覆盖所有端口，以后改端口也无需重新设置）。";
            StepIntroText.Text = "或点下方按钮一键放行（程序级规则，同样覆盖所有端口）：";
            Step1Text.Text = "1. 点击下方「立即放行（推荐）」；";
            Step2Text.Text = "2. 在系统弹出的窗口中点击「是」；";
            Step3Text.Text = "3. 完成后即可用手机扫码连接。";
            SkipNoteText.Text = "选择「暂不」也可以：手机首次连接时 Windows 会弹出『是否允许访问网络』窗口，" +
                "请务必点【允许访问】——若点【取消】会生成阻止规则，手机将无法连接（下次启动时可点『立即解除阻止』一键恢复）。";
        }

        /// <summary>程序级阻止形态（2026-08-16 起的文案，逐字保留）</summary>
        private void ApplyBlockedText()
        {
            PortIntroText.Text =
                "检测到本程序曾被 Windows 防火墙阻止（首次连接时系统弹窗若点「取消」会记住阻止规则）。" +
                "阻止规则优先于端口放行，且会阻止系统再次弹出询问窗口——手机将无法连接，需要删除该阻止规则。";
            StepIntroText.Text = "操作步骤：";
            Step1Text.Text = "1. 点击下方「立即解除阻止（推荐）」；";
            Step2Text.Text = "2. 在系统弹出的窗口中点击「是」；";
            Step3Text.Text = "3. 完成后重新点击「启动服务」即可。";
            SkipNoteText.Text = "选择「暂不」将无法用手机连接。";
            AllowButton.Content = "立即解除阻止（推荐）";
        }

        /// <summary>包内手动指引形态（2026-10-15 新增）：先试连、再手动配置，全程不提权</summary>
        private void ApplyManualOnlyText(bool programBlocked)
        {
            if (programBlocked)
            {
                PortIntroText.Text =
                    "检测到本程序曾被 Windows 防火墙阻止（首次连接时系统弹窗若点「取消」会记住阻止规则）。" +
                    "阻止规则优先于端口放行，且会阻止系统再次弹出询问窗口——手机将无法连接。\n\n" +
                    "商店版没有管理员权限，无法自动解除，需要你手动删除该阻止规则。";
                StepIntroText.Text = "手动操作（需要管理员权限）：";
                Step1Text.Text = "1. 点下方「打开防火墙设置」→ 左侧「高级设置」→「入站规则」；";
                Step2Text.Text = "2. 找到程序为本程序、动作为「阻止」的规则，右键删除；";
                Step3Text.Text = $"3. 若列表中没有本程序的放行规则，再新建一条 TCP 入站允许规则（端口 {PortRangeText}）；";
                StepExtraText.Text = "4. 完成后点下方「重新检测」，检测到即可继续启动服务。";
                SkipNoteText.Text = "也可以用「复制命令」，在管理员命令提示符中粘贴执行（等效操作）。";
            }
            else
            {
                PortIntroText.Text =
                    "未确认防火墙放行状态——商店版通常在安装时已自动配置好防火墙规则。\n\n" +
                    "请先用手机扫码试连：能连上就不用管下面的步骤。";
                StepIntroText.Text = "若手机连不上，请手动添加放行规则（需要管理员权限）：";
                Step1Text.Text = "1. 点下方「打开防火墙设置」→ 左侧「高级设置」→「入站规则」→「新建规则」；";
                Step2Text.Text = $"2. 规则类型选「端口」→ TCP → 特定本地端口填 {PortRangeText}；";
                Step3Text.Text = "3. 操作选「允许连接」，配置文件全部勾选，命名后保存；";
                StepExtraText.Text = "4. 保存后点下方「重新检测」，检测到即可继续启动服务。";
                SkipNoteText.Text = "也可以用「复制命令」，在管理员命令提示符中粘贴执行（等效操作）。";
            }
        }

        // ── 手动模式交互 ──

        /// <summary>打开「Windows Defender 防火墙」控制面板（不经提权，包内亦可执行）</summary>
        private void OpenFirewallSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("control.exe", "firewall.cpl") { UseShellExecute = true });
                Log.Information("[Lan] 已打开防火墙设置");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Lan] 打开防火墙设置失败");
                ShowHint("无法打开防火墙设置，请手动进入「控制面板 → Windows Defender 防火墙」。", warn: true);
            }
        }

        /// <summary>
        /// 复制 netsh 命令：与程序自我提权等效的操作，供管理员命令提示符中粘贴执行
        /// （主要服务于电话指导 / 远程协助——目标用户是非技术的安全管理人员）。
        /// </summary>
        private void CopyNetshCommand(bool programBlocked)
        {
            string exe = LanFirewallService.GetAppExecutablePath();
            if (string.IsNullOrWhiteSpace(exe))
            {
                Log.Warning("[Lan] 未能确定本程序路径，无法生成防火墙命令");
                ShowHint("未能确定本程序路径，无法生成命令。请按上面的步骤在防火墙设置中手动配置。", warn: true);
                return;
            }

            string command = programBlocked
                ? $"netsh advfirewall firewall delete rule name=all dir=in program=\"{exe}\""
                : $"netsh advfirewall firewall add rule name=\"{LanFirewallService.RuleName}\" " +
                  $"dir=in action=allow program=\"{exe}\"";

            try
            {
                Clipboard.SetText(command);
                Log.Information("[Lan] 已复制防火墙命令到剪贴板（阻止={Block}）", programBlocked);
                ShowHint("命令已复制。请在管理员命令提示符（开始菜单搜索 cmd → 右键「以管理员身份运行」）中粘贴并回车。", warn: false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Lan] 复制防火墙命令失败");
                ShowHint("复制失败，请按上面的步骤在防火墙设置中手动配置。", warn: true);
            }
        }

        /// <summary>
        /// 重新检测放行状态：手动配置完成后无需重启服务即可验证
        /// （包内没有一键修复，这是唯一的即时反馈）。命中则关窗返回 true，调用方随之继续启动服务。
        /// </summary>
        private async void RecheckButton_Click(object sender, RoutedEventArgs e)
        {
            RecheckButton.IsEnabled = false;
            ShowHint("正在检测防火墙规则…", warn: false);

            // netsh 全量扫描需数秒（最多三次 spawn）→ 放后台线程，避免界面假死
            bool ok = await Task.Run(() =>
                !LanFirewallService.HasProgramBlockRule()
                && (LanFirewallService.HasProgramAllowRule() || LanFirewallService.HasAllowRule()));

            // 检测期间用户可能已点「关闭」关掉窗口 —— 此时窗口已卸载，
            // 再设 DialogResult 会抛 InvalidOperationException，故先判 IsLoaded。
            if (!IsLoaded)
            {
                Log.Information("[Lan] 重新检测结束时窗口已关闭，忽略结果（命中={Ok}）", ok);
                return;
            }

            if (ok)
            {
                Log.Information("[Lan] 手动配置后重新检测：已检测到放行规则");
                DialogResult = true;
                Close();
                return;
            }

            RecheckButton.IsEnabled = true;
            Log.Information("[Lan] 手动配置后重新检测：仍未检测到放行规则");
            ShowHint("仍未检测到放行规则。请确认规则已保存（或阻止规则已删除），再点一次「重新检测」。", warn: true);
        }

        private void ShowHint(string text, bool warn)
        {
            RecheckHintText.Foreground = warn ? HintWarnBrush : HintNormalBrush;
            RecheckHintText.Text = text;
            RecheckHintText.Visibility = Visibility.Visible;
        }
    }
}
