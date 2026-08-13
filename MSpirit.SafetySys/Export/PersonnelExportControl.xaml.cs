// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 人员管理模块数据导出/导入控件（独立于隐患模块）
    /// 使用 单位 → 部门 结构，不依赖年月/日期分组
    /// </summary>
    public partial class PersonnelExportControl : UserControl
    {
        /// <summary>
        /// 用户请求关闭导出界面时触发
        /// </summary>
        public event Action? CloseRequested;

        public PersonnelExportControl()
        {
            InitializeComponent();
            DataContext = new PersonnelExportViewModel(Application.Current.MainWindow);

            // TextBoxBase 的拖放类处理器（handledEventsToo）会在冒泡阶段拦截 DragEnter/DragOver/Drop 并标记 Handled，
            // 普通 XAML 事件绑定无法触发；必须用 AddHandler(..., handledEventsToo: true) 注册，
            // 才能在 TextBox 类处理之后重新设置拖放效果并处理文件放置
            AddHandler(UIElement.DragEnterEvent, new DragEventHandler(Control_DragEnter), true);
            AddHandler(UIElement.DragOverEvent, new DragEventHandler(Control_DragOver), true);
            AddHandler(UIElement.DropEvent, new DragEventHandler(Control_Drop), true);

            Loaded += (s, e) =>
            {
                SetTextBoxesBorderColor("#0078D4");
                SubscribeWindowShortcuts();

                var vm = DataContext as PersonnelExportViewModel;
                if (vm == null) return;
                UnitSelector.ItemsSource = vm.UnitGroups;
                DeptSelector.ItemsSource = vm.DepartmentGroups;
                StatusSelector.ItemsSource = vm.PersonnelStatusOptions;
                CategorySelector.ItemsSource = vm.PersonnelCategoryOptions;
                StatusSelector.SelectedItem = vm.PersonnelStatus;
                CategorySelector.SelectedItem = vm.PersonnelCategory;

                // 人员状态/类别选择 → ViewModel
                StatusSelector.SelectionChanged += item =>
                {
                    if (item != null) vm.PersonnelStatus = item;
                };
                CategorySelector.SelectionChanged += item =>
                {
                    if (item != null) vm.PersonnelCategory = item;
                };

                // ViewModel 属性变更 → 同步下拉显示（切换项目重置等场景，VM 侧改值下拉需跟随）
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(PersonnelExportViewModel.PersonnelStatus))
                        StatusSelector.SelectedItem = vm.PersonnelStatus;
                    else if (e.PropertyName == nameof(PersonnelExportViewModel.PersonnelCategory))
                        CategorySelector.SelectedItem = vm.PersonnelCategory;
                };

                // 单位下拉点开前刷新：界面打开期间可能切换过项目，保证列表与当前项目一致
                UnitSelector.BeforeOpen = () =>
                {
                    vm.RefreshUnitsForCurrentProject();
                    return true;
                };

                // 部门下拉点开前：先重查单位 ID（防跨项目失效），再按新 ID 重查部门列表
                DeptSelector.BeforeOpen = () =>
                {
                    vm.RefreshUnitsForCurrentProject();
                    vm.RefreshDepartmentsForCurrentProject();
                    if (string.IsNullOrEmpty(vm.SelectedUnitId))
                    {
                        ToastService.Show("请先选择单位", this);
                        return false;
                    }
                    return true;
                };
            };
            Unloaded += (s, e) =>
            {
                UnsubscribeWindowShortcuts();
                (DataContext as PersonnelExportViewModel)?.Dispose();
            };
        }

        private Window? _hostWindow;

        /// <summary>
        /// 订阅宿主窗口的 PreviewKeyDown：控件级按键事件依赖键盘焦点，
        /// 而本界面点击空白会清除焦点，导致 Ctrl+V 无法触发，故提升到窗口级处理
        /// </summary>
        private void SubscribeWindowShortcuts()
        {
            _hostWindow = Window.GetWindow(this);
            if (_hostWindow != null)
                _hostWindow.PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void UnsubscribeWindowShortcuts()
        {
            if (_hostWindow != null)
            {
                _hostWindow.PreviewKeyDown -= Window_PreviewKeyDown;
                _hostWindow = null;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+V 粘贴文件（等效于点击"粘贴文件"按钮）
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (DataContext is PersonnelExportViewModel vm && vm.PasteFileCommand.CanExecute(null))
                {
                    vm.PasteFileCommand.Execute(null);
                    e.Handled = true;
                }
            }

            // Escape 关闭导出界面（窗口级处理，避免焦点不在控件内时失效；执行期间禁止关闭）
            if (e.Key == Key.Escape && DataContext is PersonnelExportViewModel vm2 && !vm2.IsExporting)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        }

        private void SetTextBoxesBorderColor(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            foreach (var textBox in FindVisualChildren<System.Windows.Controls.TextBox>(this))
                textBox.BorderBrush = brush;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void Control_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !(e.OriginalSource is System.Windows.Controls.TextBox))
            {
                if (DataContext is PersonnelExportViewModel vm && vm.StartExportCommand.CanExecute(null))
                {
                    vm.StartExportCommand.Execute(null);
                    e.Handled = true;
                }
            }

            if (e.Key == Key.Escape && DataContext is PersonnelExportViewModel vm2 && !vm2.IsExporting)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        }

        // ========== 拖放文件 ==========
        private void Control_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        // DragOver 必须也处理：TextBox 类处理器在冒泡阶段会把非文本拖放标为 None，
        // 若不在根元素上覆盖，拖到文本框上方会一直显示"禁止"图标
        private void Control_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Control_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    string ext = Path.GetExtension(filePath).ToLower();
                    if (ext == ".xlsx" || ext == ".docx")
                    {
                        var vm = DataContext as PersonnelExportViewModel;
                        if (vm != null)
                            vm.SelectedTemplatePath = filePath;
                    }
                    else
                    {
                        ToastService.Show("文件类型不支持。\n目标文件类型仅限于 .xlsx 和 .docx 格式", this);
                    }
                }
            }
            e.Handled = true;
        }

        // ========== 焦点/点击处理 ==========
        private void Grid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsClickOnInteractiveControl(e.OriginalSource as DependencyObject))
                Keyboard.ClearFocus();
        }

        private bool IsClickOnInteractiveControl(DependencyObject? source)
        {
            if (source == null) return false;
            var visual = GetVisualAncestor(source);
            if (visual == null) return false;
            source = visual;

            while (source != null)
            {
                if (source is System.Windows.Controls.TextBox ||
                    source is System.Windows.Controls.Button ||
                    source is System.Windows.Controls.Primitives.ToggleButton)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private static Visual? GetVisualAncestor(DependencyObject? obj)
        {
            while (obj != null && !(obj is Visual))
                obj = LogicalTreeHelper.GetParent(obj);
            return obj as Visual;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke();
        }

        private void NumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, "^[0-9]+$");
        }
    }
}
