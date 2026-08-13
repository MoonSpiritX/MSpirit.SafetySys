// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class MessageDialog : FluentWindow
    {
        private bool _hasCancelButton = false;
        private bool? _dialogResult = null;

        private MessageDialog(string message, string title, bool showCancelButton = false)
        {
            InitializeComponent();
            Title = title;
            MessageTextBlock.Text = message;
            _hasCancelButton = showCancelButton;

            if (!showCancelButton)
            {
                CancelButton.Visibility = Visibility.Collapsed;
                OkButton.Margin = new Thickness(0);
            }

            OkButton.Click += (s, e) => { _dialogResult = true; Close(); };
            CancelButton.Click += (s, e) => { _dialogResult = false; Close(); };

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    if (_hasCancelButton)
                        _dialogResult = false;
                    else
                        _dialogResult = null; // 单按钮按ESC视为无结果
                    Close();
                }
            };
        }

        // 普通提示（只有确定按钮）
        public static void Show(string message, string title = "提示")
        {
            var dialog = new MessageDialog(message, title, false);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        public static void Show(Window owner, string message, string title = "提示")
        {
            var dialog = new MessageDialog(message, title, false);
            dialog.Owner = owner;
            dialog.ShowDialog();
        }

        // 确认对话框（确定/取消）
        public static bool? Confirm(string message, string title = "确认")
        {
            var dialog = new MessageDialog(message, title, true);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
            return dialog._dialogResult;
        }

        public static bool? Confirm(Window owner, string message, string title = "确认")
        {
            var dialog = new MessageDialog(message, title, true);
            dialog.Owner = owner;
            dialog.ShowDialog();
            return dialog._dialogResult;
        }
    }
}