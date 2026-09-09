// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Helpers
{
    /// <summary>
    /// 窗口内容区拖拽平移辅助（程序特色：除可交互控件外，内容区任意空白处按住左键可拖动窗口）。
    /// 用法：窗口元素（FluentWindow）上加附加属性 ——
    /// <c>helpers:WindowDragHelper.EnableDrag="True"</c>，无需任何 code-behind 代码。
    ///
    /// 实现要点：
    /// - 用 Preview（隧道）事件：ListBox/ScrollViewer 等控件内部会消费 MouseLeftButtonDown 冒泡事件，
    ///   冒泡到窗口前就被拦截；隧道事件在窗口级最先触发，保证所有空白区都能命中。
    /// - 交互控件（按钮/输入框/列表项/单元格/滚动条等）由 <see cref="IsInteractiveElement"/> 白名单排除，
    ///   不触发拖拽，正常交互不受影响。
    /// - 标题栏区域由 WindowTitleBar 自行处理拖拽，此处按 <see cref="TitleBarHeight"/> 排除。
    /// - 内容区容器（根 Grid）建议设 Background="Transparent" 参与命中测试，否则纯空白区无法命中。
    /// </summary>
    public static class WindowDragHelper
    {
        /// <summary>内容区标题栏高度（各窗口 Row 0 均为 35px，WindowTitleBar 自行处理拖拽）</summary>
        private const double TitleBarHeight = 35;

        /// <summary>启用窗口内容区拖拽的附加属性（挂在 Window 上）</summary>
        public static readonly DependencyProperty EnableDragProperty =
            DependencyProperty.RegisterAttached(
                "EnableDrag", typeof(bool), typeof(WindowDragHelper),
                new PropertyMetadata(false, OnEnableDragChanged));

        public static bool GetEnableDrag(DependencyObject obj) => (bool)obj.GetValue(EnableDragProperty);
        public static void SetEnableDrag(DependencyObject obj, bool value) => obj.SetValue(EnableDragProperty, value);

        private static void OnEnableDragChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window) return;
            if ((bool)e.NewValue)
                window.PreviewMouseLeftButtonDown += OnDragMove;
            else
                window.PreviewMouseLeftButtonDown -= OnDragMove;
        }

        /// <summary>拖拽事件处理器：仅非交互控件区域触发窗口拖动</summary>
        private static void OnDragMove(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Window window) return;
            // 排除标题栏区域（由 WindowTitleBar 自己处理拖拽）
            if (e.GetPosition(window).Y < TitleBarHeight) return;
            // 交互控件区域不触发拖拽（按钮/输入框/列表项/单元格/滚动条等），避免干扰正常交互
            if (IsInteractiveElement(e.OriginalSource as DependencyObject)) return;

            try
            {
                window.DragMove();
                e.Handled = true;
            }
            catch (InvalidOperationException)
            {
                // DragMove 可能在极端时序下重复调用，静默忽略
            }
        }

        /// <summary>命中元素是否属于交互控件（沿视觉树向上检查）</summary>
        public static bool IsInteractiveElement(DependencyObject? source)
        {
            for (var n = source; n != null; n = System.Windows.Media.VisualTreeHelper.GetParent(n))
            {
                if (n is System.Windows.Controls.Primitives.ButtonBase
                    or System.Windows.Controls.Primitives.TextBoxBase
                    or System.Windows.Controls.Primitives.ScrollBar
                    or System.Windows.Controls.Primitives.DataGridColumnHeader
                    or System.Windows.Controls.ComboBox
                    or System.Windows.Controls.ListBoxItem
                    or System.Windows.Controls.DataGridCell
                    or System.Windows.Controls.TreeViewItem // 裁剪列表项需可选中（2026-08-15）
                    or MS_SafetyManagementSystem.Tools.CropOverlay) // 裁剪交互区整体豁免拖拽（2026-08-15）
                    return true;
            }
            return false;
        }
    }
}
