// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Controls
{
    public partial class WindowTitleBar : UserControl
    {
        // 依赖属性：图标
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(WindowTitleBar), new PropertyMetadata(null));

        public ImageSource Icon
        {
            get => (ImageSource)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        // 依赖属性：标题
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(WindowTitleBar), new PropertyMetadata(""));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        // 是否显示最小化按钮
        public static readonly DependencyProperty ShowMinimizeButtonProperty =
            DependencyProperty.Register(nameof(ShowMinimizeButton), typeof(bool), typeof(WindowTitleBar), new PropertyMetadata(true));

        public bool ShowMinimizeButton
        {
            get => (bool)GetValue(ShowMinimizeButtonProperty);
            set => SetValue(ShowMinimizeButtonProperty, value);
        }

        // 是否显示最大化/还原按钮
        public static readonly DependencyProperty ShowMaximizeButtonProperty =
            DependencyProperty.Register(nameof(ShowMaximizeButton), typeof(bool), typeof(WindowTitleBar), new PropertyMetadata(true));

        public bool ShowMaximizeButton
        {
            get => (bool)GetValue(ShowMaximizeButtonProperty);
            set => SetValue(ShowMaximizeButtonProperty, value);
        }

        // 标题字体大小
        public static readonly DependencyProperty TitleFontSizeProperty =
            DependencyProperty.Register(nameof(TitleFontSize), typeof(double), typeof(WindowTitleBar), new PropertyMetadata(14.667));

        public double TitleFontSize
        {
            get => (double)GetValue(TitleFontSizeProperty);
            set => SetValue(TitleFontSizeProperty, value);
        }

        // 是否显示图标
        public static readonly DependencyProperty ShowIconProperty =
            DependencyProperty.Register(nameof(ShowIcon), typeof(bool), typeof(WindowTitleBar), new PropertyMetadata(true));

        public bool ShowIcon
        {
            get => (bool)GetValue(ShowIconProperty);
            set => SetValue(ShowIconProperty, value);
        }

        // 标题栏高度
        public static readonly DependencyProperty TitleBarHeightProperty =
            DependencyProperty.Register(nameof(TitleBarHeight), typeof(double), typeof(WindowTitleBar), new PropertyMetadata(35.0));

        public double TitleBarHeight
        {
            get => (double)GetValue(TitleBarHeightProperty);
            set => SetValue(TitleBarHeightProperty, value);
        }

        // 当前窗口实例（用于操作窗口状态）
        private Window? _parentWindow;

        private void OnParentWindowStateChanged(object? sender, EventArgs e) => UpdateMaximizeButtonIcon();

        public WindowTitleBar()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _parentWindow = Window.GetWindow(this);
                if (_parentWindow != null)
                {
                    _parentWindow.StateChanged += OnParentWindowStateChanged;
                    UpdateMaximizeButtonIcon();
                }
            };
            Unloaded += (s, e) =>
            {
                if (_parentWindow != null)
                    _parentWindow.StateChanged -= OnParentWindowStateChanged;
            };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击标题栏最大化/还原
                if (ShowMaximizeButton && _parentWindow != null)
                {
                    _parentWindow.WindowState = _parentWindow.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                }
            }
            else
            {
                _parentWindow?.DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            if (_parentWindow != null)
                _parentWindow.WindowState = WindowState.Minimized;
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            if (_parentWindow != null)
                _parentWindow.WindowState = _parentWindow.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _parentWindow?.Close();
        }

        private void UpdateMaximizeButtonIcon()
        {
            if (_parentWindow == null) return;
            MaximizeButton.Content = _parentWindow.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }
    }
}