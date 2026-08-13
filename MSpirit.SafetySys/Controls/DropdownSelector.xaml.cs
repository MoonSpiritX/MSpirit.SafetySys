// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Controls
{
    /// <summary>
    /// 带下拉菜单的选择器控件，样式参照导入导出界面的单位/部门下拉框
    /// </summary>
    public partial class DropdownSelector : UserControl
    {
        private DropdownMenu? _activeMenu;

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(DropdownSelector),
                new PropertyMetadata("请选择", OnPlaceholderChanged));

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<string>), typeof(DropdownSelector),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(string), typeof(DropdownSelector),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedItemChanged));

        /// <summary>下拉菜单项变化时触发</summary>
        public event Action<string?>? SelectionChanged;

        /// <summary>
        /// 打开菜单前回调，返回 false 阻止弹出。可用于条件检查（如提示"请先选择单位"）
        /// </summary>
        public Func<bool>? BeforeOpen;

        public DropdownSelector()
        {
            InitializeComponent();
            DisplayTextBox.Text = Placeholder;
        }

        /// <summary>占位文本，默认"请选择"</summary>
        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        /// <summary>菜单项集合</summary>
        public IEnumerable<string> ItemsSource
        {
            get => (IEnumerable<string>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        /// <summary>当前选中项</summary>
        public string SelectedItem
        {
            get => (string)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (DropdownSelector)d;
            if (string.IsNullOrEmpty(control.SelectedItem))
                control.DisplayTextBox.Text = (string)e.NewValue;
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (DropdownSelector)d;
            var value = (string)e.NewValue;
            control.DisplayTextBox.Text = string.IsNullOrEmpty(value) ? control.Placeholder : value;
        }

        private void Control_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 已展开 → 关闭（触发箭头归位动画由 Closed 事件处理）
            if (_activeMenu != null)
            {
                _activeMenu.Close();
                return;
            }

            var items = ItemsSource;
            if (items == null) return;

            if (BeforeOpen != null && !BeforeOpen())
                return;

            ArrowButton.IsChecked = true;

            var menu = new DropdownMenu();
            menu.Closed += () =>
            {
                _activeMenu = null;
                ArrowButton.IsChecked = false;
            };

            foreach (var item in items)
            {
                menu.AddItem(item, () =>
                {
                    SelectedItem = item;
                    SelectionChanged?.Invoke(item);
                });
            }

            _activeMenu = menu;
            menu.ShowBelow(this);
        }

    }
}
