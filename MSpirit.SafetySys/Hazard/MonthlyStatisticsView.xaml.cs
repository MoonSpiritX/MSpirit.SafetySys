// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Views;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class MonthlyStatisticsView : UserControl
    {
        public MonthlyStatisticsView(MonthlyStatisticsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (StatisticsDataGrid.SelectedItem is MonthlyRecordRow row)
            {
                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToRecord(row.DateGroupId, row.RecordId);
            }
        }

        private void ExportCurrentMonth_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MonthlyStatisticsViewModel;
            if (vm == null) return;

            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow == null) return;

            var exportControl = new Export.ExportControl(mainWindow.ViewModel.CurrentProjectName, mainWindow.ViewModel.CurrentModuleName);
            if (exportControl.DataContext is Export.ExportWindowViewModel exportVm)
            {
                exportVm.SelectedYearMonthText = vm.YearMonthName;
            }
            mainWindow.ShowOverlay(exportControl);
        }

        private void AnalysisCurrentMonth_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MonthlyStatisticsViewModel;
            if (vm == null) return;

            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow == null) return;

            var dataService = mainWindow.ViewModel.GetProjectDataService();
            if (dataService == null) return;

            var analysisControl = new Analysis.AnalysisControl(dataService, mainWindow.ViewModel.CurrentProjectName, mainWindow.ViewModel.CurrentModuleName);
            if (analysisControl.DataContext is Analysis.AnalysisViewModel analysisVm)
            {
                analysisVm.SelectedRangeType = "custom";
                analysisVm.CustomStartText = vm.YearMonthName;
                analysisVm.CustomEndText = vm.YearMonthName;
            }
            mainWindow.ShowOverlay(analysisControl);
        }

        private void OneClickExport_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MonthlyStatisticsViewModel;
            if (vm == null) return;

            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow == null) return;

            var projectDataService = mainWindow.ViewModel.GetProjectDataService();
            if (projectDataService == null) return;

            var dataRows = vm.Rows.ToList();
            var provider = new Export.HazardExportDataProvider(dataRows, projectDataService, $"{vm.YearMonthName}隐患统计");
            Export.OneClickExporter.Export(provider, mainWindow.ViewModel.CurrentProjectName, mainWindow);
        }

        /// <summary>
        /// 拦截 DataGrid 左右键，改为水平滚动，上下键保持行切换
        /// </summary>
        private void StatisticsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
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