// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.Helpers
{
    /// <summary>
    /// 为 DataGrid 提供 Shift+滚轮 水平滚动 + 惯性垂直滚动的附加行为。
    /// 用法：
    ///   helpers:DataGridScrollBehavior.EnableShiftHorizontalScroll="True"
    ///   helpers:DataGridScrollBehavior.EnableInertialScroll="True"
    /// </summary>
    public static class DataGridScrollBehavior
    {
        // ========== Shift+滚轮水平滚动 ==========

        /// <summary>
        /// 是否启用 Shift+滚轮水平滚动
        /// </summary>
        public static readonly DependencyProperty EnableShiftHorizontalScrollProperty =
            DependencyProperty.RegisterAttached(
                "EnableShiftHorizontalScroll",
                typeof(bool),
                typeof(DataGridScrollBehavior),
                new PropertyMetadata(false, OnEnableShiftChanged));

        public static bool GetEnableShiftHorizontalScroll(DependencyObject obj) =>
            (bool)obj.GetValue(EnableShiftHorizontalScrollProperty);

        public static void SetEnableShiftHorizontalScroll(DependencyObject obj, bool value) =>
            obj.SetValue(EnableShiftHorizontalScrollProperty, value);

        /// <summary>
        /// 每次滚动的偏移像素量（默认 48）
        /// </summary>
        public static readonly DependencyProperty ScrollStepProperty =
            DependencyProperty.RegisterAttached(
                "ScrollStep",
                typeof(double),
                typeof(DataGridScrollBehavior),
                new PropertyMetadata(48.0));

        public static double GetScrollStep(DependencyObject obj) =>
            (double)obj.GetValue(ScrollStepProperty);

        public static void SetScrollStep(DependencyObject obj, double value) =>
            obj.SetValue(ScrollStepProperty, value);

        private static void OnEnableShiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            if ((bool)e.NewValue)
                grid.PreviewMouseWheel += OnShiftHorizontalScroll;
            else
                grid.PreviewMouseWheel -= OnShiftHorizontalScroll;
        }

        private static void OnShiftHorizontalScroll(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Shift)
                return;

            if (sender is not DataGrid grid) return;

            // 如果已启用惯性滚动，由 OnInertialScroll 接管（带惯性）
            if (_inertialStates.ContainsKey(grid))
                return;

            var scrollViewer = FindScrollViewer(grid);
            if (scrollViewer == null) return;

            double step = GetScrollStep(grid);
            double offset = scrollViewer.HorizontalOffset - Math.Sign(e.Delta) * step;
            scrollViewer.ScrollToHorizontalOffset(Math.Max(0, offset));

            e.Handled = true;
        }

        // ========== 惯性垂直滚动 ==========

        private static readonly Dictionary<DataGrid, InertialScrollState> _inertialStates = new();

        /// <summary>
        /// 是否启用惯性滚动（像素级 + 减速效果）
        /// </summary>
        public static readonly DependencyProperty EnableInertialScrollProperty =
            DependencyProperty.RegisterAttached(
                "EnableInertialScroll",
                typeof(bool),
                typeof(DataGridScrollBehavior),
                new PropertyMetadata(false, OnEnableInertialChanged));

        public static bool GetEnableInertialScroll(DependencyObject obj) =>
            (bool)obj.GetValue(EnableInertialScrollProperty);

        public static void SetEnableInertialScroll(DependencyObject obj, bool value) =>
            obj.SetValue(EnableInertialScrollProperty, value);

        private static void OnEnableInertialChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DataGrid grid) return;

            if ((bool)e.NewValue)
            {
                grid.PreviewMouseWheel += OnInertialScroll;
                grid.Unloaded += OnGridUnloaded;
                _inertialStates[grid] = new InertialScrollState();
            }
            else
            {
                grid.PreviewMouseWheel -= OnInertialScroll;
                grid.Unloaded -= OnGridUnloaded;
                if (_inertialStates.TryGetValue(grid, out var state))
                {
                    state.Timer?.Stop();
                    _inertialStates.Remove(grid);
                }
            }
        }

        private static void OnGridUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid grid && _inertialStates.TryGetValue(grid, out var state))
            {
                state.Timer?.Stop();
                _inertialStates.Remove(grid);
            }
        }

        private static void OnInertialScroll(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;

            if (sender is not DataGrid grid) return;
            if (!_inertialStates.TryGetValue(grid, out var state)) return;

            var scrollViewer = FindScrollViewer(grid);
            if (scrollViewer == null) return;

            e.Handled = true;

            const double baseStep = 15.0;
            double delta = -Math.Sign(e.Delta) * baseStep;

            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Shift+滚轮 → 水平惯性滚动
                scrollViewer.ScrollToHorizontalOffset(Math.Max(0, scrollViewer.HorizontalOffset + delta));
                state.HVelocity = delta;
                state.Velocity = 0; // 重置垂直惯性
            }
            else
            {
                // 普通滚轮 → 垂直惯性滚动
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + delta);
                state.Velocity = delta;
                state.HVelocity = 0; // 重置水平惯性
            }

            // 启动惯性 Timer
            if (state.Timer == null)
            {
                state.Timer = new DispatcherTimer();
                state.Timer.Interval = TimeSpan.FromMilliseconds(16);
                state.Timer.Tick += (_, _) => OnInertialTick(grid, state);
            }
            state.Timer.Start();
        }

        private static void OnInertialTick(DataGrid grid, InertialScrollState state)
        {
            var scrollViewer = FindScrollViewer(grid);
            if (scrollViewer == null)
            {
                state.Timer?.Stop();
                return;
            }

            bool hasVelocity = Math.Abs(state.Velocity) >= 0.5;
            bool hasHVelocity = Math.Abs(state.HVelocity) >= 0.5;

            if (hasVelocity)
            {
                state.Velocity *= 0.9;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + state.Velocity);
            }

            if (hasHVelocity)
            {
                state.HVelocity *= 0.9;
                scrollViewer.ScrollToHorizontalOffset(Math.Max(0, scrollViewer.HorizontalOffset + state.HVelocity));
            }

            if (!hasVelocity && !hasHVelocity)
            {
                state.Timer?.Stop();
            }
        }

        private class InertialScrollState
        {
            public DispatcherTimer? Timer;
            public double Velocity;       // 垂直方向惯性速度
            public double HVelocity;      // 水平方向惯性速度
        }

        // ========== 公共工具方法 ==========

        /// <summary>
        /// 在 DataGrid 的可视化树中查找 ScrollViewer
        /// </summary>
        public static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv)
                    return sv;

                var found = FindScrollViewer(child);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
