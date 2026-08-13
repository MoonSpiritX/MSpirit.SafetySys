// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class InputDialog : FluentWindow
    {
        public string InputText => InputTextBox.Text;

        public InputDialog(string title = "请输入", string? defaultText = null)
        {
            InitializeComponent();
            Title = title;
            TitleBarControl.Title = title;
            if (defaultText != null)
                InputTextBox.Text = defaultText;

            Activated += (s, e) => SetTextBoxesBorderColor("#0078D4");
            Deactivated += (s, e) => SetTextBoxesBorderColor("#17c3b2");

            // 窗口加载后自动聚焦文本框
            Loaded += (s, e) =>
            {
                InputTextBox.Focus();
                InputTextBox.Select(InputTextBox.Text.Length, 0);
            };
        }

        private void SetTextBoxesBorderColor(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            foreach (var textBox in FindVisualChildren<global::System.Windows.Controls.TextBox>(this))
            {
                textBox.BorderBrush = brush;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}