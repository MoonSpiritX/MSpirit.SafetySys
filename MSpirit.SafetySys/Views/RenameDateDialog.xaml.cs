// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class RenameDateDialog : FluentWindow
    {
        public int NewDay { get; private set; }

        private readonly int _year;
        private readonly int _month;
        private readonly int _currentDay;
        private readonly int _daysInMonth;

        public RenameDateDialog(int year, int month, int currentDay, int daysInMonth)
        {
            InitializeComponent();
            _year = year;
            _month = month;
            _currentDay = currentDay;
            _daysInMonth = daysInMonth;
            CurrentDateText.Text = $"{year}年{month:D2}月{currentDay:D2}日";
            NewDateInput.Focus();

            // 初始状态：确定按钮禁用（因为输入框为空）
            OkButton.IsEnabled = false;

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

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            string input = NewDateInput.Text.Trim();

            // 如果输入为空，直接禁用确定按钮并返回
            if (string.IsNullOrEmpty(input))
            {
                PreviewText.Text = "请输入两位数字";
                PreviewText.Foreground = Brushes.Red;
                OkButton.IsEnabled = false;
                return;
            }

            if (input.Length != 2 || !int.TryParse(input, out int day))
            {
                PreviewText.Text = "无效格式（需两位数字）";
                PreviewText.Foreground = Brushes.Red;
                OkButton.IsEnabled = false;
                return;
            }

            if (day < 1 || day > _daysInMonth)
            {
                PreviewText.Text = $"{_year}年{_month}月只有{_daysInMonth}天，日期无效";
                PreviewText.Foreground = Brushes.Red;
                OkButton.IsEnabled = false;
                return;
            }

            PreviewText.Text = $"新名称：{_month:D2}月{day:D2}日";
            PreviewText.Foreground = Brushes.Green;
            OkButton.IsEnabled = true;
            NewDay = day;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (OkButton.IsEnabled)
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