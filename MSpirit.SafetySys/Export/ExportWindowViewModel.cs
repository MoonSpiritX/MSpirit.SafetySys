// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Export
{
    #region 缓存用内部类
    public class YearMonthGroupInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<DateGroupInfo> DateGroups { get; set; } = new();
    }

    public class DateGroupInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Tag { get; set; }
        public bool HasRecords { get; set; }
    }
    #endregion

    public partial class ExportWindowViewModel : ObservableObject
    {
        #region 依赖注入

        private readonly Window _ownerWindow;
        private readonly string _moduleName;
        private bool IsPersonnel => _moduleName == ModuleNames.Personnel;

        #endregion

        #region 可观察属性

        [ObservableProperty]
        private string _selectedTemplatePath = string.Empty;

        [ObservableProperty]
        private bool _isMonthlyRangeInteractive = true;

        [ObservableProperty]
        private bool _isCustomRangeInteractive = false;
        [ObservableProperty]
        private bool _isDateGroupInteractive = false;
        [ObservableProperty]
        private bool _isFullMode = true;
        [ObservableProperty]
        private bool _isNoImageMode = false;
        [ObservableProperty]
        private bool _isPlaceholderMode = false;
        [ObservableProperty]
        private bool _isExportMode = true;
        [ObservableProperty]
        private bool _isImportMode = false;
        [ObservableProperty]
        private bool _isExporting;

        // 静态忙碌计数：切换项目前由主窗口检查，正在导出/导入时阻止切换
        //（后台线程写库/读数据期间切换项目会导致旧数据服务被释放、数据写一半）
        private static int _busyCount;

        /// <summary>是否有导出/导入任务正在执行（跨实例统计）</summary>
        public static bool AnyBusy => _busyCount > 0;

        partial void OnIsExportingChanged(bool value)
        {
            // IsExporting 均在 UI 线程置位（async 方法 await 后恢复 UI 上下文），普通 int 计数安全
            if (value) _busyCount++;
            else _busyCount--;
            if (_busyCount < 0) _busyCount = 0; // 防御性兜底，防止异常路径导致计数失真

            // 同步请求/释放菜单栏"项目列表"禁用（引用计数，与 LAN 窗口共用同一机制）
            // 正在导出/导入时禁用项目菜单，从 UI 上直接堵住切换项目的三个入口
            var menuBar = (Application.Current.MainWindow as MainWindow)?.MainMenuBar;
            if (menuBar == null) return;
            if (value) menuBar.RequestProjectMenuDisable();
            else menuBar.ReleaseProjectMenuDisable();
        }

        public string CurrentFileName
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedTemplatePath))
                    return string.Empty;
                try { return System.IO.Path.GetFileName(SelectedTemplatePath); }
                catch { return string.Empty; }
            }
        }

        [ObservableProperty]
        private string _selectedYearMonthText = "请选择年月";
        private string? _selectedYearMonthGroupId;

        [ObservableProperty]
        private ObservableCollection<string> _dateGroups = new();

        [ObservableProperty]
        private string _selectedDateGroupText = "请选择日期";

        [ObservableProperty]
        private bool _isMonthlyRangeSelected = true;

        [ObservableProperty]
        private bool _isCustomRangeSelected = false;

        [ObservableProperty]
        private string _currentRangeDescription = string.Empty;

        public bool IsMonthlyRangeEnabled => IsMonthlyRangeSelected;
        public bool IsCustomRangeEnabled => IsCustomRangeSelected;

        [ObservableProperty]
        private string _customStartDateStr = "";
        [ObservableProperty]
        private string _customEndDateStr = "";
        [ObservableProperty]
        private string _startDateTag = "";
        [ObservableProperty]
        private string _endDateTag = "";
        [ObservableProperty]
        private bool _isExcelFile;
        [ObservableProperty]
        private string _excelSheetIndex = "1";

        public string StartDateToolTip
        {
            get
            {
                if (StartDateDisplay == "起始日期") return null!;
                if (string.IsNullOrEmpty(StartDateTag)) return StartDateDisplay;
                return $"{StartDateDisplay} 标签:{StartDateTag}";
            }
        }

        public string EndDateToolTip
        {
            get
            {
                if (EndDateDisplay == "结束日期") return null!;
                if (string.IsNullOrEmpty(EndDateTag)) return EndDateDisplay;
                return $"{EndDateDisplay} 标签:{EndDateTag}";
            }
        }

        public string StartDateDisplay
        {
            get
            {
                if (ParseDateFromString(CustomStartDateStr, out DateTime dt))
                    return dt.ToString("yyyy年MM月dd日");
                return "起始日期";
            }
        }

        public string EndDateDisplay
        {
            get
            {
                if (ParseDateFromString(CustomEndDateStr, out DateTime dt))
                    return dt.ToString("yyyy年MM月dd日");
                return "结束日期";
            }
        }

        [ObservableProperty]
        private ObservableCollection<string> _yearMonthGroups = new();

        private List<YearMonthGroupInfo> _cachedYearMonthGroups = new();

        #endregion

        #region 命令

        public ICommand OpenFileCommand { get; }
        public ICommand RemoveFileCommand { get; }
        public ICommand PasteFileCommand { get; }
        public ICommand ClearYearMonthCommand { get; }
        public ICommand ClearDateGroupCommand { get; }
        public ICommand ClearCustomRangeCommand { get; }
        public IAsyncRelayCommand StartExportCommand { get; }

        #endregion

        #region 构造函数

        public ExportWindowViewModel(Window ownerWindow, string moduleName = "")
        {
            _ownerWindow = ownerWindow;
            _moduleName = string.IsNullOrEmpty(moduleName) ? ModuleNames.Hazard : moduleName;
            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            RemoveFileCommand = new RelayCommand(ExecuteRemoveFile);
            PasteFileCommand = new RelayCommand(ExecutePasteFile);
            ClearYearMonthCommand = new RelayCommand(ExecuteClearYearMonth);
            ClearDateGroupCommand = new RelayCommand(ExecuteClearDateGroup, CanClearDateGroup);
            ClearCustomRangeCommand = new RelayCommand(ExecuteClearCustomRange);
            StartExportCommand = new AsyncRelayCommand(ExecuteStartExportAsync);

            LoadYearMonthGroups();
            BuildCache();
            UpdateRangeDescription();

            // 切换项目时重置下拉为默认值（防止旧项目选择残留）
            MainWindow.ProjectSwitched += OnProjectSwitched;
        }

        /// <summary>控件销毁时退订项目切换事件，避免内存泄漏</summary>
        public void Dispose()
        {
            MainWindow.ProjectSwitched -= OnProjectSwitched;
        }

        /// <summary>项目切换 → 刷新分组缓存并把所有选择重置为默认值</summary>
        private void OnProjectSwitched()
        {
            LoadYearMonthGroups();
            BuildCache();
            ExecuteClearYearMonth();
            ExecuteClearCustomRange();
            UpdateRangeDescription();
            UpdateDateGroupInteractive();
        }

        #endregion

        #region 辅助方法

        private bool ParseDateFromString(string dateStr, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrEmpty(dateStr) || dateStr.Length != 8) return false;
            if (!int.TryParse(dateStr, out int raw)) return false;
            int year = raw / 10000;
            int month = (raw / 100) % 100;
            int day = raw % 100;
            if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1) return false;
            try { date = new DateTime(year, month, day); return true; }
            catch { return false; }
        }

        private DateTime? ParseDateFromGroupName(string dateName, string yearMonthName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(dateName, @"(\d{1,2})月(\d{1,2})日");
            if (!match.Success) return null;
            var yearMonthMatch = System.Text.RegularExpressions.Regex.Match(yearMonthName, @"(\d{4})年(\d{1,2})月");
            if (!yearMonthMatch.Success) return null;
            int year = int.Parse(yearMonthMatch.Groups[1].Value);
            int month = int.Parse(yearMonthMatch.Groups[2].Value);
            int day = int.Parse(match.Groups[2].Value);
            return new DateTime(year, month, day);
        }

        private int ParseDateValue(string dateName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(dateName, @"(\d{1,2})月(\d{1,2})日");
            if (match.Success)
            {
                int month = int.Parse(match.Groups[1].Value);
                int day = int.Parse(match.Groups[2].Value);
                return month * 100 + day;
            }
            return 0;
        }

        #endregion

        #region UI 回调（partial OnChanged）

        partial void OnCustomStartDateStrChanged(string value)
        {
            OnPropertyChanged(nameof(StartDateDisplay));
            OnPropertyChanged(nameof(StartDateToolTip));
            if (string.IsNullOrEmpty(value)) StartDateTag = "";
            if (!string.IsNullOrEmpty(CustomEndDateStr) && ParseDateFromString(CustomStartDateStr, out DateTime start) &&
                ParseDateFromString(CustomEndDateStr, out DateTime end) && end < start)
            {
                CustomEndDateStr = "";
                EndDateTag = "";
                ToastService.Show("起始日期已变更，结束日期已自动清空", _ownerWindow);
                OnPropertyChanged(nameof(EndDateDisplay));
                OnPropertyChanged(nameof(EndDateToolTip));
            }
            UpdateRangeDescription();
        }

        partial void OnCustomEndDateStrChanged(string value)
        {
            OnPropertyChanged(nameof(EndDateDisplay));
            OnPropertyChanged(nameof(EndDateToolTip));
            UpdateRangeDescription();
        }

        partial void OnStartDateTagChanged(string value) => OnPropertyChanged(nameof(StartDateToolTip));
        partial void OnEndDateTagChanged(string value) => OnPropertyChanged(nameof(EndDateToolTip));

        partial void OnIsMonthlyRangeSelectedChanged(bool value)
        {
            if (!value && !IsCustomRangeSelected) { IsMonthlyRangeSelected = true; return; }
            if (value) IsCustomRangeSelected = false;
            IsMonthlyRangeInteractive = value;
            IsCustomRangeInteractive = !value;
            UpdateDateGroupInteractive();
            UpdateRangeDescription();
        }

        partial void OnIsCustomRangeSelectedChanged(bool value)
        {
            if (!value && !IsMonthlyRangeSelected) { IsCustomRangeSelected = true; return; }
            if (value) IsMonthlyRangeSelected = false;
            IsCustomRangeInteractive = value;
            IsMonthlyRangeInteractive = !value;
            UpdateDateGroupInteractive();
            UpdateRangeDescription();
        }
        /// <summary>
        /// 日期下拉框仅在月度/指定分组模式且已选择有效年月时可用
        /// </summary>
        private void UpdateDateGroupInteractive()
        {
            IsDateGroupInteractive = IsMonthlyRangeSelected && !string.IsNullOrEmpty(_selectedYearMonthGroupId);
        }


        partial void OnSelectedTemplatePathChanged(string value)
        {
            OnPropertyChanged(nameof(CurrentFileName));
            string ext = Path.GetExtension(value ?? "").ToLower();
            IsExcelFile = ext == ".xlsx";
        }

        partial void OnExcelSheetIndexChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) { ExcelSheetIndex = "1"; return; }
            if (!int.TryParse(value, out int idx) || idx < 1 || idx > 999) ExcelSheetIndex = "1";
        }

        partial void OnSelectedYearMonthTextChanged(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "请选择年月")
            {
                DateGroups.Clear();
                SelectedDateGroupText = "请选择日期";
                _selectedYearMonthGroupId = null;
                UpdateRangeDescription();
                UpdateDateGroupInteractive();
                return;
            }
            SelectedDateGroupText = "请选择日期";
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService != null)
            {
                var yearMonthGroup = dataService.GetGroups(ModuleNames.Hazard, null)
                    .FirstOrDefault(g => g.Name == value);
                if (yearMonthGroup != null)
                {
                    _selectedYearMonthGroupId = yearMonthGroup.Id;
                    LoadDateGroups(_selectedYearMonthGroupId);
                }
            }
            UpdateRangeDescription();
            UpdateDateGroupInteractive();
        }

        partial void OnSelectedDateGroupTextChanged(string value) => UpdateRangeDescription();

        partial void OnIsFullModeChanged(bool value)
        {
            if (!value && !IsNoImageMode && !IsPlaceholderMode) { IsFullMode = true; return; }
            if (value) { IsNoImageMode = false; IsPlaceholderMode = false; }
        }

        partial void OnIsNoImageModeChanged(bool value)
        {
            if (!value && !IsFullMode && !IsPlaceholderMode) { IsNoImageMode = true; return; }
            if (value) { IsFullMode = false; IsPlaceholderMode = false; }
        }

        partial void OnIsPlaceholderModeChanged(bool value)
        {
            if (!value && !IsFullMode && !IsNoImageMode) { IsPlaceholderMode = true; return; }
            if (value) { IsFullMode = false; IsNoImageMode = false; }
        }

        partial void OnIsExportModeChanged(bool value) { if (value) IsImportMode = false; OnPropertyChanged(nameof(StartActionText)); }
        partial void OnIsImportModeChanged(bool value) { if (value) IsExportMode = false; OnPropertyChanged(nameof(StartActionText)); }

        /// <summary>开始执行按钮文案：导入模式显示“开始导入”，导出模式显示“开始导出”</summary>
        public string StartActionText => IsImportMode ? "开始导入" : "开始导出";

        #endregion
    }
}
