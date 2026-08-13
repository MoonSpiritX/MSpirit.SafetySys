// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Hazard;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DataGridCell = System.Windows.Controls.DataGridCell;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListView = System.Windows.Controls.ListView;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// MainWindow 分部类：内容区空白处拖拽平移窗口 + 菜单栏空白处拖动（交互控件白名单豁免）。
    /// 2026-08-13 从 MainWindow.xaml.cs 拆出，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow
    {
        // ========== 空白区拖拽窗口 ==========
        private Point _activeViewDragStartPoint;
        private bool _isActiveViewDragging = false;

        private void BasePanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryStartDrag(sender, e);
        }

        private void BasePanel_PreviewMouseMove(object sender, InputMouseEventArgs e)
        {
            TryEndDrag(e);
        }

        private void OverlayPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TryStartDrag(sender, e);
        }

        private void OverlayPanel_PreviewMouseMove(object sender, InputMouseEventArgs e)
        {
            TryEndDrag(e);
        }

        private void TryStartDrag(object sender, MouseButtonEventArgs e)
        {
            if (IsGeneralInteractiveControl(e.OriginalSource as DependencyObject))
            {
                _isActiveViewDragging = false;
                return;
            }

            var element = sender as FrameworkElement;
            if (element != null)
            {
                _activeViewDragStartPoint = e.GetPosition(this);
                _isActiveViewDragging = true;
                e.Handled = true;
            }
        }

        private void TryEndDrag(InputMouseEventArgs e)
        {
            if (!_isActiveViewDragging) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isActiveViewDragging = false;
                return;
            }

            var offset = e.GetPosition(this) - _activeViewDragStartPoint;
            if (Math.Abs(offset.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(offset.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                this.DragMove();
                _isActiveViewDragging = false;
            }
        }

        /// <summary>
        /// 判断点击的元素是否属于需要排除交互的控件（不触发窗口拖动）
        /// </summary>
        private bool IsGeneralInteractiveControl(DependencyObject? source)
        {
            if (source == null) return false;

            // 将非 Visual 对象（如 Run）沿逻辑树向上转换为 Visual 祖先
            var current = source;
            while (current != null && !(current is Visual))
                current = LogicalTreeHelper.GetParent(current);
            if (current == null) return false;
            while (current != null)
            {
                if (current is ScrollBar)
                    return true;
                if (current is DataGridRow || current is DataGridCell)
                    return true;
                if (current is WpfTextBox || current is WpfButton || current is WpfCheckBox ||
                    current is WpfComboBox || current is DatePicker || current is ToggleButton ||
                    current is System.Windows.Controls.Image || current is WpfListBox || current is WpfListView ||
                    current is WpfRichTextBox || current is TreeViewItem ||
                    current is RecordCardView || current is GridSplitter)
                    return true;
                // 下拉选择器 / 日期选择器整体视为交互区域（含点击箭头时）
                if (current is DropdownSelector || current is DateFieldPicker)
                    return true;
                // 下拉触发器：Border 直接子级为 Grid 且含旋转箭头控件时，
                // 文本框与箭头区域应整体可点击（避免被窗口拖拽逻辑拦截）
                if (current is Border border &&
                    border.Child is Grid grid &&
                    grid.Children.OfType<ArrowToggleButton>().Any())
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        // ========== 菜单栏空白处拖动 ==========
        private Point _menuBarDragStartPoint;
        private bool _isMenuBarDragging = false;

        private void MenuBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 检查是否点击在菜单项或其子元素上
            if (IsClickOnMenuItem(e.OriginalSource as DependencyObject))
            {
                _isMenuBarDragging = false;
                return;
            }

            var element = sender as FrameworkElement;
            if (element != null)
            {
                _menuBarDragStartPoint = e.GetPosition(this);
                _isMenuBarDragging = true;
                e.Handled = true;
            }
        }

        private void MenuBar_MouseMove(object sender, InputMouseEventArgs e)
        {
            if (!_isMenuBarDragging) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _isMenuBarDragging = false;
                return;
            }

            var currentPoint = e.GetPosition(this);
            var offset = currentPoint - _menuBarDragStartPoint;
            if (Math.Abs(offset.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(offset.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                this.DragMove();
                _isMenuBarDragging = false;
            }
        }

        /// <summary>
        /// 判断点击的元素是否属于菜单项（或其子元素）
        /// </summary>
        private bool IsClickOnMenuItem(DependencyObject? source)
        {
            if (source == null) return false;

            var current = source;
            while (current != null)
            {
                if (current is MenuItem)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
