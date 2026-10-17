// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Views;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelStatisticsView : UserControl
    {
        public PersonnelStatisticsView(PersonnelStatisticsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // 初始化下拉菜单项
            ScopeSelector.ItemsSource = viewModel.UnitGroups;
            StatusSelector.ItemsSource = new[] { "全部人员", "在场人员", "退场人员" };
            CategorySelector.ItemsSource = new[] { "全部人员", "三类人员", "特种作业人员" };

            // 默认选中（人员范围 = 进入时的单位；人员状态/类别 = 全部）
            ScopeSelector.SelectedItem = viewModel.SelectedUnit;
            StatusSelector.SelectedItem = viewModel.PersonnelStatus;
            CategorySelector.SelectedItem = viewModel.Category;

            // 选择变更 → ViewModel
            ScopeSelector.SelectionChanged += item =>
            {
                if (item != null) viewModel.SelectedUnit = item;
            };
            StatusSelector.SelectionChanged += item =>
            {
                if (item != null) viewModel.PersonnelStatus = item;
            };
            CategorySelector.SelectionChanged += item =>
            {
                if (item != null) viewModel.Category = item;
            };

            // ViewModel 属性变更 → 同步下拉显示
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PersonnelStatisticsViewModel.SelectedUnit))
                    ScopeSelector.SelectedItem = viewModel.SelectedUnit;
                else if (e.PropertyName == nameof(PersonnelStatisticsViewModel.PersonnelStatus))
                    StatusSelector.SelectedItem = viewModel.PersonnelStatus;
                else if (e.PropertyName == nameof(PersonnelStatisticsViewModel.Category))
                    CategorySelector.SelectedItem = viewModel.Category;
            };
        }

        /// <summary>
        /// 双击行跳转到对应的记录卡
        /// </summary>
        private void StatsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (StatsDataGrid.SelectedItem is PersonnelStatisticsRow row)
            {
                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToRecord(row.GroupId, row.RecordId);
            }
        }

        /// <summary>
        /// 一键导出单位人员统计表到 Excel（导出当前过滤后的数据）
        /// </summary>
        private void OneClickExport_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as PersonnelStatisticsViewModel;
            if (vm == null) return;

            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow == null) return;

            var dataRows = vm.Rows.ToList();

            // 文件名/表格名上下文：范围 + 状态/类别筛选（默认"全部人员"不追加，保持简洁）
            string contextName = $"{vm.GroupName}人员统计";
            if (vm.PersonnelStatus != "全部人员")
                contextName += "-" + vm.PersonnelStatus;
            if (vm.Category != "全部人员")
                contextName += "-" + vm.Category;

            var provider = new Export.PersonnelExportDataProvider(dataRows, contextName);
            Export.OneClickExporter.Export(provider, mainWindow.ViewModel.CurrentProjectName, mainWindow);
        }

        /// <summary>
        /// 拦截 DataGrid 左右键，改为水平滚动，上下键保持行切换
        /// </summary>
        private void StatsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not DataGrid grid) return;

            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                e.Handled = true;

                var scrollViewer = Helpers.DataGridScrollBehavior.FindScrollViewer(grid);
                if (scrollViewer == null) return;

                double step = 80;
                double offset = scrollViewer.HorizontalOffset;

                if (e.Key == Key.Right)
                    offset += step;
                else
                    offset -= step;

                scrollViewer.ScrollToHorizontalOffset(Math.Max(0, offset));
            }
        }
    }
}
