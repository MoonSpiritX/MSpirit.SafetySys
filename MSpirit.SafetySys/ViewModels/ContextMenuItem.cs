// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.ViewModels
{
    public class ContextMenuItem
    {
        public string Header { get; set; } = "";
        public ICommand? Command { get; set; }
        public object? CommandParameter { get; set; }
        public Visibility Visibility { get; set; } = Visibility.Visible;
    }
}