// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace MS_SafetyManagementSystem.Export
{
    public partial class ExportWindowViewModel : ObservableObject
    {
        #region 缓存构建

        private void BuildCache()
        {
            _cachedYearMonthGroups.Clear();
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null) return;

            string module = IsPersonnel ? ModuleNames.Personnel : ModuleNames.Hazard;

            var yearMonthGroups = dataService.GetGroups(module, null)
                .OrderBy(g => g.Name);
            foreach (var ymGroup in yearMonthGroups)
            {
                var yearMonthInfo = new YearMonthGroupInfo
                {
                    Id = ymGroup.Id,
                    Name = ymGroup.Name
                };
                var dateGroups = dataService.GetGroups(module, ymGroup.Id)
                    .OrderBy(g => g.Name);
                foreach (var dg in dateGroups)
                {
                    bool hasRecords = IsPersonnel
                        ? dataService.GetPersonnelRecordsByGroup(dg.Id).Count > 0
                        : dataService.GetRecordsByGroup(dg.Id).Count > 0;
                    yearMonthInfo.DateGroups.Add(new DateGroupInfo
                    {
                        Id = dg.Id,
                        Name = dg.Name,
                        Tag = dg.Tag,
                        HasRecords = hasRecords
                    });
                }
                _cachedYearMonthGroups.Add(yearMonthInfo);
            }
        }

        /// <summary>
        /// 刷新年月/日期分组缓存并重载下拉数据源。
        /// 背景：界面打开时构建一次缓存；若界面打开期间切换了项目，下拉仍是旧项目数据。
        /// 年月/日期下拉每次点开前调用，保证列表与当前项目一致。
        /// 校验已选年月/日期是否仍有效（跨项目同名年月 ID 不同），失效则重置。
        /// 注意：SelectedYearMonthText 的 setter 会实时按名称重查 ID，重置时利用该逻辑。
        /// </summary>
        public void RefreshGroupsForCurrentProject()
        {
            string previousYearMonth = SelectedYearMonthText;
            string previousDateGroup = SelectedDateGroupText;

            LoadYearMonthGroups();
            BuildCache();
            UpdateRangeDescription();

            // 校验年月选择是否仍有效（"请选择年月"/空 视为未选）
            bool hasYearMonth = !string.IsNullOrEmpty(previousYearMonth) && previousYearMonth != "请选择年月";
            if (hasYearMonth && !_cachedYearMonthGroups.Any(ym => ym.Name == previousYearMonth))
            {
                // 年月已失效：重置年月（setter 会清日期），并刷新下拉交互状态
                SelectedYearMonthText = "请选择年月";
                UpdateDateGroupInteractive();
                Log.Information("项目切换后年月选择已重置：{Previous}", previousYearMonth);
            }
            else if (hasYearMonth && !string.IsNullOrEmpty(previousDateGroup) && previousDateGroup != "请选择日期")
            {
                // 年月仍有效：校验日期是否仍存在
                var yearMonth = _cachedYearMonthGroups.FirstOrDefault(ym => ym.Name == previousYearMonth);
                if (yearMonth == null || !yearMonth.DateGroups.Any(dg => dg.Name == previousDateGroup))
                {
                    SelectedDateGroupText = "请选择日期";
                    UpdateDateGroupInteractive();
                }
            }
        }

        #endregion

        #region 年月/日期分组查询

        public List<YearMonthGroupInfo> GetFilteredYearMonthGroupsForCustomRange()
        {
            return _cachedYearMonthGroups
                .Where(ym => ym.DateGroups.Any(dg => dg.HasRecords))
                .ToList();
        }

        public List<string> GetFilteredYearMonthNamesForMenu()
        {
            // 导入模式：显示全部年月分组（允许向空分组导入数据）；导出模式：仅显示包含有效日期的分组
            if (IsImportMode)
                return _cachedYearMonthGroups.Select(ym => ym.Name).ToList();
            return _cachedYearMonthGroups
                .Where(ym => ym.DateGroups.Any(dg => dg.HasRecords))
                .Select(ym => ym.Name)
                .ToList();
        }

        public List<DateGroupInfo> GetValidDateGroupsForYearMonth(string yearMonthName)
        {
            var yearMonth = _cachedYearMonthGroups.FirstOrDefault(ym => ym.Name == yearMonthName);
            if (yearMonth == null) return new List<DateGroupInfo>();
            // 导入模式：显示该年月下全部分组（允许向空分组导入数据）；导出模式：仅显示包含记录的分组
            if (IsImportMode)
                return yearMonth.DateGroups.ToList();
            return yearMonth.DateGroups.Where(dg => dg.HasRecords).ToList();
        }

        public List<YearMonthGroupInfo> GetFilteredYearMonthsForEndDate()
        {
            if (string.IsNullOrEmpty(CustomStartDateStr) || !ParseDateFromString(CustomStartDateStr, out DateTime startDate))
                return GetFilteredYearMonthGroupsForCustomRange();

            int startYear = startDate.Year;
            int startMonth = startDate.Month;

            return _cachedYearMonthGroups
                .Where(ym =>
                {
                    if (!ym.DateGroups.Any(dg => dg.HasRecords)) return false;
                    var match = System.Text.RegularExpressions.Regex.Match(ym.Name, @"(\d{4})年(\d{1,2})月");
                    if (!match.Success) return false;
                    int year = int.Parse(match.Groups[1].Value);
                    int month = int.Parse(match.Groups[2].Value);
                    if (year > startYear) return true;
                    if (year == startYear && month >= startMonth) return true;
                    return false;
                })
                .ToList();
        }

        public List<DateGroupInfo> GetFilteredDateGroupsForEndDate(string yearMonthName, string startDateStr)
        {
            if (!ParseDateFromString(startDateStr, out DateTime startDate))
                return GetValidDateGroupsForYearMonth(yearMonthName);

            var allDateGroups = GetValidDateGroupsForYearMonth(yearMonthName);
            var yearMonth = _cachedYearMonthGroups.FirstOrDefault(ym => ym.Name == yearMonthName);
            if (yearMonth == null) return new List<DateGroupInfo>();

            var yearMonthMatch = System.Text.RegularExpressions.Regex.Match(yearMonthName, @"(\d{4})年(\d{1,2})月");
            if (!yearMonthMatch.Success) return allDateGroups;
            int year = int.Parse(yearMonthMatch.Groups[1].Value);
            int month = int.Parse(yearMonthMatch.Groups[2].Value);

            if (year > startDate.Year || (year == startDate.Year && month > startDate.Month))
                return allDateGroups;
            if (year == startDate.Year && month == startDate.Month)
            {
                return allDateGroups.Where(dg =>
                {
                    var dayMatch = System.Text.RegularExpressions.Regex.Match(dg.Name, @"(\d{1,2})月(\d{1,2})日");
                    if (dayMatch.Success)
                    {
                        int day = int.Parse(dayMatch.Groups[2].Value);
                        return day >= startDate.Day;
                    }
                    return false;
                }).ToList();
            }
            return allDateGroups;
        }

        #endregion

        #region 加载年月分组

        private void LoadYearMonthGroups()
        {
            YearMonthGroups.Clear();
            try
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var dataService = mainWindow?.ViewModel.GetProjectDataService();
                if (dataService == null) return;

                if (IsPersonnel)
                {
                    var units = dataService.GetGroups(ModuleNames.Personnel, null);
                    var sorted = units.OrderBy(g => g.Name).Select(g => g.Name);
                    foreach (var name in sorted)
                        YearMonthGroups.Add(name);
                }
                else
                {
                    if (mainWindow?.ViewModel.GetCurrentModuleKey() != ModuleNames.Hazard)
                        return;
                    var groups = dataService.GetGroups(ModuleNames.Hazard, null);
                    var sorted = groups.OrderBy(g => g.Name).Select(g => g.Name);
                    foreach (var name in sorted)
                        YearMonthGroups.Add(name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载年月分组失败: {ex.Message}");
            }
        }

        private void LoadDateGroups(string yearMonthGroupId)
        {
            DateGroups.Clear();
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null) return;

            string module = IsPersonnel ? ModuleNames.Personnel : ModuleNames.Hazard;
            var dateGroups = dataService.GetGroups(module, yearMonthGroupId);
            foreach (var grp in dateGroups)
            {
                string display = grp.Name;
                if (!string.IsNullOrEmpty(grp.Tag))
                    display += $" (标签:{grp.Tag})";
                DateGroups.Add(display);
            }
        }

        private void UpdateRangeDescription()
        {
            if (IsMonthlyRangeSelected)
            {
                if (string.IsNullOrEmpty(SelectedYearMonthText) || SelectedYearMonthText == "请选择年月")
                {
                    CurrentRangeDescription = "";
                    return;
                }
                string yearMonth = SelectedYearMonthText;
                if (string.IsNullOrEmpty(SelectedDateGroupText) || SelectedDateGroupText == "请选择日期")
                    CurrentRangeDescription = $"当前数据范围：{yearMonth}";
                else
                {
                    string datePart = SelectedDateGroupText.Split(' ')[0];
                    string dayOnly = datePart.Contains('月') ? datePart.Substring(datePart.IndexOf('月') + 1) : datePart;
                    CurrentRangeDescription = $"当前数据范围：{yearMonth}{dayOnly}";
                }
            }
            else if (IsCustomRangeSelected)
            {
                bool startValid = ParseDateFromString(CustomStartDateStr, out DateTime start);
                bool endValid = ParseDateFromString(CustomEndDateStr, out DateTime end);
                if (startValid && endValid)
                    CurrentRangeDescription = $"当前数据范围：{start:yyyy年MM月dd日} 至 {end:yyyy年MM月dd日}";
                else
                    CurrentRangeDescription = "";
            }
            else
                CurrentRangeDescription = "";
        }

        #endregion
    }
}
