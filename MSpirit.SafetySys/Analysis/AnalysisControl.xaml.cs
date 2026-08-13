// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using LiveChartsCore.SkiaSharpView.WPF;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Export;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Analysis
{
    public partial class AnalysisControl : UserControl
    {
        private readonly AnalysisViewModel _viewModel;
        private DropdownMenu? _activeStartDateMenu;
        private DropdownMenu? _activeEndDateMenu;

        /// <summary>
        /// 退出数据分析时触发，供 MainWindow 恢复常规视图
        /// </summary>
        public event Action? ExitRequested;

        public AnalysisControl(ProjectDataService dataService, string projectName, string moduleName)
        {
            InitializeComponent();

            _viewModel = new AnalysisViewModel(dataService, projectName, moduleName);
            DataContext = _viewModel;

            _viewModel.ExportMessage += msg =>
            {
                var win = Window.GetWindow(this);
                if (win != null) ToastService.Show(msg, win);
            };

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 设置所有图表的背景为透明
            foreach (var chart in FindVisualChildren<CartesianChart>(this))
                chart.Background = Brushes.Transparent;
            foreach (var chart in FindVisualChildren<PieChart>(this))
                chart.Background = Brushes.Transparent;

            SubscribeWindowShortcuts();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnsubscribeWindowShortcuts();
        }

        private Window? _hostWindow;

        /// <summary>
        /// 订阅宿主窗口的 PreviewKeyDown，实现 Esc 退出数据分析界面（窗口级处理，与焦点无关）
        /// </summary>
        private void SubscribeWindowShortcuts()
        {
            _hostWindow = Window.GetWindow(this);
            if (_hostWindow != null)
                _hostWindow.PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void UnsubscribeWindowShortcuts()
        {
            if (_hostWindow != null)
            {
                _hostWindow.PreviewKeyDown -= Window_PreviewKeyDown;
                _hostWindow = null;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Esc 退出数据分析界面
            if (e.Key == Key.Escape)
            {
                ExitRequested?.Invoke();
                e.Handled = true;
            }
        }

        private void SetTextBoxesBorderColor(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            foreach (var textBox in FindVisualChildren<System.Windows.Controls.TextBox>(this))
                textBox.BorderBrush = brush;
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

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            ExitRequested?.Invoke();
        }

        // ========== 起始日期下拉菜单 ==========
        private void StartDateBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeStartDateMenu != null)
            {
                _activeStartDateMenu.Close();
                _activeStartDateMenu = null;
                return;
            }

            var yearMonths = GetYearMonthGroups();
            if (yearMonths.Count == 0)
            {
                var win = Window.GetWindow(this);
                if (win != null) ToastService.Show("当前没有可用的年月分组", win);
                return;
            }

            var menu = new DropdownMenu();
            foreach (var ym in yearMonths)
            {
                var capturedYm = ym;
                menu.AddItem(ym, () => { _viewModel.CustomStartText = capturedYm; });
            }
            menu.Closed += () =>
            {
                _activeStartDateMenu = null;
                StartDateToggleButton.IsChecked = false;
            };
            _activeStartDateMenu = menu;
            StartDateToggleButton.IsChecked = true;
            menu.ShowBelow((FrameworkElement)sender);
            e.Handled = true;
        }

        // ========== 结束日期下拉菜单 ==========
        private void EndDateBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeEndDateMenu != null)
            {
                _activeEndDateMenu.Close();
                _activeEndDateMenu = null;
                return;
            }

            var allYearMonths = GetYearMonthGroups();
            if (allYearMonths.Count == 0)
            {
                var win = Window.GetWindow(this);
                if (win != null) ToastService.Show("当前没有可用的年月分组", win);
                return;
            }

            var (startYear, startMonth) = AnalysisViewModel.ParseYearMonthFromDisplay(_viewModel.CustomStartText);
            bool hasStart = startYear > 0 && startMonth > 0;

            var filteredYms = allYearMonths.Where(ym =>
            {
                if (!hasStart) return true;
                var (y, m) = AnalysisViewModel.ParseYearMonthFromDisplay(ym);
                if (y == 0) return false;
                return y > startYear || (y == startYear && m >= startMonth);
            }).ToList();

            if (filteredYms.Count == 0)
            {
                var win = Window.GetWindow(this);
                if (win != null) ToastService.Show("没有符合条件的年月分组", win);
                return;
            }

            var menu = new DropdownMenu();
            foreach (var ym in filteredYms)
            {
                var capturedYm = ym;
                menu.AddItem(ym, () => { _viewModel.CustomEndText = capturedYm; });
            }
            menu.Closed += () =>
            {
                _activeEndDateMenu = null;
                EndDateToggleButton.IsChecked = false;
            };
            _activeEndDateMenu = menu;
            EndDateToggleButton.IsChecked = true;
            menu.ShowBelow((FrameworkElement)sender);
            e.Handled = true;
        }

        // ========== 辅助方法 ==========

        private List<string> GetYearMonthGroups()
        {
            return _viewModel.YearMonthGroups
                .Select(g => g.Name)
                .Reverse()
                .ToList();
        }

        private void SummaryBorder_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            string? text = (SummaryBorder.Child as TextBlock)?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                System.Windows.Clipboard.SetText(text);
                var win = Window.GetWindow(this);
                if (win != null) ToastService.Show("统计分析摘要文本已复制到剪切板", win);
            }
            e.Handled = true;
        }
    }
}
