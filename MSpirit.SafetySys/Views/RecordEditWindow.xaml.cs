// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MS_SafetyManagementSystem.Models;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class RecordEditWindow : FluentWindow
    {
        public Record? ResultRecord { get; private set; }
        private readonly Record? _existingRecord;
        private readonly string? _groupId;

        public RecordEditWindow(Record? record, string? groupId)
        {
            InitializeComponent();
            _existingRecord = record;
            _groupId = groupId;

            if (record != null)
            {
                TitleTextBox.Text = record.Title;
                ContentTextBox.Text = record.Content;
            }

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

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                MessageDialog.Show("标题不能为空", "提示");
                return;
            }

            ResultRecord = _existingRecord ?? new Record();
            ResultRecord.Title = TitleTextBox.Text.Trim();
            ResultRecord.Content = ContentTextBox.Text;
            ResultRecord.GroupId = _groupId ?? _existingRecord?.GroupId ?? "";

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