// Copyright (C) 2026 斜月Spirit
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
    public partial class GroupOrder : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<Group> _groups = new();
        private readonly ProjectDataService _dataService;

        public GroupOrder(ProjectDataService dataService, string title)
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
        /// 设置分组列表并绑定 DataGrid
        /// </summary>
        public void SetGroups(System.Collections.Generic.List<Group> groups)
        {
            // 重新编号
            for (int i = 0; i < groups.Count; i++)
                groups[i].Order = i + 1;

            _groups.Clear();
            foreach (var g in groups)
                _groups.Add(g);

            // 先置空再赋值，确保 Items 集合为空
            GroupsDataGrid.ItemsSource = null;
            GroupsDataGrid.ItemsSource = _groups;
        }

        private Group? GetSelectedGroup()
        {
            return GroupsDataGrid.SelectedItem as Group;
        }

        private void MoveGroup(int delta)
        {
            var selected = GetSelectedGroup();
            if (selected == null) return;

            int oldIndex = _groups.IndexOf(selected);
            int newIndex = oldIndex + delta;

            if (newIndex < 0 || newIndex >= _groups.Count)
                return;

            _groups.Move(oldIndex, newIndex);

            // 重新编号
            for (int i = 0; i < _groups.Count; i++)
                _groups[i].Order = i + 1;

            // 保持选中
            GroupsDataGrid.SelectedItem = selected;
        }

        private void TopButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedGroup();
            if (selected == null) return;

            int index = _groups.IndexOf(selected);
            if (index <= 0) return;

            for (int i = index; i > 0; i--)
                _groups.Move(i, i - 1);

            for (int i = 0; i < _groups.Count; i++)
                _groups[i].Order = i + 1;

            GroupsDataGrid.SelectedItem = selected;
        }

        private void UpButton_Click(object sender, RoutedEventArgs e)
        {
            MoveGroup(-1);
        }

        private void DownButton_Click(object sender, RoutedEventArgs e)
        {
            MoveGroup(1);
        }

        private void BottomButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedGroup();
            if (selected == null) return;

            int index = _groups.IndexOf(selected);
            if (index >= _groups.Count - 1) return;

            for (int i = index; i < _groups.Count - 1; i++)
                _groups.Move(i, i + 1);

            for (int i = 0; i < _groups.Count; i++)
                _groups[i].Order = i + 1;

            GroupsDataGrid.SelectedItem = selected;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // 按当前顺序更新 Order
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                group.Order = i;
                _dataService.UpdateGroup(group);
            }

            DialogResult = true;
            Close();
        }
    }
}
