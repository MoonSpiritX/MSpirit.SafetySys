// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Controls
{
    /// <summary>
    /// 可复用的日期选择框：只读文本框 + 旋转箭头按钮 + 日历弹窗。
    /// 宿主通过 SelectedDate（TwoWay 默认）绑定业务字段，通过 DateSelected 事件接收选中/清空结果。
    /// </summary>
    public partial class DateFieldPicker : UserControl
    {
        public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(
            nameof(SelectedDate), typeof(DateTime?), typeof(DateFieldPicker),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public DateTime? SelectedDate
        {
            get => (DateTime?)GetValue(SelectedDateProperty);
            set => SetValue(SelectedDateProperty, value);
        }

        /// <summary>用户在日历中选中或清空日期时触发（清空时参数为 null）</summary>
        public event EventHandler<DateTime?>? DateSelected;

        /// <summary>打开弹窗时由宿主提供日历初始月份；为 null 时回退 SelectedDate ?? DateTime.Now</summary>
        public Func<DateTime>? InitialMonthProvider { get; set; }

        /// <summary>宿主可在此往右键菜单追加菜单项（如“统一输入”）</summary>
        public Action<DropdownMenu>? ContextMenuCustomizer { get; set; }

        private Window? _hostWindow;
        private ScrollViewer? _mainScrollViewer;
        private bool _isOpeningPopup;
        private DropdownMenu? _activeMenu;

        public DateFieldPicker()
        {
            InitializeComponent();
        }

        private void RootBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DatePopup.IsOpen)
                ClosePopup();
            else
                OpenPopup();
            e.Handled = true;
        }

        private void OpenPopup()
        {
            var initialMonth = InitialMonthProvider?.Invoke() ?? SelectedDate ?? DateTime.Now;
            DateCalendar?.ShowMonth(initialMonth);

            _isOpeningPopup = true;
            DatePopup.IsOpen = true;
            DateToggleButton.IsChecked = true;

            SubscribeHostEvents();
            DatePopup.Opened += OnPopupOpened;
        }

        private void OnPopupOpened(object? sender, EventArgs e)
        {
            DatePopup.Opened -= OnPopupOpened;
            _isOpeningPopup = false;

            DateCalendar.Dispatcher.BeginInvoke(new Action(() =>
            {
                DateCalendar.Focusable = true;
                DateCalendar.Focus();
                var firstButton = FindFirstFocusableChild(DateCalendar);
                if (firstButton != null)
                    FocusManager.SetFocusedElement(DateCalendar, firstButton);
            }));
        }

        private void ClosePopup()
        {
            if (!DatePopup.IsOpen) return;
            DatePopup.IsOpen = false;
            DateToggleButton.IsChecked = false;
            UnsubscribeHostEvents();
        }

        private void OnCalendarDateSelected(object? sender, DateTime? date)
        {
            SelectedDate = date;
            DateSelected?.Invoke(this, date);
            ClosePopup();
        }

        // ========== 右键菜单 ==========

        private void DateTextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            _activeMenu?.Close();
            _activeMenu = null;

            var menu = new DropdownMenu();
            menu.AddItem("清空日期", () =>
            {
                SelectedDate = null;
                DateSelected?.Invoke(this, null);
            });
            menu.AddSeparator();
            ContextMenuCustomizer?.Invoke(menu);

            menu.Closed += () => _activeMenu = null;
            _activeMenu = menu;
            menu.ShowAt(new Point(wpfPoint.X, wpfPoint.Y - 15));
            e.Handled = true;
        }

        // ========== 弹窗期间的全局事件 ==========

        private void SubscribeHostEvents()
        {
            _hostWindow = Window.GetWindow(this);
            if (_hostWindow != null)
            {
                _hostWindow.PreviewMouseDown += OnHostPreviewMouseDown;
                _hostWindow.Deactivated += OnHostDeactivated;
                _hostWindow.PreviewMouseWheel += OnHostPreviewMouseWheel;
            }

            _mainScrollViewer = FindAncestorScrollViewer(this);
            if (_mainScrollViewer != null)
                _mainScrollViewer.ScrollChanged += OnMainScrollChanged;
        }

        private void UnsubscribeHostEvents()
        {
            if (_hostWindow != null)
            {
                _hostWindow.PreviewMouseDown -= OnHostPreviewMouseDown;
                _hostWindow.Deactivated -= OnHostDeactivated;
                _hostWindow.PreviewMouseWheel -= OnHostPreviewMouseWheel;
                _hostWindow = null;
            }

            if (_mainScrollViewer != null)
            {
                _mainScrollViewer.ScrollChanged -= OnMainScrollChanged;
                _mainScrollViewer = null;
            }
        }

        private void OnHostPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isOpeningPopup) return;

            var hitElement = e.OriginalSource as DependencyObject;
            if (hitElement == null) return;

            bool clickedInsidePopup = IsVisualChildOf(DatePopup.Child, hitElement);
            bool clickedOnPicker = IsVisualChildOf(RootBorder, hitElement) ||
                                   IsVisualChildOf(DateTextBox, hitElement) ||
                                   IsVisualChildOf(DateToggleButton, hitElement);

            if (!clickedInsidePopup && !clickedOnPicker && DatePopup.IsOpen)
                ClosePopup();
        }

        private void OnHostDeactivated(object? sender, EventArgs e)
        {
            if (DatePopup.IsOpen)
                ClosePopup();
        }

        private void OnHostPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!DatePopup.IsOpen)
            {
                UnsubscribeHostEvents();
                return;
            }
            e.Handled = true;
        }

        private void OnMainScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 弹窗打开时禁止主内容滚动
            if (DatePopup.IsOpen)
                e.Handled = true;
        }

        // ========== 辅助方法 ==========

        private static FrameworkElement? FindFirstFocusableChild(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Focusable && fe.Visibility == Visibility.Visible)
                    return fe;
                var found = FindFirstFocusableChild(child);
                if (found != null) return found;
            }
            return null;
        }

        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? start)
        {
            var current = start;
            while (current != null)
            {
                if (current is ScrollViewer sv) return sv;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static bool IsVisualChildOf(DependencyObject? parent, DependencyObject? child)
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
    }
}
