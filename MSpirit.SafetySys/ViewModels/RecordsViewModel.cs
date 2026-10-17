// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.ViewModels
{
    public partial class RecordsViewModel : ObservableObject
    {
        private readonly MainWindowViewModel _owner;
        private string? _currentGroupId;
        public event Action<string>? RecordAdded; // 参数为新记录ID

        // 存储原始记录的副本，用于比较是否发生变更
        private readonly Dictionary<string, Record> _originalRecords = new();

        // ===== 懒加载相关 =====
        private const int PageSize = 30;
        private int _currentPage;
        private bool _hasMore;

        [ObservableProperty]
        private bool _isLoadingMore;

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private ObservableCollection<Record> _records = new();

        [ObservableProperty]
        private Record? _selectedRecord;

        public IRelayCommand<string?> AddRecordForGroupCommand { get; }
        public ICommand DeleteSelectedRecordsCommand { get; }
        public ICommand ClearCardsContentCommand { get; }

        public RecordsViewModel(MainWindowViewModel owner)
        {
            _owner = owner;
            AddRecordForGroupCommand = new RelayCommand<string?>(ExecuteAddRecordForGroup);
            DeleteSelectedRecordsCommand = new RelayCommand(ExecuteDeleteSelectedRecords, CanDeleteSelectedRecords);
            ClearCardsContentCommand = new RelayCommand(ExecuteClearCardsContent, CanClearCardsContent);

            Records.CollectionChanged += OnRecordsCollectionChanged;
        }

        private void OnRecordsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (Record record in e.NewItems)
                {
                    record.PropertyChanged -= OnRecordPropertyChanged;
                    record.PropertyChanged += OnRecordPropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (Record record in e.OldItems)
                {
                    record.PropertyChanged -= OnRecordPropertyChanged;
                }
            }
            RefreshCommandsState();
        }

        private void OnRecordPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Record.IsSelected))
            {
                RefreshCommandsState();
            }
        }

        private void RefreshCommandsState()
        {
            ((RelayCommand)DeleteSelectedRecordsCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ClearCardsContentCommand).NotifyCanExecuteChanged();
        }

        public void LoadRecords(string? groupId)
        {
            _currentGroupId = groupId;
            // 清空原始记录字典
            _originalRecords.Clear();
            // 取消原有记录的订阅
            foreach (var r in Records)
            {
                r.PropertyChanged -= OnRecordPropertyChanged;
            }
            Records.Clear();
            _currentPage = 0;
            _hasMore = false;

            if (string.IsNullOrEmpty(groupId)) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            string module = _owner.GetCurrentModuleKey();
            var allGroups = dataService.GetAllGroups(module);
            var group = allGroups.FirstOrDefault(g => g.Id == groupId);

            if (group == null || string.IsNullOrEmpty(group.ParentId))
            {
                // 根节点（年月分组）：全量递归加载（仅用于状态栏计数，实际由统计表接管）
                var allRecords = new List<Record>();
                CollectAllDescendantRecords(dataService, groupId, module, allRecords);
                TotalCount = allRecords.Count;

                int index = 1;
                foreach (var r in allRecords)
                {
                    r.DisplayIndex = index++;
                    r.PropertyChanged += OnRecordPropertyChanged;
                    Records.Add(r);
                    _originalRecords[r.Id] = r.Clone();
                }
            }
            else
            {
                // 子节点（日期分组）：分页加载
                TotalCount = dataService.GetRecordsCountByGroup(groupId);
                var page = dataService.GetRecordsByGroupPaged(groupId, 0, PageSize);
                _hasMore = page.Count == PageSize;
                _currentPage = 1;

                int index = 1;
                foreach (var r in page)
                {
                    r.DisplayIndex = index++;
                    r.PropertyChanged += OnRecordPropertyChanged;
                    Records.Add(r);
                    _originalRecords[r.Id] = r.Clone();
                }
            }

            RefreshCommandsState();
        }

        /// <summary>
        /// 滚动加载下一页数据
        /// </summary>
        public async Task LoadNextPageAsync()
        {
            if (IsLoadingMore || !_hasMore || string.IsNullOrEmpty(_currentGroupId))
                return;

            IsLoadingMore = true;
            try
            {
                var dataService = _owner.GetProjectDataService();
                if (dataService == null) return;

                int offset = _currentPage * PageSize;
                var page = dataService.GetRecordsByGroupPaged(_currentGroupId, offset, PageSize);
                _hasMore = page.Count == PageSize;
                _currentPage++;

                int startIndex = Records.Count + 1;
                foreach (var r in page)
                {
                    r.DisplayIndex = startIndex++;
                    r.PropertyChanged += OnRecordPropertyChanged;
                    Records.Add(r);
                    _originalRecords[r.Id] = r.Clone();
                }
            }
            finally
            {
                IsLoadingMore = false;
            }
        }

        /// <summary>
        /// 递归收集所有子分组的记录（用于根节点的状态栏计数）。
        /// 只从子分组取记录，不包含根节点自身的记录，避免将"孤儿记录"误计入。
        /// </summary>
        private void CollectAllDescendantRecords(ProjectDataService dataService, string groupId, string module, List<Record> result)
        {
            var childGroups = dataService.GetGroups(module, groupId);
            foreach (var child in childGroups)
            {
                var records = dataService.GetRecordsByGroup(child.Id);
                result.AddRange(records);
                CollectAllDescendantRecords(dataService, child.Id, module, result);
            }
        }

        public void ClearCards()
        {
            _originalRecords.Clear();
            foreach (var r in Records)
            {
                r.PropertyChanged -= OnRecordPropertyChanged;
            }
            Records.Clear();
            SelectedRecord = null;
            RefreshCommandsState();
        }

        [RelayCommand]
        private void AddRecord()
        {
            if (string.IsNullOrEmpty(_currentGroupId))
            {
                MessageDialog.Show("请先选择一个分组", "提示");
                return;
            }
            CreateNewRecord(_currentGroupId);
        }

        private void ExecuteAddRecordForGroup(string? groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return;
            CreateNewRecord(groupId);
        }

        private void CreateNewRecord(string groupId)
        {
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            string module = _owner.GetCurrentModuleKey();
            var allGroups = dataService.GetAllGroups(module);
            var group = allGroups.FirstOrDefault(g => g.Id == groupId);
            if (group == null || string.IsNullOrEmpty(group.ParentId))
            {
                string msg = module == ModuleNames.Personnel
                    ? "只有部门分组才能添加人员卡片。"
                    : "只有日期分组才能添加记录卡。";
                MessageDialog.Show(msg, "提示");
                return;
            }

            string? yearMonth = null;
            if (module == ModuleNames.Hazard)
            {
                var parentGroup = allGroups.FirstOrDefault(g => g.Id == group.ParentId);
                if (parentGroup != null)
                {
                    var match = Regex.Match(parentGroup.Name, @"(\d{4})年(\d{1,2})月");
                    if (match.Success)
                    {
                        yearMonth = $"{match.Groups[1].Value}-{int.Parse(match.Groups[2].Value):D2}";
                    }
                }
            }
            else
            {
                // 人员模块：使用当前日期作为年月，用于附件存储目录
                yearMonth = DateTime.Now.ToString("yyyy-MM");
            }

            var newRecord = new Record
            {
                Id = Guid.NewGuid().ToString("N"),
                GroupId = groupId,
                Title = "新记录",
                Content = "",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                YearMonth = yearMonth,
                DisplayIndex = dataService.GetMaxDisplayIndex(groupId) + 1
            };

            if (dataService.AddRecord(newRecord))
            {
                if (module == ModuleNames.Hazard)
                {
                    LoadRecords(groupId);
                }
                else
                {
                    // 人员模块新建时标题为空，用户自行填写
                    newRecord.Title = "";
                    dataService.UpdateRecord(newRecord);
                    LoadRecords(groupId);
                }
                // 触发事件，通知主窗口滚动到新记录
                RecordAdded?.Invoke(newRecord.Id);
            }
            else
            {
                MessageDialog.Show("添加记录失败", "错误");
            }
        }

        /// <summary>
        /// 保存单条记录（仅在发生变更时执行）
        /// </summary>
        public void SaveRecord(Record record)
        {
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            // 检查是否有变更
            if (!HasChanges(record))
                return;

            if (!dataService.UpdateRecord(record))
            {
                Log.Warning("保存记录失败，RecordId: {RecordId}, Title: {Title}", record.Id, record.Title);
            }
            else
            {
                // 保存成功后，更新原始副本
                if (_originalRecords.ContainsKey(record.Id))
                    _originalRecords[record.Id] = record.Clone();
                else
                    _originalRecords[record.Id] = record.Clone();
            }
        }

        /// <summary>
        /// 保存当前所有记录（仅保存有变更的记录）
        /// </summary>
        public void SaveAllRecords()
        {
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            foreach (var record in Records)
            {
                if (!HasChanges(record))
                    continue;

                try
                {
                    if (dataService.UpdateRecord(record))
                    {
                        // 更新原始副本
                        if (_originalRecords.ContainsKey(record.Id))
                            _originalRecords[record.Id] = record.Clone();
                        else
                            _originalRecords[record.Id] = record.Clone();
                    }
                    else
                    {
                        Log.Warning("批量保存记录失败，RecordId: {RecordId}, Title: {Title}", record.Id, record.Title);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "批量保存记录异常，RecordId: {RecordId}", record.Id);
                }
            }
        }

        /// <summary>
        /// 判断记录是否发生了变更（比较所有业务字段）
        /// </summary>
        private bool HasChanges(Record record)
        {
            if (!_originalRecords.TryGetValue(record.Id, out var original))
                return true; // 新记录，未存储原始副本，视为有变更

            return !(original.ResponsibleUnit == record.ResponsibleUnit &&
                     original.ResponsiblePerson == record.ResponsiblePerson &&
                     original.Location == record.Location &&
                     original.HazardType == record.HazardType &&
                     original.HazardLevel == record.HazardLevel &&
                     original.Deadline == record.Deadline &&
                     original.CompletedDate == record.CompletedDate &&
                     original.Reviewer == record.Reviewer &&
                     original.Description == record.Description &&
                     original.Measure == record.Measure);
            // 可根据需要添加更多字段，例如 Title、Content 等，但当前场景主要这些
        }

        [RelayCommand]
        private void EditRecord() { }

        [RelayCommand]
        private void DeleteRecord()
        {
            if (SelectedRecord == null) return;
            var result = MessageDialog.Confirm($"确定要删除记录“{SelectedRecord.Title}”吗？", "确认删除");
            if (result != true) return;

            if (_owner.GetProjectDataService()!.DeleteRecord(SelectedRecord.Id))
            {
                LoadRecords(_currentGroupId);
            }
            else
            {
                MessageDialog.Show("删除记录失败", "错误");
            }
        }

        private bool CanDeleteSelectedRecords() => Records.Any(r => r.IsSelected);

        private void ExecuteDeleteSelectedRecords()
        {
            var selectedRecords = Records.Where(r => r.IsSelected).ToList();
            if (!selectedRecords.Any()) return;

            var result = MessageDialog.Confirm($"确定要删除选中的 {selectedRecords.Count} 条记录吗？", "确认删除");
            if (result != true) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            foreach (var record in selectedRecords)
            {
                dataService.DeleteRecord(record.Id);
            }

            LoadRecords(_currentGroupId);
        }

        private bool CanClearCardsContent() => Records.Any(r => r.IsSelected);

        private void ExecuteClearCardsContent()
        {
            var selectedRecords = Records.Where(r => r.IsSelected).ToList();
            if (!selectedRecords.Any()) return;

            var result = MessageDialog.Confirm($"确定要清空选中的 {selectedRecords.Count} 条记录的所有内容吗？\n（文字、隐患类型、级别、日期和图片将被清除，不可恢复）", "确认清空");
            if (result != true) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            foreach (var record in selectedRecords)
            {
                // 清空文本字段
                record.ResponsibleUnit = "";
                record.ResponsiblePerson = "";
                record.Location = "";
                record.HazardType = "";
                record.HazardLevel = "";
                record.Deadline = null;
                record.CompletedDate = null;
                record.Reviewer = "";
                record.Description = "";
                record.Measure = "";

                // 清空图片附件
                dataService.DeleteAttachmentsByRecord(record.Id);

                // 更新数据库中的记录（文本字段）
                dataService.UpdateRecord(record);
            }

            // 重新加载当前分组，刷新所有记录卡的显示（包括图片区域）
            LoadRecords(_currentGroupId);
        }

        /// <summary>
        /// 清空单张记录卡（当前右键点击的记录）
        /// </summary>
        public void ClearRecord(Record record)
        {
            if (record == null) return;

            // 清空文本字段
            record.ResponsibleUnit = "";
            record.ResponsiblePerson = "";
            record.Location = "";
            record.HazardType = "";
            record.HazardLevel = "";
            record.Deadline = null;
            record.CompletedDate = null;
            record.Reviewer = "";
            record.Description = "";
            record.Measure = "";

            // 清空图片附件
            var dataService = _owner.GetProjectDataService();
            dataService?.DeleteAttachmentsByRecord(record.Id);

            // 保存记录
            SaveRecord(record);

            // 重新加载当前分组（刷新UI）
            LoadRecords(_currentGroupId);
        }

        /// <summary>
        /// 删除单张记录卡（当前右键点击的记录）
        /// </summary>
        public void DeleteRecord(Record record)
        {
            if (record == null) return;
            var dataService = _owner.GetProjectDataService();
            if (dataService != null)
            {
                dataService.DeleteRecord(record.Id);
                LoadRecords(_currentGroupId);
            }
        }

        /// 移动记录卡到新的日期分组（同月内）
        public void MoveRecord(Record record, int targetDay, string? targetTag)
        {
            if (record == null) return;
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            // 检查 YearMonth 有效性
            if (string.IsNullOrEmpty(record.YearMonth))
            {
                MessageDialog.Show("当前记录缺少年月信息，无法移动。请尝试重新保存记录后再试。", "提示");
                return;
            }

            string targetGroupId;
            try
            {
                // 目标年月与源记录相同（同一个月内移动）
                targetGroupId = dataService.GetOrCreateDateGroup(record.YearMonth, targetDay, targetTag);
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"移动失败：{ex.Message}", "错误");
                return;
            }

            // 如果目标分组就是当前分组，无需移动
            if (targetGroupId == record.GroupId)
            {
                MessageDialog.Show("目标分组与当前分组相同，无需移动。", "提示");
                return;
            }

            // 直接更新数据库中的 GroupId（绕过 SaveRecord 的变更检测）
            string oldGroupId = record.GroupId;
            record.GroupId = targetGroupId;
            record.DisplayIndex = dataService.GetMaxDisplayIndex(targetGroupId) + 1;
            if (!dataService.UpdateRecord(record))
            {
                record.GroupId = oldGroupId; // 恢复
                MessageDialog.Show("移动记录失败，请重试。", "错误");
                return;
            }

            // 刷新分组树，使新创建的日期分组（如果有）立即显示
            string? selectedId = _owner.GroupTree.SelectedNode?.Id;
            _owner.GroupTree.LoadGroups();
            if (!string.IsNullOrEmpty(selectedId))
                _owner.GroupTree.SetSelectedNodeById(selectedId);

            // 重新加载当前分组（源分组）的记录列表，刷新显示
            LoadRecords(_currentGroupId);
        }

        public void CopyRecord(Record sourceRecord, int targetDay, string? targetTag)
        {
            if (sourceRecord == null) return;
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            // 检查 YearMonth 有效性
            if (string.IsNullOrEmpty(sourceRecord.YearMonth))
            {
                MessageDialog.Show("源记录缺少年月信息，无法复制。请尝试重新保存记录后再试。", "提示");
                return;
            }

            string targetGroupId;
            try
            {
                targetGroupId = dataService.GetOrCreateDateGroup(sourceRecord.YearMonth, targetDay, targetTag);
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"复制失败：{ex.Message}", "错误");
                return;
            }

            var newRecord = sourceRecord.Clone();
            newRecord.Id = Guid.NewGuid().ToString("N");
            newRecord.GroupId = targetGroupId;
            newRecord.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            newRecord.UpdatedAt = null;
            newRecord.DisplayIndex = dataService.GetMaxDisplayIndex(targetGroupId) + 1;
            newRecord.YearMonth = sourceRecord.YearMonth;

            if (dataService.AddRecord(newRecord))
            {
                // 复制附件（逐张容错，返回未复制成功的说明）
                List<string> copyFailures = new();
                try
                {
                    copyFailures = dataService.CopyAttachmentsForNewRecord(sourceRecord.Id, newRecord.Id, targetGroupId, sourceRecord.YearMonth);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "复制附件过程异常: {SourceRecordId} -> {TargetRecordId}", sourceRecord.Id, newRecord.Id);
                    copyFailures.Add($"附件复制过程异常：{ex.Message}");
                }

                // 刷新分组树，使新创建的日期分组（如果有）立即显示
                string? selectedId = _owner.GroupTree.SelectedNode?.Id;
                _owner.GroupTree.LoadGroups();
                if (!string.IsNullOrEmpty(selectedId))
                    _owner.GroupTree.SetSelectedNodeById(selectedId);

                // 获取目标分组的友好名称
                string targetGroupName = "";
                var targetGroup = dataService.GetAllGroups(ModuleNames.Hazard).FirstOrDefault(g => g.Id == targetGroupId);
                if (targetGroup != null)
                {
                    // 获取父级年月分组
                    var parentGroup = dataService.GetAllGroups(ModuleNames.Hazard).FirstOrDefault(g => g.Id == targetGroup.ParentId);
                    string yearMonth = parentGroup?.Name ?? "";          // 例如 "2026年06月"
                    string dateName = targetGroup.Name;                  // 例如 "06月26日"
                                                                         // 从 dateName 中提取日部分（去掉月份前缀 "MM月"）
                    string dayPart = dateName.Contains('月') ? dateName.Substring(dateName.IndexOf('月') + 1) : dateName;
                    string tagInfo = string.IsNullOrEmpty(targetGroup.Tag) ? "" : $" (标签: {targetGroup.Tag})";
                    targetGroupName = $"{yearMonth}{dayPart}{tagInfo}";
                }
                else
                {
                    targetGroupName = targetGroupId;
                }

                // 如果复制到的目标分组就是当前显示的分组，则刷新记录列表
                if (targetGroupId == _currentGroupId)
                {
                    LoadRecords(_currentGroupId);
                }
                else
                {
                    if (copyFailures.Count == 0)
                    {
                        MessageDialog.Show($"复制成功，目标分组为：{targetGroupName}", "提示");
                    }
                    else
                    {
                        string detail = string.Join("\n- ", copyFailures.Take(6));
                        if (copyFailures.Count > 6)
                            detail += $"\n- 等共 {copyFailures.Count} 项";
                        MessageDialog.Show($"已复制到：{targetGroupName}\n\n以下 {copyFailures.Count} 张图片未能复制：\n- {detail}", "提示");
                    }
                }
            }
        }
    }
}
