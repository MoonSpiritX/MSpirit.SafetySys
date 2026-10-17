// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace MS_SafetyManagementSystem.ViewModels
{
    public partial class PersonnelRecordsViewModel : ObservableObject
    {
        private readonly MainWindowViewModel _owner;
        private string? _currentGroupId;

        /// <summary>
        /// 当前分组的附件缓存，key=recordId，LoadRecords 时批量加载
        /// </summary>
        private Dictionary<string, List<Attachment>>? _attachmentsCache;

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private ObservableCollection<PersonnelRecord> _records = new();

        public event Action? SelectedStateChanged;

        public PersonnelRecordsViewModel(MainWindowViewModel owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// 订阅或取消订阅单个记录的 IsSelected 变更事件
        /// </summary>
        private void SubscribeRecordPropertyChanged(PersonnelRecord record, bool subscribe)
        {
            if (subscribe)
                record.PropertyChanged += OnRecordPropertyChanged;
            else
                record.PropertyChanged -= OnRecordPropertyChanged;
        }

        private void OnRecordPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PersonnelRecord.IsSelected))
            {
                SelectedStateChanged?.Invoke();
            }
        }

        public void LoadRecords(string? groupId)
        {
            _currentGroupId = groupId;

            // 取消旧记录的订阅
            foreach (var r in Records)
            {
                SubscribeRecordPropertyChanged(r, false);
            }
            Records.Clear();
            _attachmentsCache = null;

            if (string.IsNullOrEmpty(groupId)) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            // 判断根节点（单位）还是子节点（部门）
            var allGroups = dataService.GetAllGroups(ModuleNames.Personnel);
            var group = allGroups.FirstOrDefault(g => g.Id == groupId);

            List<PersonnelRecord> allRecords;

            if (group == null || string.IsNullOrEmpty(group.ParentId))
            {
                // 根节点（单位）：全量递归收集（用于统计表）
                allRecords = new List<PersonnelRecord>();
                CollectAllDescendantRecords(dataService, groupId, allRecords);
            }
            else
            {
                // 子节点（部门）：全量加载（不再分页）
                allRecords = dataService.GetPersonnelRecordsByGroup(groupId);
            }

            // 按退场规则排序：在场人员在前 → 退场时间近的在前 → 原 DisplayIndex
            allRecords = SortByExitTime(allRecords);

            TotalCount = allRecords.Count;

            int index = 1;
            foreach (var r in allRecords)
            {
                r.DisplayIndex = index++;
                SubscribeRecordPropertyChanged(r, true);
                Records.Add(r);
            }

            // 批量预加载所有记录的附件（N+1 → 1）
            var recordIds = Records.Select(r => r.Id).ToList();
            if (recordIds.Count > 0)
            {
                var allAttachments = dataService.GetAttachmentsByRecordIds(recordIds);
                _attachmentsCache = allAttachments
                    .GroupBy(a => a.RecordId)
                    .ToDictionary(g => g.Key, g => g.ToList());
            }
        }

        /// <summary>
        /// 按退场规则排序：
        /// 1. 在场人员（ExitTime 为空）在前
        /// 2. 已退场人员按退场时间降序（近期退场的在前）
        /// 3. 同退场日期的保持原 DisplayIndex 顺序
        /// 4. 全退场时跳过退场时间排序（全部同一组，无分离必要）
        /// </summary>
        internal static List<PersonnelRecord> SortByExitTime(List<PersonnelRecord> records)
        {
            // 全部退场 → 不做退场时间排序，保留 DisplayIndex 顺序
            bool hasActive = records.Any(r => string.IsNullOrWhiteSpace(r.ExitTime));
            if (!hasActive)
                return records.OrderBy(r => r.DisplayIndex).ToList();

            return records
                .OrderBy(r => string.IsNullOrWhiteSpace(r.ExitTime) ? 0 : 1)
                .ThenByDescending(r =>
                {
                    var (success, _, dt) = DateParser.TryParse(r.ExitTime);
                    return success && dt.HasValue ? dt.Value : DateTime.MinValue;
                })
                .ThenBy(r => r.DisplayIndex)
                .ToList();
        }

        /// <summary>
        /// 递归收集所有子分组的记录。
        /// 人员和隐患模块的分组结构为：单位 → 部门。
        /// 只从子分组取记录，不包含根节点自身的记录，避免将"孤儿记录"误计入。
        /// </summary>
        private void CollectAllDescendantRecords(ProjectDataService dataService, string groupId, List<PersonnelRecord> result)
        {
            var childGroups = dataService.GetGroups(ModuleNames.Personnel, groupId);
            foreach (var child in childGroups)
            {
                var records = dataService.GetPersonnelRecordsByGroup(child.Id);
                result.AddRange(records);
                CollectAllDescendantRecords(dataService, child.Id, result);
            }
        }

        /// <summary>
        /// 获取缓存的附件列表（供卡片视图使用，避免 N+1 查询）
        /// </summary>
        public IReadOnlyList<Attachment>? GetCachedAttachments(string recordId)
        {
            if (_attachmentsCache != null && _attachmentsCache.TryGetValue(recordId, out var attachments))
                return attachments;
            return null;
        }

        /// <summary>
        /// 使附件缓存失效（新增/删除图片后调用，确保下次读取取到最新数据）
        /// </summary>
        public void InvalidateAttachmentsCache()
        {
            _attachmentsCache = null;
        }

        public void AddNewRecord(string groupId)
        {
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            var newRecord = new PersonnelRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                GroupId = groupId,
                Title = "",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                YearMonth = DateTime.Now.ToString("yyyy-MM"),
                DisplayIndex = dataService.GetMaxPersonnelDisplayIndex(groupId) + 1
            };

            if (dataService.AddPersonnelRecord(newRecord))
            {
                LoadRecords(groupId);
            }
            else
            {
                MessageDialog.Show("添加人员记录失败", "错误");
            }
        }

        public void SaveRecord(PersonnelRecord record)
        {
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            if (!dataService.UpdatePersonnelRecord(record))
            {
                Log.Warning("保存人员记录失败，RecordId: {RecordId}, Title: {Title}", record.Id, record.Title);
            }
        }

        /// <summary>
        /// 从数据库重新读取单条记录，原地刷新 Records 集合中对应项的属性。
        /// 不重建控件，通过 INotifyPropertyChanged 驱动 UI 更新。
        /// 适用于后台 OCR 等异步写入后需要同步 UI 的场景。
        /// </summary>
        public void RefreshSingleRecord(string recordId)
        {
            var existing = Records.FirstOrDefault(r => r.Id == recordId);
            if (existing == null) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            var fresh = dataService.GetPersonnelRecord(recordId);
            if (fresh == null) return;

            existing.Title = fresh.Title;
            existing.Gender = fresh.Gender;
            existing.Age = fresh.Age;
            existing.EmployeeID = fresh.EmployeeID;
            existing.Position = fresh.Position;
            existing.PhoneNumber = fresh.PhoneNumber;
            existing.EducationLevel = fresh.EducationLevel;
            existing.NativePlace = fresh.NativePlace;
            existing.BirthDate = fresh.BirthDate;
            existing.EntryTime = fresh.EntryTime;
            existing.EduTime = fresh.EduTime;
            existing.ExitTime = fresh.ExitTime;
            existing.ThreeCategoryPersonnel = fresh.ThreeCategoryPersonnel;
            existing.SpecialOperationsPersonnel = fresh.SpecialOperationsPersonnel;
            existing.Remark = fresh.Remark;
            existing.IDCardDocNumber = fresh.IDCardDocNumber;
            existing.CertDocNumber = fresh.CertDocNumber;
            existing.IDCardStart = fresh.IDCardStart;
            existing.IDCardEnd = fresh.IDCardEnd;
            existing.CertStart = fresh.CertStart;
            existing.CertEnd = fresh.CertEnd;
            existing.UpdatedAt = fresh.UpdatedAt;
        }

        public void DeleteRecord(PersonnelRecord record)
        {
            if (record == null) return;
            string displayName = string.IsNullOrWhiteSpace(record.Title) ? "未命名" : record.Title;
            var result = MessageDialog.Confirm(
                $"确定要删除人员记录“{displayName}”吗？\n（该操作不可恢复，身份证/证书照片将一并删除）",
                "确认删除");
            if (result != true) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService != null)
            {
                dataService.DeletePersonnelRecord(record.Id);
                LoadRecords(_currentGroupId);
            }
        }

        /// <summary>
        /// 清空界面集合（项目切换/模块切换时调用，不清除数据库内容）
        /// </summary>
        public void ClearCollection()
        {
            foreach (var r in Records)
            {
                SubscribeRecordPropertyChanged(r, false);
            }
            Records.Clear();
            _attachmentsCache = null;
        }

        /// <summary>
        /// 将单条人员记录移动到指定分组，新记录排列在目标分组末尾
        /// </summary>
        public void MoveRecord(PersonnelRecord record, string targetGroupId)
        {
            if (record == null || string.IsNullOrEmpty(targetGroupId)) return;
            if (record.GroupId == targetGroupId) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            record.GroupId = targetGroupId;
            record.DisplayIndex = dataService.GetMaxPersonnelDisplayIndex(targetGroupId) + 1;
            dataService.UpdatePersonnelRecord(record);
            LoadRecords(_currentGroupId);
        }

        /// <summary>
        /// 批量移动选中的卡片到指定分组（工具栏"移动卡片"按钮调用），新记录排列在目标分组末尾
        /// </summary>
        public void MoveSelectedCards()
        {
            var selected = Records.Where(r => r.IsSelected).ToList();
            if (!selected.Any()) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            var dialog = new Views.SelectPersonnelGroupDialog(dataService);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() != true) return;

            string targetGroupId = dialog.SelectedGroupId!;
            if (string.IsNullOrEmpty(targetGroupId)) return;

            int baseIndex = dataService.GetMaxPersonnelDisplayIndex(targetGroupId);
            for (int i = 0; i < selected.Count; i++)
            {
                var record = selected[i];
                record.GroupId = targetGroupId;
                record.DisplayIndex = baseIndex + i + 1;
                dataService.UpdatePersonnelRecord(record);
            }

            string? currentGroupId = _currentGroupId;
            LoadRecords(currentGroupId);
        }

        /// <summary>
        /// 是否有选中的卡片（供 CanExecute 状态联动）
        /// </summary>
        public bool HasSelectedCards() => Records.Any(r => r.IsSelected);

        /// <summary>
        /// 批量删除选中的卡片（从数据库彻底删除）
        /// </summary>
        public void DeleteSelectedCards()
        {
            var selected = Records.Where(r => r.IsSelected).ToList();
            if (!selected.Any()) return;

            var result = MessageDialog.Confirm(
                $"确定要删除选中的 {selected.Count} 条人员记录吗？\n（该操作不可恢复）",
                "确认删除");
            if (result != true) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            foreach (var record in selected)
            {
                dataService.DeleteAttachmentsByRecord(record.Id);
                dataService.DeletePersonnelRecord(record.Id);
            }

            LoadRecords(_currentGroupId);
        }
    }
}
