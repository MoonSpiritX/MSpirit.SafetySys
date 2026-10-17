// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelToolbarControl : UserControl
    {
        private Point _dragStartPoint;
        private bool _isDragging;

        public PersonnelToolbarControl()
        {
            InitializeComponent();
        }

        private void FunctionArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsClickOnInteractiveControl(e.OriginalSource as DependencyObject))
            {
                _isDragging = false;
                return;
            }

            _dragStartPoint = e.GetPosition(Window.GetWindow(this));
            _isDragging = true;
            e.Handled = true;
        }

        private void FunctionArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(Window.GetWindow(this));
            if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = false;
                var win = Window.GetWindow(this);
                if (win != null) win.DragMove();
            }
        }

        private void FunctionArea_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 右键不处理
        }

        private bool IsClickOnInteractiveControl(DependencyObject? source)
        {
            if (source == null) return false;
            var current = source;
            while (current != null)
            {
                if (current is Button || current is TextBox || current is CheckBox)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        /// <summary>
        /// 打开人员数据导出/导入工具（PersonnelExportControl）
        /// </summary>
        private void OnDataExport(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowPersonnelDataExport();
        }

        /// <summary>
        /// 打开人员统计数据分析（显示统计数据表）
        /// </summary>
        private void OnDataAnalysis(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowPersonnelAnalysis();
        }
    }
}
