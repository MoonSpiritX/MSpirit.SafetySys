// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelGroupTreePanel : UserControl
    {
        public TreeView TreeView => GroupTreeView;

        private double _lastScrollOffset = 0;
        private Point _dragStartPoint;
        private bool _isDragging;

        // 惯性滚动
        private DispatcherTimer? _inertiaTimer;
        private double _inertiaVelocity;

        public event RoutedPropertyChangedEventHandler<object>? SelectedNodeChanged;
        public event MouseButtonEventHandler? NodeRightButtonDown;

        public PersonnelGroupTreePanel()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 跟踪外层 RecordsScrollViewer 的滚动位置（实际控制滚动的容器）
            RecordsScrollViewer.ScrollChanged += (s, args) =>
            {
                _lastScrollOffset = RecordsScrollViewer.VerticalOffset;
            };
            // 挂载惯性滚动
            RecordsScrollViewer.PreviewMouseWheel += OnScrollViewerPreviewMouseWheel;
        }

        /// <summary>
        /// Preview 阶段拦截滚轮，带惯性效果（与 RecordsView 一致）
        /// </summary>
        private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;

            const double baseStep = 15.0;
            double delta = -Math.Sign(e.Delta) * baseStep;
            RecordsScrollViewer.ScrollToVerticalOffset(RecordsScrollViewer.VerticalOffset + delta);
            _inertiaVelocity = delta;

            if (_inertiaTimer == null)
            {
                _inertiaTimer = new DispatcherTimer();
                _inertiaTimer.Interval = TimeSpan.FromMilliseconds(16);
                _inertiaTimer.Tick += OnInertiaTick;
            }
            _inertiaTimer.Start();
        }

        private void OnInertiaTick(object? sender, EventArgs e)
        {
            _inertiaVelocity *= 0.9;
            if (Math.Abs(_inertiaVelocity) < 0.5)
            {
                _inertiaTimer?.Stop();
                return;
            }
            RecordsScrollViewer.ScrollToVerticalOffset(RecordsScrollViewer.VerticalOffset + _inertiaVelocity);
        }

        /// <summary>
        /// 获取当前滚动偏移（由 ScrollChanged 实时更新）
        /// </summary>
        public double GetScrollOffset() => _lastScrollOffset;

        /// <summary>
        /// 设置滚动偏移，在 Layout 完成后执行（含延迟重试）
        /// </summary>
        public void SetScrollOffset(double offset)
        {
            if (offset <= 0) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (RecordsScrollViewer.ScrollableHeight > 0)
                {
                    var targetOffset = Math.Min(offset, RecordsScrollViewer.ScrollableHeight);
                    RecordsScrollViewer.ScrollToVerticalOffset(targetOffset);
                    _lastScrollOffset = targetOffset;
                }
                else
                {
                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        SetScrollOffset(offset);
                    };
                    timer.Start();
                }
            }), DispatcherPriority.Loaded);
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SelectedNodeChanged?.Invoke(sender, e);
        }

        private void TreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            NodeRightButtonDown?.Invoke(sender, e);
        }

        private void Panel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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

        private void Panel_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                return;
            }

            var pos = e.GetPosition(Window.GetWindow(this));
            if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = false;
                var win = Window.GetWindow(this);
                win?.DragMove();
            }
        }

        private bool IsClickOnInteractiveControl(DependencyObject? source)
        {
            if (source == null) return false;
            var current = source;
            while (current != null)
            {
                if (current is TreeViewItem || current is ScrollBar)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
