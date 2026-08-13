// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MS_SafetyManagementSystem.Controls;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class FunctionToolbarControl : UserControl
    {
        private Point _dragStartPoint;
        private bool _isDragging = false;

        public FunctionToolbarControl()
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

            var element = sender as FrameworkElement;
            if (element != null)
            {
                var parentWindow = Window.GetWindow(this);
                if (parentWindow != null)
                {
                    _dragStartPoint = e.GetPosition(parentWindow);
                    _isDragging = true;
                    e.Handled = true;
                }
            }
        }

        private void FunctionArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                return;
            }

            var parentWindow = Window.GetWindow(this);
            if (parentWindow == null) return;

            var currentPoint = e.GetPosition(parentWindow);
            var offset = currentPoint - _dragStartPoint;
            if (System.Math.Abs(offset.X) > SystemParameters.MinimumHorizontalDragDistance ||
                System.Math.Abs(offset.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                parentWindow.DragMove();
                _isDragging = false;
            }
        }

        private bool IsClickOnInteractiveControl(DependencyObject? source)
        {
            if (source == null) return false;

            var current = source;
            while (current != null)
            {
                if (current is RecordCardView)
                    return true;
                if (current is TreeViewItem)
                    return true;
                if (current is System.Windows.Controls.Primitives.ScrollBar)
                    return true;
                if (current is TextBox || current is System.Windows.Controls.Button ||
                    current is System.Windows.Controls.ComboBox || current is System.Windows.Controls.CheckBox ||
                    current is System.Windows.Controls.ListBox || current is System.Windows.Controls.DataGrid ||
                    current is DatePicker || current is ToggleButton ||
                    current is System.Windows.Controls.Image || current is System.Windows.Controls.ListView ||
                    current is System.Windows.Controls.RichTextBox)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void OnDataExport(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowDataExport();
        }

        private void OnDataAnalysis(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.ShowDataAnalysis();
        }
    }
}
