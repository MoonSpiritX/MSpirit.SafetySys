// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MS_SafetyManagementSystem.ViewModels
{
    public partial class PersonnelStatisticsViewModel : ObservableObject
    {
        /// <summary>人员范围下拉的特殊选项：项目所有单位</summary>
        public const string AllUnitsOption = "项目所有单位";

        private readonly ProjectDataService _dataService;
        private readonly string _groupId;

        /// <summary>当前统计范围的单位 ID（null = 项目所有单位）</summary>
        private string? _currentUnitId;

        private List<PersonnelStatisticsRow> _allRows = new();

        /// <summary>当前统计范围名称（进入时单位，或切换到其他单位/项目所有单位）</summary>
        public string GroupName => _selectedUnit;

        [ObservableProperty]
        private ObservableCollection<PersonnelStatisticsRow> _rows = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string? _loadingMessage;

        [ObservableProperty]
        private ObservableCollection<string> _unitGroups = new();

        /// <summary>当前选择的人员范围（单位名 或 项目所有单位），默认进入时单位</summary>
        private string _selectedUnit = "";
        public string SelectedUnit
        {
            get => _selectedUnit;
            set
            {
                if (_selectedUnit != value)
                {
                    _selectedUnit = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GroupName));
                    _ = ReloadForScopeAsync();
                }
            }
        }

        /// <summary>过滤变化时触发，供外部（Panel）更新标题</summary>
        public event Action? FilterChanged;

        public PersonnelStatisticsViewModel(ProjectDataService dataService, string groupId, string groupName)
        {
            _dataService = dataService;
            _groupId = groupId;
            _selectedUnit = groupName;
            _currentUnitId = groupId;
            LoadUnitGroups();
        }

        // ========== 人员范围（单位选择） ==========

        /// <summary>
        /// 加载人员范围下拉：顶部"项目所有单位" + 全部单位（按分组树顺序）。
        /// 与导入导出界面的"请选择单位"下拉一致。
        /// </summary>
        private void LoadUnitGroups()
        {
            UnitGroups.Clear();
            UnitGroups.Add(AllUnitsOption);
            try
            {
                var units = _dataService.GetGroups("Personnel", null); // 已按 Order 排序（分组树顺序）
                foreach (var unit in units)
                    UnitGroups.Add(unit.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载人员范围单位列表失败");
            }
        }

        /// <summary>范围切换：更新当前单位 ID 并重新加载数据。</summary>
        private async Task ReloadForScopeAsync()
        {
            if (string.IsNullOrEmpty(_selectedUnit) || _selectedUnit == AllUnitsOption)
            {
                _currentUnitId = null; // 项目所有单位
            }
            else
            {
                // 按名称查找单位 ID（保持与下拉选项一致）
                var unit = _dataService.GetGroups("Personnel", null)
                    .FirstOrDefault(g => g.Name == _selectedUnit);
                _currentUnitId = unit?.Id;
            }
            await LoadDataAsync();
        }

        #region 过滤属性

        private string _personnelStatus = "全部人员";
        /// <summary>人员状态：全部人员 / 在场人员 / 退场人员</summary>
        public string PersonnelStatus
        {
            get => _personnelStatus;
            set
            {
                if (_personnelStatus != value)
                {
                    _personnelStatus = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        private string _category = "全部人员";
        /// <summary>类别选择：全部人员 / 三类人员 / 特种作业人员</summary>
        public string Category
        {
            get => _category;
            set
            {
                if (_category != value)
                {
                    _category = value;
                    OnPropertyChanged();
                    ApplyFilters();
                }
            }
        }

        #endregion

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            LoadingMessage = "正在加载单位人员统计数据...";
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                await Task.Run(() => LoadDataCore(dispatcher));
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = null;
            }
        }

        private void LoadDataCore(System.Windows.Threading.Dispatcher? dispatcher)
        {
            // 统一收集 + 排序（单位树顺序 → 部门树顺序 → 在场/退场/卡片顺序）
            var sourceRecords = Export.PersonnelOrderingService.SortByTree(
                Export.PersonnelOrderingService.CollectRecords(_dataService, _currentUnitId, null, _selectedUnit));
            if (sourceRecords.Count == 0)
            {
                Log.Information("PersonnelStatistics: 范围 {Scope} 下无人员记录", _selectedUnit);
                dispatcher?.Invoke(() =>
                {
                    _allRows.Clear();
                    Rows.Clear();
                });
                return;
            }

            var rows = new List<PersonnelStatisticsRow>();
            int index = 1;
            foreach (var src in sourceRecords)
            {
                var record = src.Record;
                var unitName = src.Unit;
                var department = src.Department;
                var row = new PersonnelStatisticsRow
                {
                    RecordId = record.Id,
                    GroupId = record.GroupId,
                    RowNumber = index++,
                    Title = record.Title ?? "",
                    Unit = unitName,
                    Department = department,
                    Gender = record.Gender ?? "",
                    Position = record.Position ?? "",
                    BirthDate = record.BirthDate ?? "",
                    Age = record.Age ?? "",
                    EmployeeID = record.EmployeeID ?? "",
                    NativePlace = record.NativePlace ?? "",
                    EducationLevel = record.EducationLevel ?? "",
                    PhoneNumber = record.PhoneNumber ?? "",

                    EntryTime = record.EntryTime ?? "",
                    EduTime = record.EduTime ?? "",
                    ExitTime = record.ExitTime ?? "",

                    IDCardDocNumber = record.IDCardDocNumber ?? "",
                    IDCardValidPeriod = FormatValidPeriod(record.IDCardStart, record.IDCardEnd),

                    ThreeCategoryPersonnelDisplay = record.ThreeCategoryPersonnel ? "是" : "否",
                    SpecialOperationsPersonnelDisplay = record.SpecialOperationsPersonnel ? "是" : "否",

                    Remark = record.Remark ?? "",
                };

                var certInfo = GetFirstNonEmptyCertSlot(record);
                row.CertName = certInfo.Name;
                row.CertNumber = certInfo.Number;
                row.CertValidPeriod = FormatValidPeriod(certInfo.Start, certInfo.End);

                rows.Add(row);
            }

            dispatcher?.Invoke(() =>
            {
                _allRows = rows;
                ApplyFilters();
            });
        }

        /// <summary>
        /// 根据 PersonnelStatus + Category 过滤 _allRows 并重建 Rows
        /// 过滤判定复用 PersonnelOrderingService（与导出界面一致）
        /// </summary>
        private void ApplyFilters()
        {
            var filtered = _allRows.Where(row =>
            {
                bool statusOk = Export.PersonnelOrderingService.MatchesStatus(row.ExitTime, _personnelStatus);
                bool categoryOk = Export.PersonnelOrderingService.MatchesCategory(
                    row.ThreeCategoryPersonnelDisplay, row.SpecialOperationsPersonnelDisplay, _category);
                return statusOk && categoryOk;
            });

            var list = filtered.ToList();

            // 重新编号
            for (int i = 0; i < list.Count; i++)
                list[i].RowNumber = i + 1;

            Rows.Clear();
            foreach (var row in list)
                Rows.Add(row);

            FilterChanged?.Invoke();
        }

        /// <summary>
        /// 获取当前过滤后的记录数，供标题使用
        /// </summary>
        public int FilteredCount => Rows.Count;

        private static string FormatValidPeriod(string? start, string? end)
        {
            string s = (start ?? "").Trim();
            string e = (end ?? "").Trim();
            if (string.IsNullOrEmpty(s) && string.IsNullOrEmpty(e))
                return "";
            if (e == "长期")
                return string.IsNullOrEmpty(s) ? "长期" : $"{s} 至 长期";
            return $"{s} 至 {e}";
        }

        private static (string Name, string Number, string Start, string End) GetFirstNonEmptyCertSlot(PersonnelRecord record)
        {
            var slots = new[]
            {
                (record.Cert1Name, record.Cert1DocNumber, record.Cert1Start, record.Cert1End),
                (record.Cert2Name, record.Cert2DocNumber, record.Cert2Start, record.Cert2End),
                (record.Cert3Name, record.Cert3DocNumber, record.Cert3Start, record.Cert3End),
                (record.Cert4Name, record.Cert4DocNumber, record.Cert4Start, record.Cert4End),
                (record.Cert5Name, record.Cert5DocNumber, record.Cert5Start, record.Cert5End),
            };

            foreach (var (name, number, start, end) in slots)
            {
                if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(number)
                    || !string.IsNullOrEmpty(start) || !string.IsNullOrEmpty(end))
                    return (name ?? "", number ?? "", start ?? "", end ?? "");
            }
            return ("", "", "", "");
        }
    }
}
