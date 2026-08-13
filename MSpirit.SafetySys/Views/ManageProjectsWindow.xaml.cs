// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Views
{
    public partial class ManageProjectsWindow : Wpf.Ui.Controls.FluentWindow
    {
        public ManageProjectsWindow(ConfigManager configManager)
        {
            InitializeComponent();
            DataContext = new ManageProjectsViewModel(configManager, () => this);
            this.PreviewMouseDown += OnPreviewMouseDown;

            this.Activated += (s, e) => SetSearchBoxBorderColor("#0078D4");
            this.Deactivated += (s, e) => SetSearchBoxBorderColor("#17c3b2");

            // 搜索框加载完成后，连接清空按钮的事件
            SearchTextBox.Loaded += (s, e) =>
            {
                var clearButton = SearchTextBox.Template.FindName("ClearButton", SearchTextBox) as Button;
                if (clearButton != null)
                    clearButton.Click += (sender, args) => ClearSearch_Click(sender, args);
            };
        }

        private bool IsFocusableControl(DependencyObject element)
        {
            while (element != null)
            {
                if (element is TextBox || element is Button || element is DataGridCell || element is DataGridRow)
                    return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        private void SetSearchBoxBorderColor(string hexColor)
        {
            if (SearchTextBox == null) return;
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
                SearchTextBox.BorderBrush = brush;
            }
            catch (Exception ex) { Log.Warning(ex, "设置搜索框边框颜色失败: {Color}", hexColor); }
        }

        /// <summary>
        /// 行复选框点击：手动写回 IsSelected（DataGrid 模板列中 CheckBox 的 TwoWay 绑定写回不可靠，
        /// 用事件直接设置，经 ViewModel 回调刷新"已选 N 项"统计）。
        /// </summary>
        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ProjectItemViewModel row)
            {
                row.IsSelected = cb.IsChecked == true;
                e.Handled = true;
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ManageProjectsViewModel vm)
            {
                vm.ClearSearchCommand.Execute(null);
            }
        }

        /// <summary>记录表头复选框点击前的状态（已废弃：表头改为文本"选择"，全选由底部按钮承担）</summary>

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var hitElement = e.OriginalSource as DependencyObject;
            if (hitElement == null) return;

            if (e.ChangedButton == MouseButton.Left)
            {
                if (!IsFocusableControl(hitElement))
                {
                    FocusManager.SetFocusedElement(this, null);
                    Keyboard.ClearFocus();
                }
            }
        }

        private void ProjectsDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            e.Cancel = true;
        }

        private void ProjectsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ManageProjectsViewModel vm && vm.SelectedProject != null)
            {
                vm.SelectProjectCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
