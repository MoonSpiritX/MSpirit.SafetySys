// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.ImageViewer;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;


namespace MS_SafetyManagementSystem.Hazard
{
    public partial class RecordCardView : UserControl
    {
        #region 字段

        private Window? _mainWindow;
        private SettingsService? _settingsService;
        private static string? _lastImportDirectory;

        // 图片管理相关
        private List<Attachment?> _hazardAttachments = new List<Attachment?>();
        private List<Attachment?> _remedyAttachments = new List<Attachment?>();
        private int _currentHazardIndex = 1;   // 1-3
        private int _currentRemedyIndex = 1;
        private string _attachmentsRootPath = "";
        private string _projectId = "";
        private string _currentModule = "Hazard";
        private string _currentYearMonth = "";
        private string _cacheRootPath = "";
        private bool _pendingPhotoRefresh;    // 查看器关闭后需刷新附件

        public Record Record
        {
            get => (Record)GetValue(RecordProperty);
            set => SetValue(RecordProperty, value);
        }

        public static readonly DependencyProperty RecordProperty =
            DependencyProperty.Register(nameof(Record), typeof(Record), typeof(RecordCardView), new PropertyMetadata(null, OnRecordChanged));

        #endregion

        #region 构造函数与初始化

        public RecordCardView()
        {
            InitializeComponent();

            // 日期选择器：注入初始月份来源、选中保存回调、右键“统一输入”
            DeadlinePicker.InitialMonthProvider = GetInitialMonthForRecord;
            DeadlinePicker.DateSelected += OnDeadlineDateSelected;
            DeadlinePicker.ContextMenuCustomizer = menu =>
            {
                menu.AddItem("统一输入", () => UnifyDateFieldInput(DeadlinePicker.SelectedDate, r => r.Deadline = DeadlinePicker.SelectedDate));
            };

            CompletedDatePicker.InitialMonthProvider = GetInitialMonthForRecord;
            CompletedDatePicker.DateSelected += OnCompletedDateSelected;
            CompletedDatePicker.ContextMenuCustomizer = menu =>
            {
                menu.AddItem("统一输入", () => UnifyDateFieldInput(CompletedDatePicker.SelectedDate, r => r.CompletedDate = CompletedDatePicker.SelectedDate));
            };

            this.Unloaded += (s, e) =>
            {
                CloseSuggestionMenu();
                CleanupTimers();
                CleanupAutoSaveTimers();
            };
            ManageCommonHazardTypesViewModel.HazardTypeSelectorModeChanged += OnModeChanged;
            this.Loaded += (s, e) =>
            {
                UpdateSeasonalBackground();
                AttachAutoSaveTextChangedEvents();
                AttachSuggestionTextChangedEvents();
                var settingsService = AppServiceProvider.SettingsService;
                _settingsService = settingsService;
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

                // 初始化图片附件路径
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var dataService = mainWindow?.ViewModel.GetProjectDataService();
                if (dataService != null && mainWindow != null)
                {
                    var dbPath = dataService.GetDbPath();
                    if (!string.IsNullOrEmpty(dbPath))
                    {
                        var projectFolder = Path.GetDirectoryName(dbPath);
                        _attachmentsRootPath = Path.GetDirectoryName(projectFolder) ?? "";
                    }
                    else
                    {
                        _attachmentsRootPath = "";
                    }
                    var dataPathService = new DataPathService();
                    _cacheRootPath = Path.Combine(dataPathService.RootPath, "Temp");
                    _projectId = dataService.ProjectId;
                    _currentModule = mainWindow.ViewModel.GetCurrentModuleKey();
                    _currentYearMonth = Record?.YearMonth ?? "";
                }
                LoadAttachments();
                HazardPhotoImage.ContextMenu = null;
                RemedyPhotoImage.ContextMenu = null;

                // 查看器关闭后自动刷新附件（Window.Activated 感知焦点回归）
                var parentWindow = Window.GetWindow(this);
                if (parentWindow != null)
                {
                    parentWindow.Activated += (_, _) =>
                    {
                        if (_pendingPhotoRefresh)
                        {
                            _pendingPhotoRefresh = false;
                            LoadAttachments();
                        }
                    };
                }
            };
        }

        #endregion

        #region 保存记录

        private void SaveRecord()
        {
            if (Record != null)
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                mainWindow?.ViewModel.Records.SaveRecord(Record);
            }
        }

        private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RecordCardView view && e.NewValue is Record record)
            {
                view.DataContext = record;
                view.Dispatcher.BeginInvoke(new Action(() => { }));
                view.Dispatcher.BeginInvoke(new Action(() =>
                {
                    view.CloseSuggestionMenu();
                    view.AttachLostFocusEvents();
                }));
            }
        }

        #endregion

        #region 失焦保存

        private void AttachLostFocusEvents()
        {
            DetachLostFocusEvents();
            AddLostFocusHandler(this);
        }

        private void AddLostFocusHandler(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && (element is TextBox || element is ComboBox || element is DatePicker))
                {
                    element.LostFocus += OnInputLostFocus;
                }
                else
                {
                    AddLostFocusHandler(child);
                }
            }
        }

        private void DetachLostFocusEvents()
        {
            RemoveLostFocusHandler(this);
        }

        private void RemoveLostFocusHandler(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && (element is TextBox || element is ComboBox || element is DatePicker))
                {
                    element.LostFocus -= OnInputLostFocus;
                }
                else
                {
                    RemoveLostFocusHandler(child);
                }
            }
        }

        private void OnInputLostFocus(object sender, RoutedEventArgs e)
        {
            SaveRecord();
        }

        #endregion

        #region 辅助方法

        private void UpdateSeasonalBackground()
        {
            var bgKey = SolarTermHelper.GetSeasonBackgroundKey(DateTime.Now);
            if (Application.Current.Resources[bgKey] is ImageBrush brush)
                CardBorder.Background = brush;
        }

        private Window? GetMainWindow()
        {
            if (_mainWindow == null || !_mainWindow.IsVisible)
                _mainWindow = Window.GetWindow(this);
            return _mainWindow;
        }

        private bool IsVisualChildOf(DependencyObject? parent, DependencyObject? child)
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

        #endregion
    }
}
