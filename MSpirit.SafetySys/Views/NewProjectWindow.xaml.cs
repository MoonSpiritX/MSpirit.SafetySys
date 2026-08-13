// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MS_SafetyManagementSystem.Services;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class NewProjectWindow : FluentWindow
    {
        public NewProjectWindow(ConfigManager configManager)
        {
            InitializeComponent();
            DataContext = new NewProjectViewModel(configManager);
            Loaded += (s, e) => ProjectNameTextBox.Focus();

            // 窗口激活/失活时改变所有文本框边框颜色
            Activated += (s, e) => SetTextBoxesBorderColor("#0078D4");
            Deactivated += (s, e) => SetTextBoxesBorderColor("#17c3b2");
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
    }
}