// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MS_SafetyManagementSystem;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>
    /// 托盘右键菜单控件：一级菜单（显示主窗口 / 分组菜单 / 退出程序）+ 悬停展开的二级菜单。
    /// 分组菜单由 <see cref="Groups"/> 驱动（办公工具、信息官网、外部AI、快捷网址），
    /// 悬停分组项时在右侧弹出对应二级菜单（Popup），点击二级项执行动作并关闭托盘菜单。
    /// </summary>
    public partial class TrayMenuControl : UserControl
    {
        public ICommand? ShowWindowCommand { get; set; }
        public ICommand? ExitCommand { get; set; }
        public Action? RequestClose { get; set; }

        private IReadOnlyList<TrayMenuGroup> _groups = Array.Empty<TrayMenuGroup>();

        /// <summary>托盘菜单分组（一级菜单的二级内容）；设置后重建一级分组按钮</summary>
        public IReadOnlyList<TrayMenuGroup> Groups
        {
            get => _groups;
            set
            {
                _groups = value ?? Array.Empty<TrayMenuGroup>();
                RebuildGroupItems();
            }
        }

        /// <summary>当前打开的二级菜单 Popup（供托盘服务鼠标钩子做"点击外部关闭"的区域判断）</summary>
        public Popup? CurrentSubMenuPopup => SubMenuPopupElement;

        private TrayMenuGroup? _currentGroup;

        public TrayMenuControl()
        {
            InitializeComponent();
            DataContext = this;
            SubMenuPopupElement.Closed += (s, e) => _currentGroup = null;
        }

        // ═══ 一级分组菜单构建 ═══

        private void RebuildGroupItems()
        {
            GroupItemsPanel.Children.Clear();
            bool any = false;
            foreach (var group in _groups)
            {
                any = true;
                var btn = new Button { Content = BuildGroupHeader(group.Header) };
                btn.MouseEnter += (s, e) => ShowSubMenu(group, (Button)s!);
                btn.MouseLeave += (s, e) => ScheduleCloseSubMenu();
                GroupItemsPanel.Children.Add(btn);
            }
            // 无分组时隐藏分组与退出之间的分隔线，避免空行
            GroupsSeparator.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>分组按钮内容：文本 + 右侧展开箭头（Path 绘制，不依赖字体字符）</summary>
        private static FrameworkElement BuildGroupHeader(string header)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(new TextBlock
            {
                Text = header,
                VerticalAlignment = VerticalAlignment.Center
            });
            panel.Children.Add(new Path
            {
                Data = Geometry.Parse("M0,1 L5,4 L0,7"),
                Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                StrokeThickness = 1.2,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        // ═══ 二级菜单展开/关闭 ═══

        private void ShowSubMenu(TrayMenuGroup group, FrameworkElement anchor)
        {
            if (_currentGroup == group && SubMenuPopupElement.IsOpen) return;
            _currentGroup = group;
            RebuildSubMenuItems(group);
            SubMenuPopupElement.PlacementTarget = anchor;
            SubMenuPopupElement.IsOpen = true;
        }

        /// <summary>悬停移出一级项：延迟检查鼠标是否已进入二级菜单，不在则关闭（避免移向二级菜单时误关）</summary>
        private void ScheduleCloseSubMenu()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SubMenuPopupElement.IsOpen && !IsMouseOverElement(SubMenuPopupElement.Child as UIElement))
                    CloseSubMenu();
            }), DispatcherPriority.Background);
        }

        private void CloseSubMenu()
        {
            SubMenuPopupElement.IsOpen = false;
            _currentGroup = null;
        }

        private static bool IsMouseOverElement(UIElement? element)
        {
            if (element == null) return false;
            var pos = Mouse.GetPosition(element);
            return pos.X >= 0 && pos.Y >= 0 &&
                   pos.X <= element.RenderSize.Width && pos.Y <= element.RenderSize.Height;
        }

        /// <summary>重建二级菜单内容：分组无项时显示禁用占位</summary>
        private void RebuildSubMenuItems(TrayMenuGroup group)
        {
            SubMenuItemsPanel.Children.Clear();
            if (group.Items == null || group.Items.Count == 0)
            {
                SubMenuItemsPanel.Children.Add(new Button { Content = "(暂无内容)", IsEnabled = false });
                return;
            }
            foreach (var item in group.Items)
            {
                var btn = new Button { Content = item.Text, IsEnabled = item.IsEnabled };
                var action = item.Action;
                btn.Click += (s, e) =>
                {
                    // 先关闭托盘菜单（视觉反馈），再执行动作（可能弹窗/开浏览器）
                    CloseTrayPopup();
                    RequestClose?.Invoke();
                    action?.Invoke();
                };
                SubMenuItemsPanel.Children.Add(btn);
            }
        }

        // ═══ 固定项 ═══

        private void OnShowWindowClick(object sender, RoutedEventArgs e)
        {
            ShowWindowCommand?.Execute(null);
            CloseTrayPopup();
            RequestClose?.Invoke();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            ExitCommand?.Execute(null);
            CloseTrayPopup();
            RequestClose?.Invoke();
        }

        /// <summary>关闭托盘菜单（含二级菜单）</summary>
        private void CloseTrayPopup()
        {
            CloseSubMenu();
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow != null && mainWindow.TrayPopup != null && mainWindow.TrayPopup.IsOpen)
            {
                mainWindow.TrayPopup.IsOpen = false;
                mainWindow.TrayPopup = null;
            }
        }
    }
}
