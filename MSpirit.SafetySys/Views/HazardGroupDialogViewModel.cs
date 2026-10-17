// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace MS_SafetyManagementSystem.ViewModels
{
    public class MonthItem : ObservableObject
    {
        public int MonthNumber { get; set; }
        public string DisplayName => $"{MonthNumber}月";
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
        private bool _isExists;
        public bool IsExists
        {
            get => _isExists;
            set => SetProperty(ref _isExists, value);
        }
    }

    public partial class HazardGroupDialogViewModel : ObservableObject
    {
        private readonly ProjectDataService _projectDataService;
        private readonly string _module;
        private int _currentYear;

        [ObservableProperty]
        private string _yearText = string.Empty;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private string? _groupName;

        public ObservableCollection<MonthItem> Months { get; } = new();

        public event Action<bool?>? RequestClose;

        public HazardGroupDialogViewModel(ProjectDataService projectDataService, string module)
        {
            _projectDataService = projectDataService;
            _module = module;
            _currentYear = DateTime.Now.Year;
            YearText = $"{_currentYear}年";

            // 加载已存在的年月分组名称（顶级分组）
            var existingGroups = _projectDataService.GetGroups(_module, null);
            _existingYearMonthNames = new HashSet<string>(existingGroups.Select(g => g.Name));

            InitializeMonths();
        }

        private HashSet<string> _existingYearMonthNames;

        private void InitializeMonths()
        {
            Months.Clear();
            for (int i = 1; i <= 12; i++)
            {
                string groupName = $"{_currentYear}年{i:D2}月";
                bool exists = _existingYearMonthNames.Contains(groupName);
                Months.Add(new MonthItem { MonthNumber = i, IsExists = exists });
            }
        }

        private void RefreshMonthsExists()
        {
            // 重新获取当前年份下的已存在分组名称
            var existingGroups = _projectDataService.GetGroups(_module, null);
            _existingYearMonthNames = new HashSet<string>(existingGroups.Select(g => g.Name));
            foreach (var month in Months)
            {
                string groupName = $"{_currentYear}年{month.MonthNumber:D2}月";
                month.IsExists = _existingYearMonthNames.Contains(groupName);
            }
        }

        [RelayCommand]
        private void PrevYear()
        {
            _currentYear--;
            YearText = $"{_currentYear}年";
            foreach (var month in Months)
                month.IsSelected = false;
            ErrorMessage = null;
            RefreshMonthsExists();
        }

        [RelayCommand]
        private void NextYear()
        {
            _currentYear++;
            YearText = $"{_currentYear}年";
            foreach (var month in Months)
                month.IsSelected = false;
            ErrorMessage = null;
            RefreshMonthsExists();
        }

        [RelayCommand]
        private void SelectMonth(int monthNumber)
        {
            // 清除之前的错误信息
            ErrorMessage = null;

            // 构建分组名称
            string groupName = $"{_currentYear}年{monthNumber:D2}月";

            // 检查是否存在
            var existingGroups = _projectDataService.GetGroups(_module, null);
            if (existingGroups.Any(g => g.Name == groupName))
            {
                ErrorMessage = $"分组“{groupName}”已存在，请选择其他月份";
                return;
            }

            // 创建分组
            GroupName = groupName;
            RequestClose?.Invoke(true);
        }

        [RelayCommand]
        private void Cancel()
        {
            GroupName = null;
            RequestClose?.Invoke(false);
        }
    }
}