// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>关闭程序提示窗口的用户选择</summary>
    public enum ExitChoice
    {
        None,          // 取消（关闭窗口 / Esc）
        MinimizeToTray,// 最小化到托盘
        ExitProgram,   // 退出程序
    }

    /// <summary>
    /// 关闭程序提示窗口（设置菜单「关闭程序」）：显示关闭方式说明，提供
    /// 「最小化到托盘」/「退出程序」两个按钮（全局样式、宽高一致）。
    /// 调用方根据 <see cref="ExitChoice"/> 执行对应行为。
    /// </summary>
    public partial class ExitConfirmWindow : FluentWindow
    {
        private ExitChoice _choice = ExitChoice.None;

        private ExitConfirmWindow()
        {
            InitializeComponent();

            MinimizeButton.Click += (s, e) => { _choice = ExitChoice.MinimizeToTray; Close(); };
            ExitButton.Click += (s, e) => { _choice = ExitChoice.ExitProgram; Close(); };

            // Esc 视为取消（默认 Enter 触发「最小化到托盘」，见 IsDefault）
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _choice = ExitChoice.None;
                    Close();
                }
            };
        }

        /// <summary>以模态方式弹出关闭程序提示窗口，返回用户选择（关闭窗口 / Esc 视为取消）。</summary>
        public static ExitChoice ShowDialog(Window owner)
        {
            var window = new ExitConfirmWindow { Owner = owner };
            window.ShowDialog();
            return window._choice;
        }
    }
}
