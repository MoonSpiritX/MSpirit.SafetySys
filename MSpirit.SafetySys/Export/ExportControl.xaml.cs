// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;

namespace MS_SafetyManagementSystem.Export
{
    public partial class ExportControl : UserControl
    {
        private DropdownMenu? _activeYearMonthMenu;
        private DropdownMenu? _activeDateGroupMenu;

        // 自定义范围弹出菜单
        private DropdownMenu? _activeStartDateYearMenu;   // 起始日期的年月一级菜单
        private DropdownMenu? _activeStartDateDayMenu;    // 起始日期的日期二级菜单
        private DropdownMenu? _activeEndDateYearMenu;     // 结束日期的年月一级菜单
        private DropdownMenu? _activeEndDateDayMenu;      // 结束日期的日期二级菜单

        /// <summary>
        /// 用户请求关闭导出界面时触发
        /// </summary>
        public event Action? CloseRequested;

        public ExportControl(string projectName, string moduleName)
        {
            InitializeComponent();
            DataContext = new ExportWindowViewModel(Application.Current.MainWindow, moduleName);

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
            };
            Unloaded += (s, e) =>
            {
                UnsubscribeWindowShortcuts();
                (DataContext as ExportWindowViewModel)?.Dispose();
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
                if (DataContext is ExportWindowViewModel vm && vm.PasteFileCommand.CanExecute(null))
                {
                    vm.PasteFileCommand.Execute(null);
                    e.Handled = true;
                }
            }

            // Escape 关闭导出界面（窗口级处理，避免焦点不在控件内时失效；执行期间禁止关闭）
            if (e.Key == Key.Escape && DataContext is ExportWindowViewModel vm2 && !vm2.IsExporting)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        }

        private void Control_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter 开始导出（仅当未聚焦在文本框内时触发）
            if (e.Key == Key.Enter && !(e.OriginalSource is TextBox))
            {
                if (DataContext is ExportWindowViewModel vm && vm.StartExportCommand.CanExecute(null))
                {
                    vm.StartExportCommand.Execute(null);
                    e.Handled = true;
                }
            }

            // Escape 关闭（执行期间禁止）
            if (e.Key == Key.Escape && DataContext is ExportWindowViewModel vm2 && !vm2.IsExporting)
            {
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        }

