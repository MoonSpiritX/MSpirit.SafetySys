// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 人员模块导出/导入 ViewModel（独立于隐患模块，使用 单位 → 部门 结构）
    /// </summary>
    public partial class PersonnelExportViewModel : ObservableObject
    {
        private readonly Window _ownerWindow;

        // ========== 界面模式切换 ==========
        [ObservableProperty]
        private bool _isExportMode = true;

        [ObservableProperty]
        private bool _isImportMode = false;

        // ========== 目标文件 ==========
        [ObservableProperty]
        private string _selectedTemplatePath = string.Empty;

        [ObservableProperty]
        private bool _isExcelFile;

        [ObservableProperty]
        private string _excelSheetIndex = "1";

        public string CurrentFileName
        {
            get
            {
                if (string.IsNullOrEmpty(SelectedTemplatePath))
                    return string.Empty;
                try { return Path.GetFileName(SelectedTemplatePath); }
                catch { return string.Empty; }
            }
        }

        // ========== 单位/部门数据 ==========
        /// <summary>单位下拉框顶部的特殊选项：选中即导出项目所有单位的人员</summary>
        public const string AllUnitsOption = "项目所有单位";

        [ObservableProperty]
        private ObservableCollection<string> _unitGroups = new();

        [ObservableProperty]
        private string _selectedUnitText = "请选择单位";

        [ObservableProperty]
        private string _selectedUnitId = "";

        [ObservableProperty]
        private ObservableCollection<string> _departmentGroups = new();

        [ObservableProperty]
        private string _selectedDeptText = "请选择部门（可选）";

        [ObservableProperty]
        private string _selectedDeptId = "";

        [ObservableProperty]
        private string _currentRangeDescription = string.Empty;

        // ========== 模式选择（至少一项选中） ==========
        [ObservableProperty]
        private bool _isFullMode = true;

        [ObservableProperty]
        private bool _isNoImageMode = false;

        [ObservableProperty]
        private bool _isPlaceholderMode = false;

        // ========== 进度 ==========
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

        // ========== 人员范围选择（状态/类别，与统计界面一致） ==========

        /// <summary>人员状态下拉选项</summary>
        public string[] PersonnelStatusOptions { get; } = { "全部人员", "在场人员", "退场人员" };

        /// <summary>人员类别下拉选项</summary>
        public string[] PersonnelCategoryOptions { get; } = { "全部人员", "三类人员", "特种作业人员" };

        [ObservableProperty]
        private string _personnelStatus = "全部人员";

        [ObservableProperty]
        private string _personnelCategory = "全部人员";

        // 单位下拉是否可用（导出模式恒可用：全项目通过"项目所有单位"选项实现；
        // 导入模式仅在"导入到指定单位/部门分组"选中时可用，自动分配模式下禁用）
        public bool IsRangeSelectionEnabled => !IsImportMode || IsTargetGroupSelected;

        /// <summary>
        /// 部门下拉是否可用：仅当选中了具体的单位（SelectedUnitId 非空）时才启用。
        /// 导入模式自动分配时禁用。未选单位、清空、选中"项目所有单位"（ID 为空）时均禁用。
        /// </summary>
        public bool IsDeptSelectionEnabled => !string.IsNullOrEmpty(SelectedUnitId) && (!IsImportMode || IsTargetGroupSelected);

        // ========== 导入模式：目标分组 vs 自动分配（互斥，对齐隐患模块） ==========

        /// <summary>导入到指定单位/部门分组（选中时启用单位/部门下拉）</summary>
        [ObservableProperty]
        private bool _isTargetGroupSelected = true;

        /// <summary>按文件中单位字段自动分配（自动创建不存在的单位/部门）</summary>
        [ObservableProperty]
        private bool _isAutoAssignSelected = false;

        partial void OnIsTargetGroupSelectedChanged(bool value)
        {
            // 互斥：保证至少一项选中；选中指定分组则取消自动分配
            if (!value && !IsAutoAssignSelected) { IsTargetGroupSelected = true; return; }
            if (value) IsAutoAssignSelected = false;
            OnPropertyChanged(nameof(IsRangeSelectionEnabled));
            OnPropertyChanged(nameof(IsDeptSelectionEnabled));
        }

        partial void OnIsAutoAssignSelectedChanged(bool value)
        {
            // 互斥：保证至少一项选中；选中自动分配则取消指定分组
            if (!value && !IsTargetGroupSelected) { IsAutoAssignSelected = true; return; }
            if (value) IsTargetGroupSelected = false;
            OnPropertyChanged(nameof(IsRangeSelectionEnabled));
            OnPropertyChanged(nameof(IsDeptSelectionEnabled));
        }

        // ========== 命令 ==========
        public ICommand OpenFileCommand { get; }
        public ICommand RemoveFileCommand { get; }
        public ICommand PasteFileCommand { get; }
        public ICommand ClearUnitSelectionCommand { get; }
        public ICommand ClearDeptSelectionCommand { get; }
        public IAsyncRelayCommand StartExportCommand { get; }

        public PersonnelExportViewModel(Window ownerWindow)
        {
            _ownerWindow = ownerWindow;

            OpenFileCommand = new RelayCommand(ExecuteOpenFile);
            RemoveFileCommand = new RelayCommand(ExecuteRemoveFile);
            PasteFileCommand = new RelayCommand(ExecutePasteFile);
            ClearUnitSelectionCommand = new RelayCommand(ExecuteClearUnitSelection);
            ClearDeptSelectionCommand = new RelayCommand(ExecuteClearDeptSelection);
            StartExportCommand = new AsyncRelayCommand(ExecuteStartExportAsync);

            LoadUnitGroups();

            // 切换项目时重置下拉为默认值（防止旧项目选择残留）
            MainWindow.ProjectSwitched += OnProjectSwitched;
        }

        /// <summary>控件销毁时退订项目切换事件，避免内存泄漏</summary>
        public void Dispose()
        {
            MainWindow.ProjectSwitched -= OnProjectSwitched;
        }

        /// <summary>项目切换 → 刷新单位列表并把所有下拉重置为默认值</summary>
        private void OnProjectSwitched()
        {
            LoadUnitGroups();
            ResetSelectionsToDefault();
        }

        /// <summary>把所有下拉选择重置为默认值（单位/部门/状态/类别）</summary>
        private void ResetSelectionsToDefault()
        {
            SelectedUnitText = "请选择单位";
            SelectedUnitId = "";
            DepartmentGroups.Clear();
            SelectedDeptText = "请选择部门（可选）";
            SelectedDeptId = "";
            PersonnelStatus = "全部人员";
            PersonnelCategory = "全部人员";
            UpdateRangeDescription();
        }

        // ========== 加载单位列表 ==========
        private void LoadUnitGroups()
        {
            UnitGroups.Clear();
            // 顶部插入特殊选项：项目所有单位（选中后部门下拉禁用，导出全项目人员）
            UnitGroups.Add(AllUnitsOption);
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var dataService = mainWindow?.ViewModel.GetProjectDataService();
                if (dataService == null) return;

                // 单位列表保持分组树顺序（GetGroups 已按 Order 排序，勿再按名称重排）
                var units = dataService.GetGroups(ModuleNames.Personnel, null)
                    .ToList();

                foreach (var unit in units)
                    UnitGroups.Add(unit.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载人员单位列表失败");
            }
        }

        /// <summary>
        /// 执行导出/导入前（或单位下拉点开前）刷新单位列表并校验当前选择。
        /// 背景：单位列表在界面打开时加载一次；若界面打开期间切换了项目，
        /// 下拉仍是旧项目的单位，而数据源已变为新项目 → 列表与数据不一致。
        /// 这里重新加载，并把不再存在的单位选择重置为"项目所有单位"。
        /// 注意：同名单位/部门在不同项目的 ID 不同，不能只看名称存在就保留，必须重查 ID。
        /// </summary>
        public void RefreshUnitsForCurrentProject()
        {
            string previous = SelectedUnitText;
            LoadUnitGroups();

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();

            // 校验原选择是否仍有效（项目所有单位始终有效）
            if (previous == AllUnitsOption || string.IsNullOrEmpty(previous))
            {
                // 全项目/未选：部门选择可能来自旧项目，清空重选
                DepartmentGroups.Clear();
                SelectedDeptText = "请选择部门（可选）";
                SelectedDeptId = "";
                UpdateRangeDescription();
                return;
            }

            // 按名称在当前项目重新查找单位：同名也要更新 ID（不同项目同名单位 ID 不同）
            var unit = dataService?.GetGroups(ModuleNames.Personnel, null)
                .FirstOrDefault(g => g.Name == previous);
            if (unit != null)
            {
                SelectedUnitId = unit.Id;
                UpdateRangeDescription();
                return; // 单位仍有效，保留选择；部门由 RefreshDepartmentsForCurrentProject 点开时重查
            }

            // 单位不存在 → 重置为"项目所有单位"
            SelectedUnitText = AllUnitsOption;
            SelectedUnitId = "";
            DepartmentGroups.Clear();
            SelectedDeptText = "请选择部门（可选）";
            SelectedDeptId = "";
            UpdateRangeDescription();
            Log.Information("项目切换后单位选择已重置：{Previous} → 项目所有单位", previous);
        }

        /// <summary>
        /// 部门下拉点开前刷新：部门列表可能来自旧项目的单位，需按当前项目的单位重查。
        /// </summary>
        public void RefreshDepartmentsForCurrentProject()
        {
            // 单位未选或"项目所有单位"：部门无意义，直接清空（沿用现有交互提示）
            if (string.IsNullOrEmpty(SelectedUnitId) || SelectedUnitText == AllUnitsOption)
            {
                DepartmentGroups.Clear();
                SelectedDeptText = "请选择部门（可选）";
                SelectedDeptId = "";
                return;
            }

            // 重查当前项目下该单位的部门列表（若单位 ID 已失效，LoadDepartmentGroups 内部会清空）
            LoadDepartmentGroups(SelectedUnitId);
        }

        // ========== 加载部门列表 ==========
        private void LoadDepartmentGroups(string unitId)
        {
            DepartmentGroups.Clear();
            SelectedDeptText = "请选择部门（可选）";
            SelectedDeptId = "";
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var dataService = mainWindow?.ViewModel.GetProjectDataService();
                if (dataService == null) return;

                // 部门列表保持分组树顺序（GetGroups 已按 Order 排序，勿再按名称重排）
                var depts = dataService.GetGroups(ModuleNames.Personnel, unitId)
                    .ToList();

                foreach (var dept in depts)
                    DepartmentGroups.Add(dept.Name);

                if (depts.Count > 0)
                    SelectedDeptText = "请选择部门（可选）";
                else
                    SelectedDeptText = "该单位下无部门";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载人员部门列表失败");
            }
        }

        // ========== 选择变更 ==========
        partial void OnSelectedUnitTextChanged(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "请选择单位")
            {
                SelectedUnitId = "";
                DepartmentGroups.Clear();
                SelectedDeptText = "请选择部门（可选）";
                SelectedDeptId = "";
                OnPropertyChanged(nameof(IsDeptSelectionEnabled));
                UpdateRangeDescription();
                return;
            }

            // 项目所有单位：全项目导出/导入，部门下拉禁用
            if (value == AllUnitsOption)
            {
                SelectedUnitId = "";
                DepartmentGroups.Clear();
                SelectedDeptText = "请选择部门（可选）";
                SelectedDeptId = "";
                OnPropertyChanged(nameof(IsDeptSelectionEnabled));
                UpdateRangeDescription();
                return;
            }

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null) return;

            var unit = dataService.GetGroups(ModuleNames.Personnel, null)
                .FirstOrDefault(g => g.Name == value);
            if (unit != null)
            {
                SelectedUnitId = unit.Id;
                LoadDepartmentGroups(unit.Id);
            }

            OnPropertyChanged(nameof(IsDeptSelectionEnabled));
            UpdateRangeDescription();
        }

        partial void OnSelectedDeptTextChanged(string value)
        {
            if (string.IsNullOrEmpty(value) || value.StartsWith("请选择") || value.StartsWith("该单位"))
            {
                SelectedDeptId = "";
                UpdateRangeDescription();
                return;
            }

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null) return;

            // 从当前已加载的单位下查找部门
            var dept = dataService.GetGroups(ModuleNames.Personnel, SelectedUnitId)
                .FirstOrDefault(g => g.Name == value);
            if (dept != null)
                SelectedDeptId = dept.Id;

            UpdateRangeDescription();
        }

        // ========== 范围描述 ==========
        private void UpdateRangeDescription()
        {
            // 项目所有单位
            if (SelectedUnitText == AllUnitsOption)
            {
                CurrentRangeDescription = "当前数据范围：项目所有单位";
                return;
            }

            if (string.IsNullOrEmpty(SelectedUnitText) || SelectedUnitText == "请选择单位")
            {
                CurrentRangeDescription = "";
                return;
            }

            string desc = SelectedUnitText;
            if (!string.IsNullOrEmpty(SelectedDeptId) &&
                !string.IsNullOrEmpty(SelectedDeptText) &&
                !SelectedDeptText.StartsWith("请选择") &&
                !SelectedDeptText.StartsWith("该单位"))
            {
                desc += " → " + SelectedDeptText;
            }
            CurrentRangeDescription = $"当前数据范围：{desc}";
        }

        // ========== 清空选择 ==========
        private void ExecuteClearUnitSelection()
        {
            SelectedUnitText = "请选择单位";
            SelectedUnitId = "";
            DepartmentGroups.Clear();
            SelectedDeptText = "请选择部门（可选）";
            SelectedDeptId = "";
            OnPropertyChanged(nameof(IsDeptSelectionEnabled));
            UpdateRangeDescription();
        }

        private void ExecuteClearDeptSelection()
        {
            SelectedDeptText = "请选择部门（可选）";
            SelectedDeptId = "";
            UpdateRangeDescription();
        }

        // ========== 文件操作 ==========
        private void ExecuteOpenFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择导出模板文件",
                Filter = "模板文件|*.xlsx;*.docx|Excel 文件|*.xlsx|Word 文件|*.docx",
                FilterIndex = 1,
                RestoreDirectory = true,
                InitialDirectory = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"
            };
            if (dialog.ShowDialog() == true)
                SelectedTemplatePath = dialog.FileName;
        }

        private void ExecuteRemoveFile() => SelectedTemplatePath = string.Empty;

        private void ExecutePasteFile()
        {
            var allowedExtensions = new[] { ".xlsx", ".docx" };

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0)
                {
                    string filePath = files[0]!;
                    string ext = Path.GetExtension(filePath).ToLower();
                    if (allowedExtensions.Contains(ext))
                    {
                        SelectedTemplatePath = filePath;
                        return;
                    }
                    MessageDialog.Show(_ownerWindow, "文件类型不支持。\n目标文件类型仅限于 .xlsx 和 .docx 格式", "提示");
                    return;
                }
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                text = text.Trim('"', '\'');
                if ((text.Contains(":\\") || text.StartsWith("\\\\")))
                {
                    string ext = Path.GetExtension(text).ToLower();
                    if (allowedExtensions.Contains(ext))
                    {
                        SelectedTemplatePath = text;
                        return;
                    }
                    MessageDialog.Show(_ownerWindow, "文件类型不支持。\n目标文件类型仅限于 .xlsx 和 .docx 格式", "提示");
                    return;
                }
            }

            MessageDialog.Show(_ownerWindow, "剪贴板中没有有效的文件路径。\n请先复制文件或文件路径。", "提示");
        }

        partial void OnSelectedTemplatePathChanged(string value)
        {
            OnPropertyChanged(nameof(CurrentFileName));
            string ext = Path.GetExtension(value ?? "").ToLower();
            IsExcelFile = ext == ".xlsx";
        }

        partial void OnExcelSheetIndexChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ExcelSheetIndex = "1";
                return;
            }
            if (!int.TryParse(value, out int idx) || idx < 1 || idx > 999)
                ExcelSheetIndex = "1";
        }

        // ========== 模式切换 ==========

        /// <summary>开始执行按钮文案：导入模式显示“开始导入”，导出模式显示“开始导出”</summary>
        public string StartActionText => IsImportMode ? "开始导入" : "开始导出";

        partial void OnIsExportModeChanged(bool value)
        {
            if (value) IsImportMode = false;
            OnPropertyChanged(nameof(StartActionText));
        }

        partial void OnIsImportModeChanged(bool value)
        {
            if (value) IsExportMode = false;
            OnPropertyChanged(nameof(StartActionText));
            // 模式切换时刷新单位/部门下拉可用性（导入自动分配模式下禁用）
            OnPropertyChanged(nameof(IsRangeSelectionEnabled));
            OnPropertyChanged(nameof(IsDeptSelectionEnabled));
        }

        partial void OnIsFullModeChanged(bool value)
        {
            if (!value && !IsNoImageMode && !IsPlaceholderMode)
            {
                IsFullMode = true;
                return;
            }
            if (value)
            {
                IsNoImageMode = false;
                IsPlaceholderMode = false;
            }
        }

        partial void OnIsNoImageModeChanged(bool value)
        {
            if (!value && !IsFullMode && !IsPlaceholderMode)
            {
                IsNoImageMode = true;
                return;
            }
            if (value)
            {
                IsFullMode = false;
                IsPlaceholderMode = false;
            }
        }

        partial void OnIsPlaceholderModeChanged(bool value)
        {
            if (!value && !IsFullMode && !IsNoImageMode)
            {
                IsPlaceholderMode = true;
                return;
            }
            if (value)
            {
                IsFullMode = false;
                IsNoImageMode = false;
            }
        }

        // ========== 导出/导入入口 ==========
        private async Task ExecuteStartExportAsync()
        {
            if (IsExporting) return;

            // 界面打开期间可能切换过项目：刷新单位列表并校验选择，避免"旧单位列表 + 新项目数据"
            RefreshUnitsForCurrentProject();

            if (IsImportMode)
            {
                await ExecuteStartImportAsync();
                return;
            }

            // 导出模式
            if (IsFullMode)
                await ExecuteExportWithModeAsync("Full");
            else if (IsNoImageMode)
                await ExecuteExportWithModeAsync("NoImage");
            else if (IsPlaceholderMode)
                await ExecuteExportWithModeAsync("OnlyImage");
            else
                MessageDialog.Show(_ownerWindow, "请先选择导出模式", "提示");
        }

        // ========== 导出 ==========
        private async Task ExecuteExportWithModeAsync(string mode)
        {
            var dataRows = GetPersonnelRowsForExport();
            if (dataRows == null || dataRows.Count == 0)
            {
                MessageDialog.Show(_ownerWindow, "没有符合条件的数据", "提示");
                return;
            }

            string templatePath = SelectedTemplatePath;
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                MessageDialog.Show(_ownerWindow, "请先选择有效的模板文件", "提示");
                return;
            }

            string ext = Path.GetExtension(templatePath).ToLower();
            if (ext != ".docx" && ext != ".xlsx")
            {
                MessageDialog.Show(_ownerWindow, "不支持的文件格式", "提示");
                return;
            }

            string contextName = SelectedUnitText == AllUnitsOption
                ? "项目所有单位"
                : SelectedUnitText;

            if (SelectedUnitText != AllUnitsOption &&
                !string.IsNullOrEmpty(SelectedDeptId) &&
                !string.IsNullOrEmpty(SelectedDeptText) &&
                !SelectedDeptText.StartsWith("请选择") &&
                !SelectedDeptText.StartsWith("该单位"))
            {
                contextName += "-" + SelectedDeptText;
            }

            var dataPathService = new DataPathService();
            var mappingService = new MappingDictionaryService(dataPathService.RootPath);
            var provider = new PersonnelExportDataProvider(dataRows, contextName);

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var projectDataService = mainWindow?.ViewModel.GetProjectDataService();

            string outputFileName = Path.GetFileNameWithoutExtension(templatePath) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ext;
            string outputPath = Path.Combine(Path.GetDirectoryName(templatePath) ?? "", outputFileName);

            IsExporting = true;

            try
            {
                (bool success, string? errorMessage) result2;

                if (ext == ".docx")
                {
                    string attachmentsRoot = GetAttachmentsRootPath();
                    var exporter = new WordSmartExporter(mappingService, attachmentsRoot, "Personnel");
                    result2 = await Task.Run(() => exporter.Export(templatePath, outputPath, provider, mode));
                }
                else
                {
                    string sheetName = ExcelSheetIndex;
                    if (string.IsNullOrWhiteSpace(sheetName)) sheetName = "1";
                    string attachmentsRoot = GetAttachmentsRootPath();
                    var exporter = new ExcelSmartExporter(mappingService, attachmentsRoot, "Personnel");
                    result2 = await Task.Run(() => exporter.Export(templatePath, outputPath, provider, mode, sheetName));
                }

                IsExporting = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (result2.success)
                        MessageDialog.Show(_ownerWindow, $"导出成功！文件已保存至：{outputPath}", "完成");
                    else
                        MessageDialog.Show(_ownerWindow, $"导出失败：{result2.errorMessage}", "错误");
                });
            }
            catch (Exception ex)
            {
                IsExporting = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageDialog.Show(_ownerWindow, $"导出异常：{ex.Message}", "错误"));
            }
        }

        // ========== 导入 ==========
        private async Task ExecuteStartImportAsync()
        {
            string filePath = SelectedTemplatePath;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                MessageDialog.Show(_ownerWindow, "请先选择有效的源文件", "提示");
                return;
            }

            // 确定目标部门/单位（2026-08-10 改为互斥选项驱动：指定分组 vs 按文件自动分配）
            string targetGroupId = "";
            bool autoAssignByDate = IsAutoAssignSelected;

            if (!autoAssignByDate)
            {
                // 导入到指定单位/部门分组：按所选单位/部门定向
                if (!string.IsNullOrEmpty(SelectedUnitId) && SelectedUnitText != "请选择单位")
                {
                    if (!string.IsNullOrEmpty(SelectedDeptId) &&
                        !SelectedDeptText.StartsWith("请选择") &&
                        !SelectedDeptText.StartsWith("该单位"))
                    {
                        // 已选单位 + 已选部门 → 定向到部门
                        targetGroupId = SelectedDeptId;
                    }
                    else
                    {
                        // 已选单位 + 未选部门 → 定向到单位根级（writer 内会按行部门字段分配到该单位下）
                        targetGroupId = SelectedUnitId;
                    }
                }
                else
                {
                    // 勾选了"指定分组"但未选单位 → 提示（互斥选项保证只有一种语义，这里做防御）
                    MessageDialog.Show(_ownerWindow, "已选择「导入到指定单位/部门分组」，请先选择单位", "提示");
                    return;
                }
            }

            var dataPathService = new DataPathService();
            var mappingService = new MappingDictionaryService(dataPathService.RootPath);
            var manager = new ImportManager(mappingService, "Personnel");
            var writer = new PersonnelDataWriter();

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show(_ownerWindow, "无法获取项目数据服务", "错误");
                return;
            }

            // 解析文件
            IsExporting = true;
            ImportParseResult? parseResult = null;

            try
            {
                int sheetIndex = 1;
                if (int.TryParse(ExcelSheetIndex, out int si) && si > 0)
                    sheetIndex = si;

                parseResult = await Task.Run(() => manager.ParseFile(filePath, sheetIndex));
                if (parseResult == null || parseResult.TotalRowCount == 0)
                {
                    IsExporting = false;
                    MessageDialog.Show(_ownerWindow, "未能从文件中解析出有效数据", "提示");
                    return;
                }

                // 校验（传 false 跳过隐患模块的日期校验，人员模块的部门分配由 PersonnelDataWriter 自行兜底）
                parseResult = manager.Validate(parseResult, false);

                if (parseResult.ValidRowCount == 0)
                {
                    IsExporting = false;
                    string warnMsg = "所有行均无效";
                    if (parseResult.Warnings.Count > 0)
                        warnMsg += "\n" + string.Join("\n", parseResult.Warnings.Take(5));
                    MessageDialog.Show(_ownerWindow, warnMsg, "校验失败");
                    return;
                }

                // 构建预览消息
                string targetDesc = "";
                if (!autoAssignByDate)
                {
                    targetDesc = SelectedUnitText;
                    if (!string.IsNullOrEmpty(SelectedDeptText) && !SelectedDeptText.StartsWith("请选择") && !SelectedDeptText.StartsWith("该单位"))
                        targetDesc += " → " + SelectedDeptText;
                }
                else
                {
                    targetDesc = "按文件中单位字段自动匹配";
                }
                string preview = manager.BuildPreviewMessage(parseResult, autoAssignByDate, targetDesc);

                IsExporting = false;

                // 用户确认弹窗
                bool confirmed = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new ConfirmDialog(preview, "数据导入预览");
                    dialog.Owner = _ownerWindow;
                    return dialog.ShowDialog() == true;
                });

                if (!confirmed) return;

                // 执行导入
                IsExporting = true;

                var resultStats = await Task.Run(() => manager.Import(
                    parseResult,
                    writer,
                    dataService,
                    "",
                    targetGroupId,
                    autoAssignByDate));

                IsExporting = false;

                string resultMsg = $"导入完成！\n成功：{resultStats.success} 条\n失败：{resultStats.failed} 条";
                if (resultStats.errors.Count > 0)
                {
                    resultMsg += "\n\n错误详情：\n" + string.Join("\n", resultStats.errors.Take(10));
                    if (resultStats.errors.Count > 10)
                        resultMsg += $"\n... 及其他 {resultStats.errors.Count - 10} 条";
                }
                MessageDialog.Show(_ownerWindow, resultMsg, "导入结果");

                // 刷新主窗口分组树，使新创建的单位/部门立即可见
                if (resultStats.success > 0)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var win = Application.Current.MainWindow as MainWindow;
                        win?.ViewModel.GroupTree.LoadGroups();
                    });
                }
            }
            catch (Exception ex)
            {
                IsExporting = false;
                Log.Error(ex, "人员导入过程发生异常");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageDialog.Show(_ownerWindow, $"导入异常：{ex.Message}", "错误"));
            }
        }

        // ========== 数据收集 ==========
        private List<PersonnelStatisticsRow> GetPersonnelRowsForExport()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel?.GetProjectDataService();
            if (dataService == null) return new List<PersonnelStatisticsRow>();

            // ── 项目所有单位（单位下拉选择"项目所有单位"）──
            if (SelectedUnitText == AllUnitsOption)
            {
                return BuildRowsFromSources(
                    FilterAndSort(
                        PersonnelOrderingService.CollectRecords(dataService, null, null, "")),
                    dataService);
            }

            // ── 按单位/部门范围导出 ──
            if (string.IsNullOrEmpty(SelectedUnitId))
                return new List<PersonnelStatisticsRow>();

            // 收集：选中部门 → 仅该部门（含子分组）；否则整个单位。统一按树顺序排序（与统计界面一致）
            string? deptId = !string.IsNullOrEmpty(SelectedDeptId) &&
                             !SelectedDeptText.StartsWith("请选择") &&
                             !SelectedDeptText.StartsWith("该单位")
                ? SelectedDeptId : null;
            var sources = PersonnelOrderingService.CollectRecords(dataService, SelectedUnitId, deptId, SelectedUnitText);
            return BuildRowsFromSources(FilterAndSort(sources), dataService);
        }

        /// <summary>统一过滤（状态/类别）→ 排序（树顺序 + 部门内），与统计界面一致。</summary>
        private List<PersonnelOrderingService.SourceRecord> FilterAndSort(
            IEnumerable<PersonnelOrderingService.SourceRecord> sources)
        {
            var filtered = PersonnelOrderingService.FilterByCategory(
                PersonnelOrderingService.FilterByStatus(sources, PersonnelStatus), PersonnelCategory);
            return PersonnelOrderingService.SortByTree(filtered);
        }

        /// <summary>把统一排序后的源记录转成导出行（单位/部门名取源记录，与树一致）。</summary>
        private List<PersonnelStatisticsRow> BuildRowsFromSources(
            List<PersonnelOrderingService.SourceRecord> sources, ProjectDataService dataService)
        {
            var rows = new List<PersonnelStatisticsRow>();
            int index = 1;
            foreach (var src in sources)
            {
                rows.Add(BuildPersonnelRow(src.Record, index++, src.Unit, src.Department, dataService));
            }
            return rows;
        }

        /// <summary>
        /// 获取项目所有单位和部门的所有人员数据（"项目所有单位"模式）
        /// </summary>
        private List<PersonnelStatisticsRow> GetAllProjectPersonnelRows(ProjectDataService dataService)
        {
            return BuildRowsFromSources(
                FilterAndSort(
                    PersonnelOrderingService.CollectRecords(dataService, null, null, "")),
                dataService);
        }

        private PersonnelStatisticsRow BuildPersonnelRow(PersonnelRecord record, int rowNumber, string department, ProjectDataService dataService)
        {
            return PersonnelHelper.BuildPersonnelRow(record, rowNumber, SelectedUnitText, department, dataService);
        }

        private PersonnelStatisticsRow BuildPersonnelRow(PersonnelRecord record, int rowNumber, string unit, string department, ProjectDataService dataService)
        {
            return PersonnelHelper.BuildPersonnelRow(record, rowNumber, unit, department, dataService);
        }

        private string GetAttachmentsRootPath()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel?.GetProjectDataService();
            if (dataService == null)
            {
                Log.Error("GetAttachmentsRootPath: 无法获取 ProjectDataService");
                return "";
            }
            try { return dataService.GetProjectsDateRoot(); }
            catch (Exception ex)
            {
                Log.Error(ex, "GetAttachmentsRootPath: 获取路径失败");
                return "";
            }
        }
    }
}
