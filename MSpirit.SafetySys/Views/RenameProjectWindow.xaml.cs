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
    public partial class RenameProjectWindow : FluentWindow
    {
        public RenameProjectWindow(ConfigManager configManager, string projectId, string currentName)
        {
            InitializeComponent();
            DataContext = new RenameProjectViewModel(configManager, projectId, currentName);
            Loaded += (s, e) =>
            {
                NewNameTextBox.Focus();
                NewNameTextBox.CaretIndex = NewNameTextBox.Text.Length;
            };

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

        public string NewName => (DataContext as RenameProjectViewModel)?.NewName ?? string.Empty;
    }
}