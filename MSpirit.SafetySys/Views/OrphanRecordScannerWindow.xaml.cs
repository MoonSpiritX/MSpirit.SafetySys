// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Helpers;
using Serilog;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>
    /// 非标准记录扫描窗口。
    /// 扫描所有项目中直接挂在根分组（年月/单位）下而非子分组（日期/部门）的记录。
    /// 快捷键 Ctrl+Alt+DEL 打开。
    /// </summary>
    public partial class OrphanRecordScannerWindow : FluentWindow
    {
        private readonly ObservableCollection<OrphanRecordInfo> _records = new();
        private readonly ConfigManager _configManager;

        public OrphanRecordScannerWindow()
        {
            InitializeComponent();
            ResultDataGrid.ItemsSource = _records;

            string rootPath = AppServiceProvider.GetDataPathService().RootPath;
            _configManager = new ConfigManager(rootPath);
        }

        /// <summary>
        /// 扫描所有项目的所有模块，找出挂在根分组下的记录
        /// </summary>
        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            _records.Clear();

            var projects = _configManager.GetProjectList();
            int index = 0;

            foreach (var project in projects)
            {
                string projectFolder = _configManager.GetProjectFolder(project.Id);
                if (!System.IO.Directory.Exists(projectFolder)) continue;

                using var dataService = new ProjectDataService(project.Id, projectFolder);

                ScanHazardModule(dataService, project.Name, ref index);
                ScanPersonnelModule(dataService, project.Name, ref index);
            }

            Log.Information("非标准记录扫描完成，共发现 {Count} 条", _records.Count);
        }

        /// <summary>
        /// 扫描隐患模块：找 GroupId 指向根分组（年月分组）的记录
        /// </summary>
        private void ScanHazardModule(ProjectDataService dataService, string projectName, ref int index)
        {
            var allGroups = dataService.GetAllGroups(ModuleNames.Hazard);
            var rootGroups = allGroups.Where(g => string.IsNullOrEmpty(g.ParentId)).ToList();

            foreach (var root in rootGroups)
            {
                var records = dataService.GetRecordsByGroup(root.Id);
                foreach (var r in records)
                {
                    _records.Add(new OrphanRecordInfo
                    {
                        Index = ++index,
                        ProjectName = projectName,
                        ModuleName = "隐患",
                        Level1Group = root.Name,
                        Level2Group = "",
                        RecordSummary = !string.IsNullOrEmpty(r.Title) ? r.Title
                            : (!string.IsNullOrEmpty(r.Content) ? Truncate(r.Content, 20) : ""),
                        RecordId = r.Id,
                        ProjectId = dataService.ProjectId,
                        GroupId = r.GroupId
                    });
                }
            }
        }

        /// <summary>
        /// 扫描人员模块：找 GroupId 指向根分组（单位分组）的记录
        /// </summary>
        private void ScanPersonnelModule(ProjectDataService dataService, string projectName, ref int index)
        {
            var allGroups = dataService.GetAllGroups(ModuleNames.Personnel);
            var rootGroups = allGroups.Where(g => string.IsNullOrEmpty(g.ParentId)).ToList();

            foreach (var root in rootGroups)
            {
                var records = dataService.GetPersonnelRecordsByGroup(root.Id);
                foreach (var r in records)
                {
                    _records.Add(new OrphanRecordInfo
                    {
                        Index = ++index,
                        ProjectName = projectName,
                        ModuleName = "人员",
                        Level1Group = root.Name,
                        Level2Group = "",
                        RecordSummary = r.Title ?? "",
                        RecordId = r.Id,
                        ProjectId = dataService.ProjectId,
                        GroupId = r.GroupId
                    });
                }
            }
        }

        /// <summary>
        /// 删除选中的非标准记录
        /// </summary>
        private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResultDataGrid.SelectedItems.Cast<OrphanRecordInfo>().ToList();
            if (!selected.Any()) return;

            if (MessageDialog.Confirm($"确定删除选中的 {selected.Count} 条非标准记录吗？\n（该操作不可恢复）", "确认删除") != true)
                return;

            foreach (var item in selected)
            {
                DeleteRecord(item);
                _records.Remove(item);
            }

            Renumber();
        }

        /// <summary>
        /// 删除全部非标准记录
        /// </summary>
        private void DeleteAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_records.Any()) return;
            if (MessageDialog.Confirm($"确定删除全部 {_records.Count} 条非标准记录吗？\n（该操作不可恢复）", "确认删除") != true)
                return;

            var items = _records.ToList();
            foreach (var item in items)
            {
                DeleteRecord(item);
                _records.Remove(item);
            }
        }

        /// <summary>
        /// 移动选中的记录到指定分组
        /// </summary>
        private void MoveSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ResultDataGrid.SelectedItems.Cast<OrphanRecordInfo>().ToList();
            if (!selected.Any()) return;

            // 按 (ProjectId, ModuleName) 分组，不同组不能一起移动
            var groupKeys = selected.Select(s => new { s.ProjectId, s.ModuleName }).Distinct().ToList();
            if (groupKeys.Count > 1)
            {
                MessageDialog.Show("选中的记录涉及多个项目或模块，请分批移动。", "提示");
                return;
            }

            var first = selected.First();
            string projectFolder = _configManager.GetProjectFolder(first.ProjectId);
            if (!System.IO.Directory.Exists(projectFolder)) return;

            using var dataService = new ProjectDataService(first.ProjectId, projectFolder);

            bool isHazard = first.ModuleName == "隐患";
            string moduleKey = isHazard ? ModuleNames.Hazard : ModuleNames.Personnel;
            var allGroups = dataService.GetAllGroups(moduleKey);

            // 按下拉菜单的尺寸和位置计算弹出点
            var button = sender as UIElement;
            var screenPoint = button?.PointToScreen(new Point(0, button.RenderSize.Height)) ?? new Point(0, 0);

            // 构建下拉菜单：展平为"一级分组 > 二级分组"格式
            var menu = new Controls.DropdownMenu();
            foreach (var root in allGroups.Where(g => string.IsNullOrEmpty(g.ParentId)).OrderBy(g => g.Name))
            {
                var children = allGroups.Where(g => g.ParentId == root.Id).OrderBy(g => g.Name).ToList();
                if (children.Any())
                {
                    string rootId = root.Id;
                    string rootName = root.Name;
                    // 移到一级分组本身
                    menu.AddItem($"【{rootName}】", () => MoveRecords(selected, rootId, dataService, moduleKey));

                    foreach (var child in children)
                    {
                        string childId = child.Id;
                        string childName = child.Name;
                        string displayName = $"{rootName} > {childName}";
                        menu.AddItem(displayName, () => MoveRecords(selected, childId, dataService, moduleKey));
                    }
                }
                else
                {
                    string rootId = root.Id;
                    string rootName = root.Name;
                    menu.AddItem(rootName, () => MoveRecords(selected, rootId, dataService, moduleKey));
                }
            }
            menu.ShowAt(screenPoint);
        }

        /// <summary>
        /// 执行移动：将选中记录的目标 GroupId 更新为目标分组
        /// </summary>
        private void MoveRecords(System.Collections.Generic.List<OrphanRecordInfo> records,
            string targetGroupId, ProjectDataService dataService, string module)
        {
            bool isHazard = module == ModuleNames.Hazard;

            foreach (var item in records)
            {
                if (isHazard)
                {
                    var record = dataService.GetRecordById(item.RecordId);
                    if (record != null)
                    {
                        record.GroupId = targetGroupId;
                        dataService.UpdateRecord(record);
                    }
                }
                else
                {
                    var record = dataService.GetPersonnelRecord(item.RecordId);
                    if (record != null)
                    {
                        record.GroupId = targetGroupId;
                        dataService.UpdatePersonnelRecord(record);
                    }
                }
                _records.Remove(item);
            }

            Renumber();
            Log.Information("已移动 {Count} 条记录到目标分组", records.Count);
        }

        /// <summary>
        /// 删除单条记录及其附件
        /// </summary>
        private void DeleteRecord(OrphanRecordInfo item)
        {
            string projectFolder = _configManager.GetProjectFolder(item.ProjectId);
            if (!System.IO.Directory.Exists(projectFolder)) return;

            using var dataService = new ProjectDataService(item.ProjectId, projectFolder);
            dataService.DeleteAttachmentsByRecord(item.RecordId);

            if (item.ModuleName == "隐患")
                dataService.DeleteRecord(item.RecordId);
            else
                dataService.DeletePersonnelRecord(item.RecordId);
        }

        /// <summary>
        /// 重排序号
        /// </summary>
        private void Renumber()
        {
            for (int i = 0; i < _records.Count; i++)
                _records[i].Index = i + 1;
        }

        /// <summary>
        /// 截断字符串到指定长度
        /// </summary>
        private static string Truncate(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value[..maxLength] + "...";
        }

        /// <summary>
        /// 快捷键 Ctrl+Alt+0 关闭窗口
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.D0 && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
            {
                Close();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }
    }
}
