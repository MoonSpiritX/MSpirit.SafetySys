// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MS_SafetyManagementSystem.Common;

namespace MS_SafetyManagementSystem.Views
{
    public partial class AddDateDialogViewModel : ObservableObject
    {
        private readonly ProjectDataService _dataService;
        private readonly string _projectId;
        private readonly string _module;
        private readonly string _parentGroupId;
        private readonly int _year;
        private readonly int _month;
        private readonly HashSet<int> _existingDays;

        [ObservableProperty]
        private string _title;  // 保留可能用于其他地方，但不再显示在内容区

        [ObservableProperty]
        private string _windowTitle;  // 新增：标题栏文本

        [ObservableProperty]
        private ObservableCollection<CalendarDay> _days = new();

        public AddDateDialogViewModel(ProjectDataService dataService, string parentGroupId, string parentGroupName)
        {
            _dataService = dataService;
            _parentGroupId = parentGroupId;
            // 从 parentGroupName 解析年月，例如 "2026年5月"
            var parts = parentGroupName.Split('年', '月');
            if (parts.Length >= 2 && int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m))
            {
                _year = y;
                _month = m;
            }
            else
            {
                throw new ArgumentException("无法解析年月分组名称");
            }

            _projectId = dataService.ProjectId;
            _module = ModuleNames.Hazard;

            // 获取该年月分组下已有的日期子组名称，提取日期数字
            var existingGroups = _dataService.GetGroups(_module, parentGroupId);
            _existingDays = existingGroups
                .Select(g => g.Name)
                .Select(name => System.Text.RegularExpressions.Regex.Match(name, @"(\d{1,2})月(\d{1,2})日"))
                .Where(m => m.Success && int.TryParse(m.Groups[2].Value, out _))
                .Select(m => int.Parse(m.Groups[2].Value))
                .ToHashSet();

            Title = $"为【{parentGroupName}】添加日期";
            WindowTitle = $"隐患管理模块 - {parentGroupName} - 添加日期";
            GenerateCalendar();
        }


        private void GenerateCalendar()
        {
            // 一周从周一开始
            DayOfWeek firstDayOfWeek = DayOfWeek.Monday;
            DateTime firstOfMonth = new DateTime(_year, _month, 1);
            int daysInMonth = DateTime.DaysInMonth(_year, _month);
            int leadingDays = ((int)firstOfMonth.DayOfWeek - (int)firstDayOfWeek + 7) % 7;

            // 上个月
            DateTime prevMonth = firstOfMonth.AddMonths(-1);
            int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
            int prevMonthStartDay = daysInPrevMonth - leadingDays + 1;

            // 总共42格
            Days.Clear();
            for (int i = 0; i < 42; i++)
            {
                DateTime date;
                bool isCurrentMonth;
                if (i < leadingDays)
                {
                    // 上个月
                    int day = prevMonthStartDay + i;
                    date = new DateTime(prevMonth.Year, prevMonth.Month, day);
                    isCurrentMonth = false;
                }
                else if (i < leadingDays + daysInMonth)
                {
                    // 当月
                    int day = i - leadingDays + 1;
                    date = new DateTime(_year, _month, day);
                    isCurrentMonth = true;
                }
                else
                {
                    // 下个月
                    int day = i - (leadingDays + daysInMonth) + 1;
                    date = new DateTime(_year, _month, 1).AddMonths(1).AddDays(day - 1);
                    isCurrentMonth = false;
                }

                var dayModel = new CalendarDay
                {
                    Date = date,
                    IsCurrentMonth = isCurrentMonth,
                    IsExists = isCurrentMonth && _existingDays.Contains(date.Day)
                };
                Days.Add(dayModel);
            }
        }

        [RelayCommand]
        private void SelectDate(CalendarDay day)
        {
            if (!day.IsCurrentMonth) return;

            var existingGroups = _dataService.GetGroups(_module, _parentGroupId);
            string baseName = $"{day.Date.Month:D2}月{day.Date.Day:D2}日";
            var sameNameGroups = existingGroups.Where(g => g.Name == baseName).ToList();

            string finalName = baseName;
            string? tag = null;

            if (sameNameGroups.Any())
            {
                var existingTags = sameNameGroups
                    .Select(g => g.Tag)
                    .Where(t => !string.IsNullOrEmpty(t) && int.TryParse(t, out _))
                    .Select(t => int.Parse(t!))
                    .ToList();
                int nextTag = 1;
                while (existingTags.Contains(nextTag)) nextTag++;
                tag = nextTag.ToString();
            }

            var newGroup = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                ProjectId = _projectId,
                Module = _module,
                ParentId = _parentGroupId,
                Name = finalName,
                Tag = tag,
                Order = 0,
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
            };

            if (_dataService.AddGroup(newGroup))
            {
                OnGroupCreated?.Invoke(newGroup.Id, newGroup.Name, _parentGroupId, newGroup.Tag);
                CloseDialog(true);
            }
            else
            {
                ErrorMessage = "创建日期分组失败，请重试。";
            }
        }
        private bool GroupExists(string groupName)
        {
            var children = _dataService.GetGroups(_module, _parentGroupId);
            return children.Any(g => g.Name == groupName);
        }

        public event Action<string, string, string, string?>? OnGroupCreated; // (groupId, groupName, parentId)
        public event Action<bool>? RequestClose;

        [ObservableProperty]
        private string? _errorMessage;

        [RelayCommand]
        private void Cancel() => CloseDialog(false);

        private void CloseDialog(bool result) => RequestClose?.Invoke(result);
    }
}
