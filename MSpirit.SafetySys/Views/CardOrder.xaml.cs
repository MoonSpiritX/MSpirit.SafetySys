// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Views
{
    public partial class CardOrder : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<Record> _records = new();
        private readonly ProjectDataService _dataService;

        public CardOrder(ProjectDataService dataService, string title)
        {
            InitializeComponent();

            _dataService = dataService;
            DataContext = this;
            Title = title;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Close();
            };
        }

        /// <summary>
        /// 设置记录列表并绑定 DataGrid
        /// </summary>
        public void SetRecords(System.Collections.Generic.List<Record> records)
        {
            // 重新编号
            for (int i = 0; i < records.Count; i++)
                records[i].DisplayIndex = i + 1;

            _records.Clear();
            foreach (var r in records)
                _records.Add(r);

            // 先置空再赋值，确保 Items 集合为空
            RecordsDataGrid.ItemsSource = null;
            RecordsDataGrid.ItemsSource = _records;
        }

        private Record? GetSelectedRecord()
        {
            return RecordsDataGrid.SelectedItem as Record;
        }

        private void MoveRecord(int delta)
        {
            var selected = GetSelectedRecord();
            if (selected == null) return;

            int oldIndex = _records.IndexOf(selected);
            int newIndex = oldIndex + delta;

            if (newIndex < 0 || newIndex >= _records.Count)
                return;

            _records.Move(oldIndex, newIndex);

            // 重新编号
            for (int i = 0; i < _records.Count; i++)
                _records[i].DisplayIndex = i + 1;

            // 保持选中
            RecordsDataGrid.SelectedItem = selected;
        }

        private void TopButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedRecord();
            if (selected == null) return;

            int index = _records.IndexOf(selected);
            if (index <= 0) return;

            for (int i = index; i > 0; i--)
                _records.Move(i, i - 1);

            for (int i = 0; i < _records.Count; i++)
                _records[i].DisplayIndex = i + 1;

            RecordsDataGrid.SelectedItem = selected;
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            MoveRecord(-1);
        }

        private void DownButton_Click(object sender, RoutedEventArgs e)
        {
            MoveRecord(1);
        }

        private void BottomButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedRecord();
            if (selected == null) return;

            int index = _records.IndexOf(selected);
            if (index >= _records.Count - 1) return;

            for (int i = index; i < _records.Count - 1; i++)
                _records.Move(i, i + 1);

            for (int i = 0; i < _records.Count; i++)
                _records[i].DisplayIndex = i + 1;

            RecordsDataGrid.SelectedItem = selected;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // 按当前顺序更新 DisplayIndex
            for (int i = 0; i < _records.Count; i++)
            {
                var record = _records[i];
                record.DisplayIndex = i + 1;
                _dataService.UpdateRecord(record);
            }

            DialogResult = true;
            Close();
        }
    }
}
