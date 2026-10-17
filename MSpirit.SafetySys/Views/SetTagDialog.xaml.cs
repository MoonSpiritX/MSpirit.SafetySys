// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class SetTagDialog : FluentWindow
    {
        public string? NewTag { get; private set; }

        private readonly string _dateName;
        private readonly string? _currentTag;
        private readonly List<(string Name, string? Tag)> _existingPairs;

        public SetTagDialog(string dateName, string? currentTag, List<(string Name, string? Tag)> existingPairs)
        {
            InitializeComponent();
            _dateName = dateName;
            _currentTag = currentTag;
            _existingPairs = existingPairs;

            CurrentDateText.Text = dateName;
            TagInput.Text = currentTag ?? "";
            TagInput.Focus();
            TagInput.SelectAll();

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

        private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string input = TagInput.Text.Trim();

            if (string.IsNullOrEmpty(input))
            {
                ErrorText.Text = "输入内容为空时，将清除标签，但空标签应唯一";
                ErrorText.Foreground = Brushes.Green;
                return;
            }

            if (input.Length > 20)
            {
                ErrorText.Text = "标签不能超过20个字符";
                ErrorText.Foreground = Brushes.Red;
                return;
            }

            bool conflict = _existingPairs.Any(p => p.Name == _dateName && p.Tag == input);
            if (conflict)
            {
                ErrorText.Text = $"标签 “{input}” 在同一天的其它分组中已存在，请使用不同标签";
                ErrorText.Foreground = Brushes.Red;
                return;
            }

            ErrorText.Text = "";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string input = TagInput.Text.Trim();

            if (string.IsNullOrEmpty(input))
            {
                if (_existingPairs.Any(p => p.Name == _dateName && p.Tag == null))
                {
                    MessageDialog.Show("已存在无标签分组，不能再添加第二个无标签分组。请先修改现有无标签分组，或为当前分组设置其他标签。", "提示");
                    return;
                }
                NewTag = null;
                DialogResult = true;
                Close();
                return;
            }

            if (input.Length > 20)
            {
                MessageDialog.Show("标签不能超过20个字符", "提示");
                return;
            }

            bool conflict = _existingPairs.Any(p => p.Name == _dateName && p.Tag == input);
            if (conflict)
            {
                MessageDialog.Show($"标签“{input}”在同一天的其它分组中已存在，请使用不同标签", "提示");
                return;
            }

            NewTag = input;
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