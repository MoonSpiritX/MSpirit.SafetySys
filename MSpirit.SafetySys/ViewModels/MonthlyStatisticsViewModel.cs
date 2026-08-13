// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.ViewModels
{
    public partial class MonthlyStatisticsViewModel : ObservableObject
    {
        private readonly ProjectDataService _dataService;
        private readonly string _parentGroupId;
        private readonly string _yearMonthName;

        public string YearMonthName => _yearMonthName;

        [ObservableProperty]
        private ObservableCollection<MonthlyRecordRow> _rows = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string? _loadingMessage;

        public ICommand RefreshCommand { get; }

        public MonthlyStatisticsViewModel(ProjectDataService dataService, string parentGroupId, string yearMonthName)
        {
            _dataService = dataService;
            _parentGroupId = parentGroupId;
            _yearMonthName = yearMonthName;
            RefreshCommand = new AsyncRelayCommand(LoadDataAsync);
        }

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            LoadingMessage = "正在加载月度台账数据...";
            try
            {
                await Task.Run(() => LoadDataCore());
            }
            finally
            {
                IsLoading = false;
                LoadingMessage = null;
            }
        }

        private void LoadDataCore()
        {
            // 按日期升序获取所有日期子分组
            var dateGroups = _dataService.GetGroups("Hazard", _parentGroupId)
                .OrderBy(g => ParseDateValue(g.Name))
                .ToList();

            // 第一步：收集本月所有记录卡 ID
            var allRecordIds = new List<string>();
            var recordsByGroup = new Dictionary<string, List<Record>>(); // GroupId -> Records
            var groupDateMap = new Dictionary<string, DateTime?>();      // GroupId -> RecordDate

            foreach (var dateGroup in dateGroups)
            {
                var records = _dataService.GetRecordsByGroup(dateGroup.Id);
                if (records.Count == 0) continue;

                recordsByGroup[dateGroup.Id] = records;
                groupDateMap[dateGroup.Id] = ParseDateFromGroupName(dateGroup.Name);
                allRecordIds.AddRange(records.Select(r => r.Id));
            }

            // 第二步：一次性获取所有附件，并建立 RecordId -> List<Attachment> 的映射
            var attachmentsByRecord = new Dictionary<string, List<Attachment>>();
            if (allRecordIds.Any())
            {
                var allAttachments = _dataService.GetAttachmentsByRecordIds(allRecordIds);
                foreach (var att in allAttachments)
                {
                    if (!attachmentsByRecord.ContainsKey(att.RecordId))
                        attachmentsByRecord[att.RecordId] = new List<Attachment>();
                    attachmentsByRecord[att.RecordId].Add(att);
                }
            }

            // 第三步：构建行数据
            var allRowsList = new List<MonthlyRecordRow>();

            foreach (var dateGroup in dateGroups)
            {
                if (!recordsByGroup.TryGetValue(dateGroup.Id, out var records)) continue;
                DateTime? groupDate = groupDateMap[dateGroup.Id];

                foreach (var record in records)
                {
                    var attachments = attachmentsByRecord.GetValueOrDefault(record.Id) ?? new List<Attachment>();
                    int hazardPhotoCount = attachments.Count(a => a.AttachmentType == "Hazard");
                    int remedyPhotoCount = attachments.Count(a => a.AttachmentType == "Remedy");

                    var row = new MonthlyRecordRow
                    {
                        RecordId = record.Id,
                        DateGroupId = dateGroup.Id,
                        DateName = dateGroup.Name,
                        RecordDate = groupDate,
                        ResponsibleUnit = record.ResponsibleUnit,
                        ResponsiblePerson = record.ResponsiblePerson,
                        Location = record.Location,
                        HazardType = record.HazardType,
                        HazardLevel = record.HazardLevel,
                        Deadline = record.Deadline,
                        CompletedDate = record.CompletedDate,
                        Reviewer = record.Reviewer,
                        Description = record.Description,
                        Measure = record.Measure,
                        HazardPhotoCount = hazardPhotoCount,
                        RemedyPhotoCount = remedyPhotoCount
                    };

                    row.Status = ComputeStatus(record, hazardPhotoCount, groupDate);
                    row.RowNumber = allRowsList.Count + 1;   // 按当前构建顺序分配序号
                    allRowsList.Add(row);
                }
            }

            // 直接赋值，不进行额外排序
            Rows = new ObservableCollection<MonthlyRecordRow>(allRowsList);
        }

        private string ComputeStatus(Record record, int hazardPhotoCount, DateTime? recordDate)
        {
            bool isValidRecord = !string.IsNullOrEmpty(record.Description) || hazardPhotoCount > 0;
            bool hasBasicInfo = !string.IsNullOrEmpty(record.ResponsibleUnit) ||
                                !string.IsNullOrEmpty(record.ResponsiblePerson) ||
                                !string.IsNullOrEmpty(record.Location) ||
                                !string.IsNullOrEmpty(record.HazardType) ||
                                !string.IsNullOrEmpty(record.HazardLevel) ||
                                record.Deadline.HasValue;

            if (isValidRecord)
            {
                bool isCompleted = record.CompletedDate.HasValue && !string.IsNullOrEmpty(record.Measure);
                if (isCompleted)
                    return "完成";

                DateTime? deadlineToCheck = record.Deadline;
                if (!deadlineToCheck.HasValue && recordDate.HasValue)
                    deadlineToCheck = recordDate.Value.AddDays(3);

                if (deadlineToCheck.HasValue)
                {
                    if (DateTime.Now.Date > deadlineToCheck.Value.Date)
                        return "超期";
                    else
                        return "进行";
                }
                else
                {
                    return "进行";
                }
            }
            else if (hasBasicInfo)
            {
                return "无效";
            }
            else
            {
                return "";
            }
        }

        private DateTime? ParseDateFromGroupName(string dateName)
        {
            var match = Regex.Match(dateName, @"(\d{1,2})月(\d{1,2})日");
            if (!match.Success) return null;
            var yearMonthMatch = Regex.Match(_yearMonthName, @"(\d{4})年(\d{1,2})月");
            if (!yearMonthMatch.Success) return null;
            int year = int.Parse(yearMonthMatch.Groups[1].Value);
            int month = int.Parse(yearMonthMatch.Groups[2].Value);
            int day = int.Parse(match.Groups[2].Value);
            return new DateTime(year, month, day);
        }

        private int ParseDateValue(string dateName)
        {
            var match = Regex.Match(dateName, @"(\d{1,2})月(\d{1,2})日");
            if (match.Success)
            {
                int month = int.Parse(match.Groups[1].Value);
                int day = int.Parse(match.Groups[2].Value);
                return month * 100 + day;
            }
            return 0;
        }
    }
}