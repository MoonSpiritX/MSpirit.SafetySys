// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// MainWindow 分部类：分组树右键菜单构建与 Popup 关闭管理（全局点击/失焦关闭）。
    /// 2026-08-13 从 MainWindow.xaml.cs 拆出，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow
    {
        private Popup? _activePopup;
        public Popup? TrayPopup { get; set; }

        private void GroupTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hitElement = e.OriginalSource as DependencyObject;
            if (hitElement == null) return;
            var treeViewItem = FindParent<TreeViewItem>(hitElement);
            if (treeViewItem == null) return;

            var node = treeViewItem.DataContext as GroupNode;
            if (node == null) return;

            var treeViewItemToSelect = FindTreeViewItem(_currentHazardPanel?.TreeView ?? _currentPersonnelPanel?.TreeView, node);
            if (treeViewItemToSelect != null)
            {
                treeViewItemToSelect.IsSelected = true;
            }
            _viewModel.GroupTree.SelectedNode = node;

            if (_activePopup != null)
            {
                _activePopup.IsOpen = false;
                _activePopup = null;
            }

            // 构建菜单项
            var menuPanel = new StackPanel();
            foreach (var menuItem in node.ContextMenuItems)
            {
                var btn = new WpfButton
                {
                    Content = menuItem.Header,
                    Style = (Style)FindResource("RecordCardContextMenuItemStyle"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    Height = 30,
                    Margin = new Thickness(0, 2, 0, 2),
                    Command = menuItem.Command,
                    CommandParameter = menuItem.CommandParameter,
                    Visibility = menuItem.Visibility
                };
                // 菜单项点击后关闭弹出菜单
                btn.Click += (s, e) => CloseActivePopup();
                menuPanel.Children.Add(btn);
            }

            var contentControl = new ContentControl
            {
                Style = (Style)FindResource("RecordCardContextMenuContainerStyle"),
                Content = menuPanel
            };

            contentControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var physicalMouse = new System.Windows.Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var transform = source.CompositionTarget.TransformFromDevice;
            var wpfMouse = transform.Transform(physicalMouse);

            var screen = Screen.FromPoint(new System.Drawing.Point((int)physicalMouse.X, (int)physicalMouse.Y));
            var physicalWorkArea = new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
            var wpfWorkArea = new Rect(
                transform.Transform(new Point(physicalWorkArea.Left, physicalWorkArea.Top)),
                transform.Transform(new Point(physicalWorkArea.Right, physicalWorkArea.Bottom)));

            double left = wpfMouse.X;
            double top = wpfMouse.Y - 10;

            if (left + contentControl.DesiredSize.Width > wpfWorkArea.Right)
                left = wpfWorkArea.Right - contentControl.DesiredSize.Width;
            if (left < wpfWorkArea.Left)
                left = wpfWorkArea.Left;
            if (top + contentControl.DesiredSize.Height > wpfWorkArea.Bottom)
                top = wpfWorkArea.Bottom - contentControl.DesiredSize.Height;
            if (top < wpfWorkArea.Top)
                top = wpfWorkArea.Top;

            _activePopup = new Popup
            {
                Child = contentControl,
                Placement = PlacementMode.AbsolutePoint,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.Slide,
                HorizontalOffset = left,
                VerticalOffset = top
            };
            _activePopup.IsOpen = true;

            this.PreviewMouseDown += OnGlobalPreviewMouseDownForPopup;
            this.Deactivated += OnWindowDeactivatedForPopup;

            e.Handled = true;
        }

        private bool IsClickInsideActivePopup(DependencyObject hit)
        {
            if (_activePopup == null || _activePopup.Child == null) return false;
            return IsVisualChildOf(_activePopup.Child, hit);
        }

        private bool IsVisualChildOf(DependencyObject parent, DependencyObject child)
        {
            if (parent == null || child == null) return false;
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void CloseActivePopup()
        {
            if (_activePopup != null && _activePopup.IsOpen)
            {
                _activePopup.IsOpen = false;
                _activePopup = null;
                this.PreviewMouseDown -= OnGlobalPreviewMouseDownForPopup;
                this.Deactivated -= OnWindowDeactivatedForPopup;
            }

            if (TrayPopup != null && TrayPopup.IsOpen)
            {
                TrayPopup.IsOpen = false;
                TrayPopup = null;
            }
        }

        public void CloseTrayPopup()
        {
            if (TrayPopup != null && TrayPopup.IsOpen)
            {
                TrayPopup.IsOpen = false;
                TrayPopup = null;
            }
        }

        private void OnGlobalPreviewMouseDownForPopup(object sender, MouseButtonEventArgs e)
        {
            var hit = e.OriginalSource as DependencyObject;
            if (hit == null) return;

            bool shouldClose = true;

            if (_activePopup != null && _activePopup.IsOpen)
            {
                if (IsClickInsideActivePopup(hit))
                    shouldClose = false;
                else
                    CloseActivePopup();
            }

            if (TrayPopup != null && TrayPopup.IsOpen && shouldClose)
            {
                if (IsVisualChildOf(TrayPopup.Child, hit))
                    shouldClose = false;
                else
                    TrayPopup.IsOpen = false;
            }

            if (shouldClose)
                CloseActivePopup();
        }

        private void OnWindowDeactivatedForPopup(object? sender, EventArgs e)
        {
            CloseActivePopup();
        }

        /// <summary>给菜单项附加悬停效果（Windows 经典菜单无默认悬停色）</summary>
        private void AttachHoverEffect(MenuItem item)
        {
            item.MouseEnter += (s, e) => item.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5F3FF"));
            item.MouseLeave += (s, e) => item.Background = Brushes.Transparent;
            foreach (var child in item.Items)
                if (child is MenuItem subItem)
                    AttachHoverEffect(subItem);
        }

        private T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t) return t;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private TreeViewItem? FindTreeViewItem(ItemsControl container, object item)
        {
            if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem tvi)
                return tvi;

            foreach (var childItem in container.Items)
            {
                var childContainer = container.ItemContainerGenerator.ContainerFromItem(childItem) as ItemsControl;
                if (childContainer != null)
                {
                    var result = FindTreeViewItem(childContainer, item);
                    if (result != null)
                        return result;
                }
            }
            return null;
        }
    }
}
