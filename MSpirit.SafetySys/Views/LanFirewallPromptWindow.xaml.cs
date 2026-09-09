// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>
    /// 防火墙放行引导窗口（由 <see cref="Services.LanFirewallService.EnsureFirewallAccess"/> 弹出）：
    ///  - programBlocked=false：无任何放行规则时提示。默认路径为**让 Windows 弹原生
    ///    「是否允许此应用访问网络？」窗口**（手机首次连接时自动弹出，点允许即生成程序级
    ///    放行规则、覆盖所有端口）；也可点「立即放行」一键 UAC 写入程序级规则（确定性等价物）。
    ///  - programBlocked=true：检测到本程序被程序级阻止规则拦截时提示（阻止优先于端口放行，
    ///    且会抑制原生弹窗），确定 = 删除阻止规则（随后触发 UAC），取消 = 暂不。
    /// </summary>
    public partial class LanFirewallPromptWindow : FluentWindow
    {
        public LanFirewallPromptWindow(Window? owner, bool programBlocked = false)
        {
            InitializeComponent();
            if (owner != null)
                Owner = owner;

            if (programBlocked)
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
            else
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

            AllowButton.Click += (s, e) => { DialogResult = true; Close(); };
            SkipButton.Click += (s, e) => { DialogResult = false; Close(); };
        }
    }
}
