// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>托盘右键菜单的单条动作（二级菜单项：显示文本 + 点击行为）</summary>
    public class TrayMenuAction
    {
        public string Text { get; }
        public Action? Action { get; }
        public bool IsEnabled { get; }

        public TrayMenuAction(string text, Action? action, bool isEnabled = true)
        {
            Text = text;
            Action = action;
            IsEnabled = isEnabled;
        }
    }

    /// <summary>托盘右键菜单分组（一级菜单项：分组名 + 二级菜单项列表）</summary>
    public class TrayMenuGroup
    {
        public string Header { get; }
        public IReadOnlyList<TrayMenuAction> Items { get; }

        public TrayMenuGroup(string header, IReadOnlyList<TrayMenuAction> items)
        {
            Header = header;
            Items = items;
        }
    }
}
