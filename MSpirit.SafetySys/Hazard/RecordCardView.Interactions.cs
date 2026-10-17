// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class RecordCardView : UserControl
    {
        #region 模式切换

        private void OnModeChanged()
        {
            Dispatcher.Invoke(() =>
            {
                var settingsService = AppServiceProvider.SettingsService;
                if (settingsService != null && settingsService.GetHazardTypeSelectorMode() == "DropdownMenu")
                {
                    HazardTypeBorder.PreviewMouseLeftButtonDown -= HazardTypeBorder_PreviewMouseLeftButtonDown_FullWindow;
                    HazardTypeBorder.PreviewMouseLeftButtonDown += HazardTypeBorder_PreviewMouseLeftButtonDown_Dropdown;
                }
                else
                {
                    HazardTypeBorder.PreviewMouseLeftButtonDown -= HazardTypeBorder_PreviewMouseLeftButtonDown_Dropdown;
                    HazardTypeBorder.PreviewMouseLeftButtonDown += HazardTypeBorder_PreviewMouseLeftButtonDown_FullWindow;
                }
                if (HazardTypePopup.IsOpen)
                    CloseHazardTypeMenu();
            });
        }

        #endregion

        #region TextBox 右键菜单

        private void TextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;
            _currentRightClickTextBox = textBox;

            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            double offsetY = -15;
            var adjustedPoint = new Point(wpfPoint.X, wpfPoint.Y + offsetY);
            ShowTextBoxContextMenu(textBox, adjustedPoint);
            e.Handled = true;
        }

        private void ShowTextBoxContextMenu(TextBox textBox, Point screenPoint)
        {
            _activeTextBoxMenu?.Close();
            _activeTextBoxMenu = null;

            var menu = new DropdownMenu();

            menu.AddItem("粘贴 (Ctrl+V)", () => PasteStandard(textBox));
            menu.AddItem("复制 (Ctrl+C)", () => CopyFullText(textBox));
            menu.AddItem("剪切 (Ctrl+X)", () => CutFullText(textBox));
            menu.AddItem("清空 (Ctrl+Q)", () => ClearFullText(textBox));

            if (textBox.Name == "ResponsibleUnitTextBox" ||
                textBox.Name == "ResponsiblePersonTextBox" ||
                textBox.Name == "ReviewerTextBox")
            {
                menu.AddSeparator();
                menu.AddItem("统一输入", () => UnifyFieldInput(textBox));
            }

            menu.Closed += () => _activeTextBoxMenu = null;
            _activeTextBoxMenu = menu;
            menu.ShowAt(screenPoint);
        }

        private void PasteStandard(TextBox textBox)
        {
            textBox.Paste();
        }

        private void CopyFullText(TextBox textBox)
        {
            if (!string.IsNullOrEmpty(textBox.Text))
                Clipboard.SetText(textBox.Text);
        }

        private void CutFullText(TextBox textBox)
        {
            if (!string.IsNullOrEmpty(textBox.Text))
            {
                Clipboard.SetText(textBox.Text);
                textBox.Text = "";
            }
        }

        private void ClearFullText(TextBox textBox)
        {
            textBox.Text = "";
        }

        #endregion

        #region 卡片右键菜单

        private void CardBorder_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hitElement = e.OriginalSource as DependencyObject;
            if (IsOverInteractiveControl(hitElement))
                return;

            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            ShowCardContextMenu(wpfPoint);
            e.Handled = true;
        }

        private bool IsOverInteractiveControl(DependencyObject? hitElement)
        {
            if (hitElement == null) return false;
            while (hitElement != null)
            {
                if (hitElement is Button || hitElement is TextBox || hitElement is CheckBox ||
                    hitElement is ToggleButton ||
                    (hitElement is Border border && (border.Name == "HazardPhotoDisplayArea" || border.Name == "RemedyPhotoDisplayArea" ||
                                                     border.Name == "HazardTypeBorder" || border.Name == "HazardLevelBorder")) ||
                    hitElement is Image || hitElement is Popup ||
                    (hitElement is TextBlock textBlock && (textBlock.Name == "HazardTypeTextBlock" || textBlock.Name == "HazardLevelTextBlock")))
                    return true;
                hitElement = VisualTreeHelper.GetParent(hitElement);
            }
            return false;
        }

        private void ShowCardContextMenu(Point screenPoint)
        {
            _activeCardMenu?.Close();
            _activeCardMenu = null;

            var menu = new DropdownMenu();

            menu.AddItem("生成图片", () => GenerateImage());

            menu.AddSeparator();

            menu.AddItem("清空卡片", () =>
            {
                if (MessageDialog.Confirm("确定要清空当前记录卡的所有内容吗？", "确认清空") == true)
                {
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    mainWindow?.ViewModel.Records.ClearRecord(this.Record);
                }
            });

            menu.AddItem("删除卡片", () =>
            {
                if (MessageDialog.Confirm("确定要删除当前记录卡吗？此操作不可恢复。", "确认删除") == true)
                {
                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    mainWindow?.ViewModel.Records.DeleteRecord(this.Record);
                }
            });

            menu.AddSeparator();

            menu.AddItem("移动卡片", () =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow == null) return;
                int year = GetRecordCurrentYear();
                int month = GetRecordCurrentMonth();
                int maxDays = DateTime.DaysInMonth(year, month);
                var dialog = new MoveOrCopyDialog("移动卡片", month, maxDays);
                dialog.Owner = mainWindow;
                if (dialog.ShowDialog() == true)
                {
                    mainWindow.ViewModel.Records.MoveRecord(this.Record, dialog.Day, dialog.TargetTag);
                }
            });

            menu.AddItem("复制卡片", () =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow == null) return;
                int year = GetRecordCurrentYear();
                int month = GetRecordCurrentMonth();
                int maxDays = DateTime.DaysInMonth(year, month);
                var dialog = new MoveOrCopyDialog("复制卡片", month, maxDays);
                dialog.Owner = mainWindow;
                if (dialog.ShowDialog() == true)
                {
                    mainWindow.ViewModel.Records.CopyRecord(this.Record, dialog.Day, dialog.TargetTag);
                }
            });

            menu.Closed += () => _activeCardMenu = null;
            _activeCardMenu = menu;
            menu.ShowAt(screenPoint);
        }

        private void GenerateImage()
        {
            if (Record == null) return;

            string projectName = "未命名项目";
            if (Application.Current.MainWindow is MainWindow mainWindow && mainWindow.ViewModel != null)
            {
                projectName = mainWindow.ViewModel.CurrentProjectName;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
                projectName = projectName.Replace(c.ToString(), "_");
            projectName = projectName.Replace(" ", "_");

            string moduleName = _currentModule;
            string datePart = GetRecordFullDate();
            int globalIndex = GetGlobalCardIndex();

            string fileName = $"{projectName}_{moduleName}_{datePart}_{globalIndex}.png";

            var originalEffect = CardBorder.Effect;
            CardBorder.Effect = null;

            CardBorder.UpdateLayout();

            bool success = ScreenshotHelper.CaptureToFile(
                CardBorder,
                fileName,
                ref _lastExportDirectory,
                padding: 5,
                autoCrop: true,
                onBeforeRender: null,
                onAfterRender: () => CardBorder.Effect = originalEffect
            );

            if (success)
            {
                ToastService.Show("记录卡图片已保存", this);
            }
        }

        #endregion

        #region 统一输入

        private void UnifyFieldInput(TextBox textBox)
        {
            if (Record == null) return;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow?.ViewModel == null) return;
            var dataService = mainWindow.ViewModel.GetProjectDataService();
            if (dataService == null) return;

            string fieldName;
            switch (textBox.Name)
            {
                case "ResponsibleUnitTextBox":
                    fieldName = "ResponsibleUnit";
                    break;
                case "ResponsiblePersonTextBox":
                    fieldName = "ResponsiblePerson";
                    break;
                case "ReviewerTextBox":
                    fieldName = "Reviewer";
                    break;
                default:
                    return;
            }

            string newValue = textBox.Text;

            var allGroups = dataService.GetAllGroups(_currentModule);
            var currentGroup = allGroups.FirstOrDefault(g => g.Id == Record.GroupId);
            if (currentGroup == null || string.IsNullOrEmpty(currentGroup.ParentId))
                return;

            var siblingGroupIds = allGroups
                .Where(g => g.ParentId == currentGroup.ParentId &&
                            g.Name == currentGroup.Name &&
                            g.Tag == currentGroup.Tag)
                .Select(g => g.Id)
                .ToHashSet();

            var targetRecords = mainWindow.ViewModel.Records.Records
                .Where(r => siblingGroupIds.Contains(r.GroupId) && r.Id != Record.Id)
                .ToList();

            if (targetRecords.Count == 0) return;

            foreach (var target in targetRecords)
            {
                switch (fieldName)
                {
                    case "ResponsibleUnit":
                        target.ResponsibleUnit = newValue;
                        break;
                    case "ResponsiblePerson":
                        target.ResponsiblePerson = newValue;
                        break;
                    case "Reviewer":
                        target.Reviewer = newValue;
                        break;
                }
                mainWindow.ViewModel.Records.SaveRecord(target);
            }
        }

        private void UnifyDateFieldInput(DateTime? dateValue, Action<Record> setDateValue)
        {
            if (Record == null) return;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow?.ViewModel == null) return;
            var dataService = mainWindow.ViewModel.GetProjectDataService();
            if (dataService == null) return;

            var allGroups = dataService.GetAllGroups(_currentModule);
            var currentGroup = allGroups.FirstOrDefault(g => g.Id == Record.GroupId);
            if (currentGroup == null || string.IsNullOrEmpty(currentGroup.ParentId))
                return;

            var siblingGroupIds = allGroups
                .Where(g => g.ParentId == currentGroup.ParentId &&
                            g.Name == currentGroup.Name &&
                            g.Tag == currentGroup.Tag)
                .Select(g => g.Id)
                .ToHashSet();

            var targetRecords = mainWindow.ViewModel.Records.Records
                .Where(r => siblingGroupIds.Contains(r.GroupId) && r.Id != Record.Id)
                .ToList();

            if (targetRecords.Count == 0) return;

            foreach (var target in targetRecords)
            {
                setDateValue(target);
                mainWindow.ViewModel.Records.SaveRecord(target);
            }
        }

        #endregion

        #region 快捷键

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            // 搜索建议下拉：↑/↓/Enter/Esc 交给菜单处理，Tab 关闭菜单
            if (_activeSuggestionMenu != null && _activeSuggestionMenu.IsOpen && _suggestionTriggerTextBox == textBox)
            {
                if (e.Key == Key.Tab)
                {
                    CloseSuggestionMenu();
                }
                else if (_activeSuggestionMenu.HandleNavigationKey(e.Key))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.KeyboardDevice.Modifiers != ModifierKeys.Control) return;

            if (e.Key == Key.V)
            {
                textBox.Paste();
                e.Handled = true;
            }
            else if (e.Key == Key.C)
            {
                CopyFullText(textBox);
                e.Handled = true;
            }
            else if (e.Key == Key.X)
            {
                CutFullText(textBox);
                e.Handled = true;
            }
            else if (e.Key == Key.Q)
            {
                ClearFullText(textBox);
                e.Handled = true;
            }
        }

        #endregion

        #region 隐患类型选择器

        private TextBox? _currentRightClickTextBox;
        private DropdownMenu? _activeTextBoxMenu;
        private DropdownMenu? _activeCardMenu;
        private Button? _moreTypesButton;
        private DispatcherTimer? _openSubTimer;
        private DispatcherTimer? _closeSubTimer;
        private bool _isMouseOverMoreButton = false;
        private bool _isMouseOverSubMenu = false;
        private static string? _lastExportDirectory;
        private DropdownMenu? _activeLevelMenu;

        private void HazardTypeBorder_PreviewMouseLeftButtonDown_FullWindow(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            OpenAllTypesWindowModal();
        }

        private void OpenAllTypesWindowModal()
        {
            if (Record == null) return;
            var service = AppServiceProvider.CommonHazardTypeService;
            if (service == null)
            {
                MessageDialog.Show("隐患类型服务未初始化，无法打开类型选择窗口。", "提示");
                return;
            }

            string originalValue = Record.HazardType;
            var win = new AllHazardTypesWindow(service);
            win.Owner = Window.GetWindow(this);
            if (win.ShowDialog() == true)
            {
                string? selectedType = win.SelectedTypeName;
                if (selectedType != null)
                {
                    Record.HazardType = selectedType;
                    SaveRecord();
                }
            }
            else
            {
                if (Record.HazardType != originalValue)
                {
                    Record.HazardType = originalValue;
                    SaveRecord();
                }
            }
        }

        private void HazardTypeBorder_PreviewMouseLeftButtonDown_Dropdown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var originalSource = e.OriginalSource as DependencyObject;

            if (_moreTypesButton != null && IsVisualChildOf(_moreTypesButton, originalSource))
            {
                if (!FullTypeSubPopup.IsOpen)
                {
                    if (_openSubTimer != null) _openSubTimer.Stop();
                    if (_closeSubTimer != null) _closeSubTimer.Stop();
                    OpenSubMenu();
                }
                return;
            }

            if (HazardTypePopup.IsOpen)
                CloseHazardTypeMenu();
            else
                OpenHazardTypeMenu();
        }

        private void OpenHazardTypeMenu()
        {
            if (HazardTypePopup.IsOpen)
            {
                CloseHazardTypeMenu();
                return;
            }

            BuildHazardTypeMenu();
            HazardTypePopup.IsOpen = true;
            HazardTypeToggleButton.IsChecked = true;
            SubscribeToGlobalEvents();
        }

        private void BuildHazardTypeMenu()
        {
            HazardTypeMenuPanel.Children.Clear();
            var commonService = AppServiceProvider.CommonHazardTypeService;
            if (commonService == null) return;
            var commonNames = commonService.GetCommonNames();

            foreach (var typeName in commonNames)
            {
                AddMenuItem(typeName, () => SetHazardType(typeName));
            }

            if (commonNames.Any())
                AddSeparator();

            _moreTypesButton = new Button
            {
                Content = "更多类型  →",
                Style = (Style)FindResource("RecordCardContextMenuItemStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 30,
                Margin = new Thickness(0, 2, 0, 2),
                Name = "MoreTypesButton"
            };
            _moreTypesButton.Click += MoreTypesButton_Click;
            _moreTypesButton.MouseEnter += MoreTypesButton_MouseEnter;
            _moreTypesButton.MouseLeave += MoreTypesButton_MouseLeave;
            HazardTypeMenuPanel.Children.Add(_moreTypesButton);

            AddSeparator();
            AddMenuItem("清空类型", () => SetHazardType(""));
        }

        private void AddMenuItem(string header, Action clickAction)
        {
            var btn = new Button
            {
                Content = header,
                Style = (Style)FindResource("RecordCardContextMenuItemStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 30,
                Margin = new Thickness(0, 2, 0, 2)
            };
            btn.Click += (s, e) => { clickAction(); CloseHazardTypeMenu(); };
            HazardTypeMenuPanel.Children.Add(btn);
        }

        private void AddSeparator()
        {
            var sep = new Separator { Margin = new Thickness(0, 1, 0, 1) };
            HazardTypeMenuPanel.Children.Add(sep);
        }

        private void SetHazardType(string typeName)
        {
            if (Record == null) return;
            Record.HazardType = typeName;
            SaveRecord();
        }

        private void MoreTypesButton_MouseEnter(object sender, MouseEventArgs e)
        {
            _isMouseOverMoreButton = true;
            if (_closeSubTimer != null) _closeSubTimer.Stop();
            if (FullTypeSubPopup.IsOpen) return;

            if (_openSubTimer == null)
            {
                _openSubTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _openSubTimer.Tick += (s, _) =>
                {
                    _openSubTimer.Stop();
                    if (_isMouseOverMoreButton && !FullTypeSubPopup.IsOpen)
                        OpenSubMenu();
                };
            }
            _openSubTimer.Start();
        }

        private void MoreTypesButton_MouseLeave(object sender, MouseEventArgs e)
        {
            _isMouseOverMoreButton = false;
            if (FullTypeSubPopup.IsOpen)
                StartCloseSubMenuTimer();
        }

        private void MoreTypesButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (!FullTypeSubPopup.IsOpen)
            {
                if (_openSubTimer != null) _openSubTimer.Stop();
                if (_closeSubTimer != null) _closeSubTimer.Stop();
                OpenSubMenu();
            }
        }

        private void OpenSubMenu()
        {
            if (FullTypeSubPopup.IsOpen) return;

            BuildFullTypeGrid();
            FullTypeSubPopup.PlacementTarget = _moreTypesButton;
            FullTypeSubPopup.IsOpen = true;

            if (_closeSubTimer != null) _closeSubTimer.Stop();

            FullTypeSubPopup.Child.MouseEnter += (s, e) => { _isMouseOverSubMenu = true; if (_closeSubTimer != null) _closeSubTimer.Stop(); };
            FullTypeSubPopup.Child.MouseLeave += (s, e) => { _isMouseOverSubMenu = false; StartCloseSubMenuTimer(); };
        }

        private void BuildFullTypeGrid()
        {
            FullTypeGrid.Children.Clear();
            var commonService = AppServiceProvider.CommonHazardTypeService;
            if (commonService == null) return;
            var allTypes = commonService.GetAllTypes();
            var commonNames = commonService.GetCommonNames();

            foreach (var type in allTypes)
            {
                if (commonNames.Contains(type.Name))
                    continue;

                var btn = new Button
                {
                    Content = type.Name,
                    Style = (Style)FindResource("RecordCardContextMenuItemStyle"),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Height = 30,
                    Margin = new Thickness(2),
                    Tag = type.Name
                };
                btn.Click += (s, e) =>
                {
                    SetHazardType(type.Name);
                    CloseHazardTypeMenu();
                };
                FullTypeGrid.Children.Add(btn);
            }
        }

        private void StartCloseSubMenuTimer()
        {
            if (_closeSubTimer == null)
            {
                _closeSubTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _closeSubTimer.Tick += (s, _) =>
                {
                    _closeSubTimer.Stop();
                    if (!_isMouseOverMoreButton && !_isMouseOverSubMenu && FullTypeSubPopup.IsOpen)
                        FullTypeSubPopup.IsOpen = false;
                };
            }
            _closeSubTimer.Start();
        }

        private void CloseHazardTypeMenu()
        {
            if (HazardTypePopup.IsOpen)
            {
                HazardTypePopup.IsOpen = false;
                HazardTypeToggleButton.IsChecked = false;
            }
            if (FullTypeSubPopup.IsOpen)
                FullTypeSubPopup.IsOpen = false;
            UnsubscribeFromGlobalEvents();
        }

        private void SubscribeToGlobalEvents()
        {
            var mainWin = GetMainWindow();
            if (mainWin != null)
            {
                mainWin.PreviewMouseDown += OnGlobalPreviewMouseDown;
                mainWin.Deactivated += OnGlobalDeactivated;
                mainWin.PreviewMouseWheel += OnGlobalPreviewMouseWheel;
            }
        }

        private void UnsubscribeFromGlobalEvents()
        {
            var mainWin = GetMainWindow();
            if (mainWin != null)
            {
                mainWin.PreviewMouseDown -= OnGlobalPreviewMouseDown;
                mainWin.Deactivated -= OnGlobalDeactivated;
                mainWin.PreviewMouseWheel -= OnGlobalPreviewMouseWheel;
            }
        }

        private void OnGlobalPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!HazardTypePopup.IsOpen && !FullTypeSubPopup.IsOpen)
            {
                var mainWin = GetMainWindow();
                if (mainWin != null)
                    mainWin.PreviewMouseWheel -= OnGlobalPreviewMouseWheel;
                return;
            }
            e.Handled = true;
        }

        private void OnGlobalPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!HazardTypePopup.IsOpen && !FullTypeSubPopup.IsOpen && _activeLevelMenu == null)
                return;

            var hitElement = e.OriginalSource as DependencyObject;
            if (hitElement == null) return;

            bool clickedInsideMainMenu = IsVisualChildOf(HazardTypePopup.Child, hitElement) ||
                                         IsVisualChildOf(HazardTypeBorder, hitElement) ||
                                         IsVisualChildOf(HazardTypeTextBlock, hitElement) ||
                                         IsVisualChildOf(HazardTypeToggleButton, hitElement);
            bool clickedInsideSubMenu = FullTypeSubPopup.IsOpen && IsVisualChildOf(FullTypeSubPopup.Child, hitElement);
            bool clickedInsideLevelMenu = _activeLevelMenu != null;
            bool clickedOnLevelBorder = IsVisualChildOf(HazardLevelBorder, hitElement) ||
                                        IsVisualChildOf(HazardLevelTextBlock, hitElement) ||
                                        IsVisualChildOf(HazardLevelToggleButton, hitElement);

            if (!clickedInsideMainMenu && !clickedInsideSubMenu && !clickedInsideLevelMenu && !clickedOnLevelBorder)
            {
                CloseHazardTypeMenu();
            }
        }

        private void OnGlobalDeactivated(object? sender, EventArgs e)
        {
            CloseHazardTypeMenu();
        }

        private void CleanupTimers()
        {
            _openSubTimer?.Stop();
            _closeSubTimer?.Stop();
        }

        #endregion

        #region 隐患级别选择器

        private void HazardLevelBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (HazardTypePopup.IsOpen)
                CloseHazardTypeMenu();

            if (_activeLevelMenu != null)
            {
                _activeLevelMenu.Close();
                _activeLevelMenu = null;
                HazardLevelToggleButton.IsChecked = false;
                return;
            }

            var menu = new DropdownMenu();
            string[] levels = { "一般", "较大", "重大" };
            foreach (var level in levels)
            {
                var capturedLevel = level;
                menu.AddItem(level, () =>
                {
                    Record.HazardLevel = capturedLevel;
                    SaveRecord();
                });
            }
            menu.AddSeparator();
            menu.AddItem("取消选择", () =>
            {
                Record.HazardLevel = "";
                SaveRecord();
            });

            menu.Closed += () =>
            {
                _activeLevelMenu = null;
                HazardLevelToggleButton.IsChecked = false;
            };
            _activeLevelMenu = menu;
            HazardLevelToggleButton.IsChecked = true;
            menu.ShowBelow(HazardLevelBorder);
        }

        #endregion

        #region 日期选择器（宿主接线）

        /// <summary>
        /// 日历弹窗打开时的初始月份：优先取记录已有日期，其次解析当前年月分组，最后当前月
        /// </summary>
        private DateTime GetInitialMonthForRecord()
        {
            if (Record != null)
            {
                if (Record.Deadline.HasValue)
                    return Record.Deadline.Value;
                if (Record.CompletedDate.HasValue)
                    return Record.CompletedDate.Value;
            }

            if (Record != null && !string.IsNullOrEmpty(Record.GroupId))
            {
                var mainWin = Application.Current.MainWindow as MainWindow;
                var dataService = mainWin?.ViewModel.GetProjectDataService();
                if (dataService != null && mainWin != null)
                {
                    var module = mainWin.ViewModel.GetCurrentModuleKey();
                    var allGroups = dataService.GetAllGroups(module);
                    var currentGroup = allGroups.FirstOrDefault(g => g.Id == Record.GroupId);
                    if (currentGroup != null)
                    {
                        var yearMonthGroup = currentGroup;
                        if (!string.IsNullOrEmpty(currentGroup.ParentId))
                            yearMonthGroup = allGroups.FirstOrDefault(g => g.Id == currentGroup.ParentId);
                        if (yearMonthGroup != null)
                        {
                            var match = Regex.Match(yearMonthGroup.Name, @"(\d{4})年(\d{1,2})月");
                            if (match.Success)
                            {
                                int year = int.Parse(match.Groups[1].Value);
                                int month = int.Parse(match.Groups[2].Value);
                                return new DateTime(year, month, 1);
                            }
                        }
                    }
                }
            }

            return DateTime.Now;
        }

        private void OnDeadlineDateSelected(object? sender, DateTime? date)
        {
            if (Record == null) return;
            Record.Deadline = date;
            SaveRecord();
        }

        private void OnCompletedDateSelected(object? sender, DateTime? date)
        {
            if (Record == null) return;
            Record.CompletedDate = date;
            SaveRecord();
        }

        #endregion

    }
}