        private void SetTextBoxesBorderColor(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            foreach (var textBox in FindVisualChildren<TextBox>(this))
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
                        var vm = DataContext as ExportWindowViewModel;
                        if (vm != null)
                            vm.SelectedTemplatePath = filePath;
                    }
                    else
                    {
                        ToastService.Show("文件类型不支持。\n导出目标文件类型仅限于 .xlsx 和 .docx 格式", this);
                    }
                }
            }
            e.Handled = true;
        }

        private void Grid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 如果点击的不是可交互控件，则清除焦点
            if (!IsClickOnInteractiveControl(e.OriginalSource as DependencyObject))
            {
                Keyboard.ClearFocus();
            }
        }

        private bool IsClickOnInteractiveControl(DependencyObject? source)
        {
            if (source == null) return false;

            var visual = GetVisualAncestor(source);
            if (visual == null) return false;
            source = visual;

            // 检查是否点击在特定的下拉框边框上
            var temp = source;
            while (temp != null)
            {
                if (temp == YearMonthBorder2 || temp == DateGroupBorder ||
                    temp == StartDateBorder || temp == EndDateBorder)
                    return true;
                temp = VisualTreeHelper.GetParent(temp);
            }

            // 检查是否点击在可交互控件上
            while (source != null)
            {
                if (source is TextBox ||
                    source is Button ||
                    source is System.Windows.Controls.RadioButton ||
                    source is System.Windows.Controls.CheckBox ||
                    source is System.Windows.Controls.ComboBox ||
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

        // ========== 年月分组下拉菜单（月度及单日） ==========
        private void YearMonthBorder2_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeYearMonthMenu != null)
            {
                _activeYearMonthMenu.Close();
                return;
            }

            var viewModel = DataContext as ExportWindowViewModel;
            if (viewModel == null) return;

            // 界面打开期间可能切换过项目：刷新分组缓存，保证下拉列表与当前项目一致
            viewModel.RefreshGroupsForCurrentProject();

            var filteredYearMonths = viewModel.GetFilteredYearMonthNamesForMenu();
            if (filteredYearMonths == null || filteredYearMonths.Count == 0)
            {
                MessageDialog.Show(viewModel.IsImportMode ? "当前项目还没有年月分组，请先在程序中新建年月及日期分组" : "当前没有可用的年月分组（没有包含有效日期的分组）", "提示");
                return;
            }

            var menu = new DropdownMenu();
            menu.Closed += () => { YearMonthToggleButton2.IsChecked = false; _activeYearMonthMenu = null; };
            foreach (var groupName in filteredYearMonths)
            {
                var name = groupName;
                menu.AddItem(name, () => { viewModel.SelectedYearMonthText = name; });
            }
            menu.ShowBelow((FrameworkElement)sender);
            _activeYearMonthMenu = menu;
            YearMonthToggleButton2.IsChecked = true;
            e.Handled = true;
        }

        // ========== 日期分组下拉菜单（月度及单日） ==========
        private void DateGroupBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeDateGroupMenu != null)
            {
                _activeDateGroupMenu.Close();
                return;
            }

            var viewModel = DataContext as ExportWindowViewModel;
            if (viewModel == null) return;

            // 界面打开期间可能切换过项目：刷新分组缓存，保证日期列表与当前项目一致
            viewModel.RefreshGroupsForCurrentProject();

            if (string.IsNullOrEmpty(viewModel.SelectedYearMonthText) || viewModel.SelectedYearMonthText == "请选择年月")
            {
                ToastService.Show("请先选择年月分组", this);
                return;
            }

            var dateGroups = viewModel.GetValidDateGroupsForYearMonth(viewModel.SelectedYearMonthText);
            if (dateGroups == null || dateGroups.Count == 0)
            {
                ToastService.Show(viewModel.IsImportMode ? "当前年月下还没有日期分组，请先在程序中新建日期分组" : "当前年月下没有有效日期分组（无记录卡）", this);
                return;
            }

            var menu = new DropdownMenu();
            menu.Closed += () => { DateGroupToggleButton.IsChecked = false; _activeDateGroupMenu = null; };
            foreach (var dg in dateGroups)
            {
                string display = dg.Name;
                if (!string.IsNullOrEmpty(dg.Tag))
                    display += $" (标签:{dg.Tag})";
                var name = display;
                menu.AddItem(name, () => { viewModel.SelectedDateGroupText = name; });
            }
            menu.ShowBelow((FrameworkElement)sender);
            _activeDateGroupMenu = menu;
            DateGroupToggleButton.IsChecked = true;
            e.Handled = true;
        }

        // ========== 起始日期两级菜单 ==========
        private void StartDateBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeStartDateYearMenu != null)
            {
                CloseStartDateMenu();
                return;
            }

            var viewModel = DataContext as ExportWindowViewModel;
            if (viewModel == null) return;

            var filteredYearMonths = viewModel.GetFilteredYearMonthGroupsForCustomRange();
            if (filteredYearMonths.Count == 0)
            {
                ToastService.Show("当前没有可用的年月分组（没有包含有效日期的分组）", this);
                return;
            }

            var menu = new DropdownMenu();
            menu.Closed += () => { StartDateToggleButton.IsChecked = false; _activeStartDateYearMenu = null; };
            foreach (var ymInfo in filteredYearMonths)
            {
                var ymName = ymInfo.Name;
                var btn = menu.AddItem(ymName, null);
                if (btn != null)
                {
                    btn.MouseEnter += (s, args) => ShowStartDateSubMenu(btn, ymName);
                }
            }
            menu.ShowBelow((FrameworkElement)sender);
            _activeStartDateYearMenu = menu;
            StartDateToggleButton.IsChecked = true;
            e.Handled = true;
        }

        private void ShowStartDateSubMenu(System.Windows.Controls.Button yearMonthBtn, string yearMonthName)
        {
            CloseStartDateSubMenu();

            var viewModel = DataContext as ExportWindowViewModel;
            if (viewModel == null) return;

            var dateGroups = viewModel.GetValidDateGroupsForYearMonth(yearMonthName);
            if (dateGroups.Count == 0)
            {
                ToastService.Show("该年月下没有有效日期分组（无记录卡）", this);
                return;
            }

            var subMenu = new DropdownMenu();
            foreach (var dg in dateGroups)
            {
                string display = dg.Name;
                if (!string.IsNullOrEmpty(dg.Tag))
                    display += $" (标签:{dg.Tag})";
                var dgCopy = dg;
                subMenu.AddItem(display, () =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(yearMonthName, @"(\d{4})年(\d{1,2})月");
                    if (match.Success)
                    {
                        int year = int.Parse(match.Groups[1].Value);
                        int month = int.Parse(match.Groups[2].Value);
                        var dayMatch = System.Text.RegularExpressions.Regex.Match(dgCopy.Name, @"(\d{1,2})月(\d{1,2})日");
                        if (dayMatch.Success)
                        {
                            int day = int.Parse(dayMatch.Groups[2].Value);
                            string dateStr = $"{year:D4}{month:D2}{day:D2}";
                            viewModel.CustomStartDateStr = dateStr;
                            viewModel.StartDateTag = dgCopy.Tag ?? "";
                        }
                    }
                    CloseStartDateMenu();
                });
            }
            _activeStartDateDayMenu = subMenu;
            subMenu.Closed += () => _activeStartDateDayMenu = null;

            subMenu.ShowRightOf(yearMonthBtn);
        }

        // ========== 结束日期两级菜单（与起始日期对称） ==========
        private void EndDateBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeEndDateYearMenu != null)
            {
                CloseEndDateMenu();
                return;
            }

            var viewModel = DataContext as ExportWindowViewModel;
            if (viewModel == null) return;

            var filteredYearMonths = viewModel.GetFilteredYearMonthsForEndDate();
            if (filteredYearMonths.Count == 0)
            {
                ToastService.Show("当前没有可用的年月分组（没有包含有效日期的分组）", this);
                return;
            }

            var menu = new DropdownMenu();
            menu.Closed += () => { EndDateToggleButton.IsChecked = false; _activeEndDateYearMenu = null; };
            foreach (var ymInfo in filteredYearMonths)
            {
                var ymName = ymInfo.Name;
                var btn = menu.AddItem(ymName, null);
                if (btn != null)
                {
                    btn.MouseEnter += (s, args) => ShowEndDateSubMenu(btn, ymName);
                }
            }
            menu.ShowBelow((FrameworkElement)sender);
            _activeEndDateYearMenu = menu;
            EndDateToggleButton.IsChecked = true;
            e.Handled = true;
        }

        private void ShowEndDateSubMenu(System.Windows.Controls.Button yearMonthBtn, string yearMonthName)
        {
            CloseEndDateSubMenu();

            var viewModel = DataContext as ExportWindowViewModel;
            if (viewModel == null) return;

            var dateGroups = viewModel.GetFilteredDateGroupsForEndDate(yearMonthName, viewModel.CustomStartDateStr);
            if (dateGroups.Count == 0)
            {
                ToastService.Show("该年月下没有符合条件的日期分组", this);
                return;
            }

            var subMenu = new DropdownMenu();
            foreach (var dg in dateGroups)
            {
                string display = dg.Name;
                if (!string.IsNullOrEmpty(dg.Tag))
                    display += $" (标签:{dg.Tag})";
                var dgCopy = dg;
                subMenu.AddItem(display, () =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(yearMonthName, @"(\d{4})年(\d{1,2})月");
                    if (match.Success)
                    {
                        int year = int.Parse(match.Groups[1].Value);
                        int month = int.Parse(match.Groups[2].Value);
                        var dayMatch = System.Text.RegularExpressions.Regex.Match(dgCopy.Name, @"(\d{1,2})月(\d{1,2})日");
                        if (dayMatch.Success)
                        {
                            int day = int.Parse(dayMatch.Groups[2].Value);
                            string dateStr = $"{year:D4}{month:D2}{day:D2}";
                            viewModel.CustomEndDateStr = dateStr;
                            viewModel.EndDateTag = dgCopy.Tag ?? "";
                        }
                    }
                    CloseEndDateMenu();
                });
            }
            _activeEndDateDayMenu = subMenu;
            subMenu.Closed += () => _activeEndDateDayMenu = null;

            subMenu.ShowRightOf(yearMonthBtn);
        }

        // ========== 辅助关闭方法 ==========
        private void CloseStartDateSubMenu()
        {
            _activeStartDateDayMenu?.Close();
            _activeStartDateDayMenu = null;
        }

        private void CloseStartDateMenu()
        {
            CloseStartDateSubMenu();
            _activeStartDateYearMenu?.Close();
            _activeStartDateYearMenu = null;
        }

        private void CloseEndDateSubMenu()
        {
            _activeEndDateDayMenu?.Close();
            _activeEndDateDayMenu = null;
        }

        private void CloseEndDateMenu()
        {
            CloseEndDateSubMenu();
            _activeEndDateYearMenu?.Close();
            _activeEndDateYearMenu = null;
        }

        private void NumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke();
        }
    }
}
