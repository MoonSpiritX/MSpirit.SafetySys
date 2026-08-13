// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using Serilog;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Views
{
    public partial class ManageLinksWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ManageLinksViewModel _viewModel;

        public ManageLinksWindow(FavoriteLinksService linksService)
        {
            InitializeComponent();
            _viewModel = new ManageLinksViewModel(linksService, () => this);
            DataContext = _viewModel;

            this.Activated += (s, e) => SetSearchBoxBorderColor("#0078D4");
            this.Deactivated += (s, e) => SetSearchBoxBorderColor("#17c3b2");

            this.PreviewMouseDown += OnPreviewMouseDown;
            this.Deactivated += OnWindowDeactivated;

            // 搜索框加载完成后，连接清空按钮的事件
            SearchTextBox.Loaded += (s, e) =>
            {
                var clearButton = SearchTextBox.Template.FindName("ClearButton", SearchTextBox) as Button;
                if (clearButton != null)
                    clearButton.Click += (sender, args) => ClearSearch_Click(sender, args);
            };
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

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Models.LinkCategory cat)
            {
                _viewModel.SelectedCategory = cat;
            }
        }

        private void LinksDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedLink != null)
            {
                _viewModel.OpenLinkCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void LinksDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            e.Cancel = true;
        }

        #region 编辑退出逻辑
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

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var hitElement = e.OriginalSource as DependencyObject;
            if (hitElement == null) return;

            if (IsDataGridEditing())
            {
                if (ShouldExitEditing(hitElement))
                {
                    EndDataGridEditing();
                }
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                if (!IsFocusableControl(hitElement))
                {
                    FocusManager.SetFocusedElement(this, null);
                    Keyboard.ClearFocus();
                }
            }
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            EndDataGridEditing();
        }

        private bool IsDataGridEditing()
        {
            if (LinksDataGrid == null) return false;
            return LinksDataGrid.CurrentCell.IsValid && LinksDataGrid.CurrentCell.Column != null;
        }

        private bool ShouldExitEditing(DependencyObject hitElement)
        {
            var dataGrid = FindVisualParent<DataGrid>(hitElement);
            if (dataGrid != LinksDataGrid) return true;

            var currentCell = LinksDataGrid.CurrentCell;
            if (!currentCell.IsValid || currentCell.Column == null) return true;

            var cellContainer = GetCellContainer(currentCell);
            if (cellContainer == null) return true;

            return !IsVisualChildOf(cellContainer, hitElement);
        }

        private DataGridCell? GetCellContainer(DataGridCellInfo cellInfo)
        {
            if (LinksDataGrid == null || cellInfo.Column == null || cellInfo.Item == null)
                return null;

            var row = LinksDataGrid.ItemContainerGenerator.ContainerFromItem(cellInfo.Item) as DataGridRow;
            if (row == null) return null;

            var presenter = FindVisualChild<DataGridCellsPresenter>(row);
            if (presenter == null) return null;

            int columnIndex = cellInfo.Column.DisplayIndex;
            return presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
        }

        private bool IsVisualChildOf(DependencyObject ancestor, DependencyObject child)
        {
            while (child != null)
            {
                if (child == ancestor) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t) return t;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void EndDataGridEditing()
        {
            if (LinksDataGrid == null) return;
            LinksDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
            LinksDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        }
        #endregion
    }
}
