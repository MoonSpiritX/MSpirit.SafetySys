// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class MoveOrCopyDialog : FluentWindow
    {
        public int Day { get; private set; }
        public string? TargetTag { get; private set; }   // 重命名以避免隐藏基类 Tag

        private readonly int _maxDays;
        private readonly int _currentMonth;

        public MoveOrCopyDialog(string title, int currentMonth, int maxDays)
        {
            InitializeComponent();
            Title = title;
            TitleBarControl.Title = title;
            _currentMonth = currentMonth;
            _maxDays = maxDays;

            DayTextBox.Text = "";
            TagTextBox.Text = "";

            Activated += OnActivated;
            Deactivated += OnDeactivated;

            UpdateHint();
        }

        private void OnActivated(object? sender, EventArgs e)
        {
            SetTextBoxesBorderColor("#0078D4");
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            SetTextBoxesBorderColor("#17c3b2");
        }

        private void SetTextBoxesBorderColor(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            foreach (var textBox in FindVisualChildren<System.Windows.Controls.TextBox>(this))
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

        private void DayTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateHint();
        }

        private void UpdateHint()
        {
            string dayText = DayTextBox.Text.Trim();
            string tag = TagTextBox.Text.Trim();
            string displayTag = string.IsNullOrEmpty(tag) ? "（无标签）" : tag;

            if (string.IsNullOrEmpty(dayText))
            {
                HintTextBlock.Text = "请输入目标日期";
                HintTextBlock.Foreground = Brushes.Green;
                ErrorTextBlock.Visibility = Visibility.Collapsed;
                return;
            }

            if (!int.TryParse(dayText, out int day) || day < 1 || day > _maxDays)
            {
                HintTextBlock.Text = $"目标分组：{_currentMonth:D2}月{dayText:D2}日{displayTag}。\n如果不存在，将自动创建。";
                HintTextBlock.Foreground = Brushes.Gray;
                ErrorTextBlock.Text = $"请输入 1-{_maxDays} 之间的数字";
                ErrorTextBlock.Visibility = Visibility.Visible;
                return;
            }

            HintTextBlock.Text = $"目标分组：{_currentMonth:D2}月{day:D2}日{displayTag}。\n如果不存在，将自动创建。";
            HintTextBlock.Foreground = Brushes.Green;
            ErrorTextBlock.Visibility = Visibility.Collapsed;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string dayText = DayTextBox.Text.Trim();
            if (!int.TryParse(dayText, out int day) || day < 1 || day > _maxDays)
            {
                MessageDialog.Show($"请输入有效的日期（1-{_maxDays}）", "提示");
                return;
            }
            Day = day;
            TargetTag = string.IsNullOrWhiteSpace(TagTextBox.Text) ? null : TagTextBox.Text.Trim();
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