// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class ConfirmDialog : FluentWindow
    {
        public ConfirmDialog(string message, string title = "确认删除")
        {
            InitializeComponent();
            Title = title;
            MessageTextBlock.Text = message;
            OkButton.Click += (s, e) => { DialogResult = true; Close(); };
            CancelButton.Click += (s, e) => { DialogResult = false; Close(); };
        }
    }
}