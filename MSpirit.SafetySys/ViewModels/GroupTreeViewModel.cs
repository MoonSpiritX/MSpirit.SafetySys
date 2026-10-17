// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using App = System.Windows.Application;
using Group = MS_SafetyManagementSystem.Models.Group;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Export;

namespace MS_SafetyManagementSystem.ViewModels
{
    public class GroupNode : ObservableObject
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? ParentId { get; set; }
        public ObservableCollection<GroupNode> Children { get; set; } = new();
        private bool _isSelected;
        private string? _tag;
        public string? Tag
        {
            get => _tag;
            set => SetProperty(ref _tag, value);
        }
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }
        // 右键菜单项集合
        public ObservableCollection<ContextMenuItem> ContextMenuItems { get; } = new();
    }

    public partial class GroupTreeViewModel : ObservableObject
    {
        private readonly MainWindowViewModel _owner;
        public event Action<string>? ScrollToNodeRequested;

        [ObservableProperty]
        private ObservableCollection<GroupNode> _rootNodes = new();

        private GroupNode? _selectedNode;
        public GroupNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode == value) return;
                if (_selectedNode != null)
                    _selectedNode.IsSelected = false;
                _selectedNode = value;
                if (_selectedNode != null)
                    _selectedNode.IsSelected = true;
                OnPropertyChanged();
                // 按模块分流加载记录
                string module = _owner.GetCurrentModuleKey();
                if (module == ModuleNames.Personnel)
                    _owner.PersonnelRecords.LoadRecords(value?.Id);
                else
                    _owner.Records.LoadRecords(value?.Id);
            }
        }

        private List<Group> _currentGroups = new();

        // 新增命令
        public ICommand AddDateForNodeCommand { get; }
        public ICommand AddRecordForNodeCommand { get; }
        public ICommand BatchPersonnelEntryCommand { get; }
        public ICommand BatchHazardEntryCommand { get; }
        public ICommand RenameGroupCommand { get; }
        public ICommand DeleteGroupCommand { get; }
        public ICommand SetTagForNodeCommand { get; }
        public ICommand SortRecordsCommand { get; }
        public ICommand ExportDateGroupCommand { get; }
        public ICommand ImportToDateGroupCommand { get; }
        public ICommand SortUnitsCommand { get; }
        public ICommand SortDepartmentsCommand { get; }
        public ICommand SortPersonnelRecordsCommand { get; }

        public GroupTreeViewModel(MainWindowViewModel owner)
        {
            _owner = owner;
            AddDateForNodeCommand = new RelayCommand<GroupNode>(ExecuteAddDateForNode);
            AddRecordForNodeCommand = new RelayCommand<GroupNode>(ExecuteAddRecordForNode);
            BatchPersonnelEntryCommand = new RelayCommand<GroupNode>(ExecuteBatchPersonnelEntry);
            BatchHazardEntryCommand = new RelayCommand<GroupNode>(ExecuteBatchHazardEntry);
            RenameGroupCommand = new RelayCommand<GroupNode>(ExecuteRenameGroup);
            DeleteGroupCommand = new RelayCommand<GroupNode>(ExecuteDeleteGroup);
            SetTagForNodeCommand = new RelayCommand<GroupNode>(ExecuteSetTagForNode);
            SortRecordsCommand = new RelayCommand<GroupNode>(ExecuteSortRecords);
            ExportDateGroupCommand = new RelayCommand<GroupNode>(ExecuteExportDateGroup);
            ImportToDateGroupCommand = new RelayCommand<GroupNode>(ExecuteImportToDateGroup);
            SortUnitsCommand = new RelayCommand<GroupNode>(ExecuteSortUnits);
            SortDepartmentsCommand = new RelayCommand<GroupNode>(ExecuteSortDepartments);
            SortPersonnelRecordsCommand = new RelayCommand<GroupNode>(ExecuteSortPersonnelRecords);
        }

        public void LoadGroups()
        {
            try
            {
                // 保存当前选中节点（按 Id），树重建后恢复选中并触发 SelectedNode setter 重载记录列表
                // （2026-08-14 修复：原逻辑重建树后不恢复选中，右侧记录列表保持旧数据，需切模块才刷新）
                string? prevSelectedId = SelectedNode?.Id;

                RootNodes.Clear();
                var dataService = _owner.GetProjectDataService();
                if (dataService == null)
                {
                    Log.Warning("LoadGroups: 无法获取 ProjectDataService");
                    MessageDialog.Show("ProjectDataService 为空，请确保已选择项目。", "提示");
                    return;
                }

                var module = _owner.GetCurrentModuleKey();
                var groups = dataService.GetAllGroups(module);
                Log.Information("LoadGroups: 从数据库获取到 {Count} 个分组", groups.Count);

                _currentGroups = groups.ToList();

                BuildTree(groups, null, RootNodes, 0);

                if (module == ModuleNames.Hazard)
                {
                    var sortedRoots = RootNodes.OrderByDescending(r => ParseGroupToNumeric(r.Name)).ToList();
                    RootNodes.Clear();
                    foreach (var root in sortedRoots)
                        RootNodes.Add(root);
                    SortChildrenDescending(RootNodes);
                }

                // 刷新所有节点的菜单项
                RefreshAllNodeMenus();

                // 树重建后：恢复原选中节点（仍在树中则重载其记录；已被删除则清空选中）
                if (RootNodes.Count == 0)
                {
                    SelectedNode = null;
                }
                else if (!string.IsNullOrEmpty(prevSelectedId))
                {
                    var restored = FindNodeById(prevSelectedId);
                    SelectedNode = restored;   // setter 内部按模块 LoadRecords，刷新右侧记录列表
                }

                Log.Information("LoadGroups: 构建树完成，根节点数 {Count}", RootNodes.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LoadGroups 发生异常");
                MessageDialog.Show($"加载分组树失败: {ex.Message}", "错误");
            }
        }

        private void BuildTree(List<Group> groups, string? parentId, ObservableCollection<GroupNode> nodes, int level)
        {
            var children = groups.Where(g =>
                (string.IsNullOrEmpty(g.ParentId) && parentId == null) ||
                (g.ParentId == parentId)
            );
            foreach (var g in children)
            {
                var node = new GroupNode { Id = g.Id, Name = g.Name, ParentId = parentId, Tag = g.Tag };
                nodes.Add(node);
                BuildTree(groups, g.Id, node.Children, level + 1);
            }
        }

        private void BuildNodeMenu(GroupNode node, string module, int level)
        {
            node.ContextMenuItems.Clear();

            if (module == ModuleNames.Hazard)
            {
                if (level == 0) // 顶级分组（年月）
                {
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "添加日期",
                        Command = AddDateForNodeCommand,
                        CommandParameter = node
                    });
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "隐患手机录入",
                        Command = BatchHazardEntryCommand,
                        CommandParameter = node
                    });
                }
                else if (level == 1) // 日期分组
                {
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "添加记录",
                        Command = AddRecordForNodeCommand,
                        CommandParameter = node
                    });
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "设置标签",
                        Command = SetTagForNodeCommand,
                        CommandParameter = node
                    });
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "导出当日",
                        Command = ExportDateGroupCommand,
                        CommandParameter = node
                    });
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "向当日导入",
                        Command = ImportToDateGroupCommand,
                        CommandParameter = node
                    });
                    // 记录排序
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "记录排序",
                        Command = SortRecordsCommand,
                        CommandParameter = node
                    });
                }

                // 只有非顶级分组（日期分组）才显示“重命名”
                if (level > 0)
                {
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "重命名",
                        Command = RenameGroupCommand,
                        CommandParameter = node
                    });
                }

                // 所有节点都显示“删除”
                node.ContextMenuItems.Add(new ContextMenuItem
                {
                    Header = "删除",
                    Command = DeleteGroupCommand,
                    CommandParameter = node
                });
            }
            else if (module == ModuleNames.Personnel)
            {
                // 预留人员模块自定义菜单
                // 示例：顶级（单位）显示“添加部门”，二级（部门）显示“添加人员”，均有重命名/删除
                if (level == 0)
                {
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "添加部门",
                        Command = AddDateForNodeCommand,
                        CommandParameter = node
                    });
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "批量人员录入",
                        Command = BatchPersonnelEntryCommand,
                        CommandParameter = node
                    });
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "单位排序",
                        Command = SortUnitsCommand,
                        CommandParameter = node
                    });
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "部门排序",
                        Command = SortDepartmentsCommand,
                        CommandParameter = node
                    });
                }
                else if (level == 1)
                {
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "添加人员",
                        Command = AddRecordForNodeCommand,
                        CommandParameter = node
                    });
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "批量人员录入",
                        Command = BatchPersonnelEntryCommand,
                        CommandParameter = node
                    });
                    // 人员记录卡排序
                    node.ContextMenuItems.Add(new ContextMenuItem
                    {
                        Header = "记录卡排序",
                        Command = SortPersonnelRecordsCommand,
                        CommandParameter = node
                    });
                }
                node.ContextMenuItems.Add(new ContextMenuItem
                {
                    Header = "重命名",
                    Command = RenameGroupCommand,
                    CommandParameter = node
                });
                node.ContextMenuItems.Add(new ContextMenuItem
                {
                    Header = "删除",
                    Command = DeleteGroupCommand,
                    CommandParameter = node
                });
            }
        }

        private void RefreshAllNodeMenus()
        {
            string module = _owner.GetCurrentModuleKey();
            foreach (var root in RootNodes)
            {
                RefreshNodeMenuRecursive(root, module, 0);
            }
        }

        private void RefreshNodeMenuRecursive(GroupNode node, string module, int level)
        {
            BuildNodeMenu(node, module, level);
            foreach (var child in node.Children)
            {
                RefreshNodeMenuRecursive(child, module, level + 1);
            }
        }

        private void SortChildrenDescending(ObservableCollection<GroupNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Any())
                {
                    var sorted = node.Children.OrderByDescending(c => ParseDateValue(c.Name)).ToList();
                    node.Children.Clear();
                    foreach (var child in sorted)
                        node.Children.Add(child);
                    SortChildrenDescending(node.Children);
                }
            }
        }

        private int ParseGroupToNumeric(string groupName)
        {
            var match = Regex.Match(groupName, @"(\d{4})年(\d{1,2})月");
            if (match.Success)
            {
                int year = int.Parse(match.Groups[1].Value);
                int month = int.Parse(match.Groups[2].Value);
                return year * 100 + month;
            }
            return 0;
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

        private void InsertGroup(Group newGroup)
        {
            var module = _owner.GetCurrentModuleKey();
            if (module == ModuleNames.Hazard)
            {
                _currentGroups.Add(newGroup);

                GroupNode? parentNode = null;
                if (!string.IsNullOrEmpty(newGroup.ParentId))
                {
                    parentNode = FindNodeById(newGroup.ParentId);
                    if (parentNode == null) return;
                }

                var newNode = new GroupNode { Id = newGroup.Id, Name = newGroup.Name, ParentId = newGroup.ParentId, Tag = newGroup.Tag };
                var targetCollection = parentNode?.Children ?? RootNodes;
                int insertIndex = 0;

                if (parentNode == null)
                {
                    int newValue = ParseGroupToNumeric(newGroup.Name);
                    for (int i = 0; i < targetCollection.Count; i++)
                    {
                        int currentValue = ParseGroupToNumeric(targetCollection[i].Name);
                        if (newValue > currentValue)
                        {
                            insertIndex = i;
                            break;
                        }
                        insertIndex = i + 1;
                    }
                }
                else
                {
                    int newValue = ParseDateValue(newGroup.Name);
                    for (int i = 0; i < targetCollection.Count; i++)
                    {
                        int currentValue = ParseDateValue(targetCollection[i].Name);
                        if (newValue > currentValue)
                        {
                            insertIndex = i;
                            break;
                        }
                        insertIndex = i + 1;
                    }
                }

                targetCollection.Insert(insertIndex, newNode);
                // 刷新菜单
                int level = parentNode == null ? 0 : 1;
                BuildNodeMenu(newNode, module, level);
                SelectedNode = newNode;
                ScrollToNodeRequested?.Invoke(newGroup.Id);

                Log.Information("增量插入分组成功: {GroupName}, 父级: {ParentId}", newGroup.Name, newGroup.ParentId ?? "根");
            }
            else
            {
                LoadGroups();
            }
        }

        public void Clear()
        {
            RootNodes.Clear();
            SelectedNode = null;
            _currentGroups.Clear();
        }

        [RelayCommand]
        private void CreateGroup()
        {
            var dataService = _owner.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show("请先选择一个项目", "提示");
                return;
            }

            string module = _owner.GetCurrentModuleKey();
            if (module == ModuleNames.Hazard)
            {
                var dialog = new HazardGroupDialog(dataService, module);
                dialog.Owner = Application.Current.MainWindow;
                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.GroupName))
                {
                    if (_currentGroups.Any(g => g.Name == dialog.GroupName))
                    {
                        MessageDialog.Show($"分组“{dialog.GroupName}”已存在，请勿重复创建。", "提示");
                        return;
                    }

                    var newGroup = new Group
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        ProjectId = dataService.ProjectId,
                        Module = module,
                        ParentId = null,
                        Name = dialog.GroupName,
                        Order = 0,
                        CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
                    };

                    if (dataService.AddGroup(newGroup))
                    {
                        InsertGroup(newGroup);
                    }
                    else
                    {
                        MessageDialog.Show("创建分组失败，请重试。", "错误");
                    }
                }
            }
            else
            {
                var inputDialog = new InputDialog(
                    module == ModuleNames.Personnel ? "请输入单位名称" : "请输入分组名称");
                inputDialog.Owner = Application.Current.MainWindow;
                if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
                {
                    string groupName = inputDialog.InputText.Trim();

                    if (module == ModuleNames.Personnel)
                    {
                        // 人员模块：添加单位（根节点）
                        if (_currentGroups.Any(g => g.ParentId == null && g.Name == groupName))
                        {
                            MessageDialog.Show($"单位“{groupName}”已存在，请勿重复创建。", "提示");
                            return;
                        }

                        var newGroup = new Group
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            ProjectId = dataService.ProjectId,
                            Module = ModuleNames.Personnel,
                            ParentId = null,
                            Name = groupName,
                            Order = RootNodes.Count,
                            CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
                        };

                        if (dataService.AddGroup(newGroup))
                        {
                            InsertGroup(newGroup);
                        }
                        else
                        {
                            MessageDialog.Show("创建单位失败，请重试。", "错误");
                        }
                    }
                    else
                    {
                        // 隐患模块或其他：原有逻辑
                        if (_currentGroups.Any(g => g.Name == groupName))
                        {
                            MessageDialog.Show($"分组“{groupName}”已存在，请勿重复创建。", "提示");
                            return;
                        }

                        string? parentId = SelectedNode?.ParentId;
                        var newGroup = new Group
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            ProjectId = dataService.ProjectId,
                            Module = module,
                            ParentId = parentId,
                            Name = groupName,
                            Order = 0,
                            CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
                        };

                        if (dataService.AddGroup(newGroup))
                        {
                            InsertGroup(newGroup);
                        }
                        else
                        {
                            MessageDialog.Show("创建分组失败，请重试。", "错误");
                        }
                    }
                }
            }
        }

        [RelayCommand]
        private void AddDate()
        {
            var selectedNode = SelectedNode;
            if (selectedNode == null)
            {
                MessageDialog.Show("请先选中一个分组", "提示");
                return;
            }

            string module = _owner.GetCurrentModuleKey();
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            if (module == ModuleNames.Personnel)
            {
                // 人员模块：在单位下添加部门。如果选中的是部门，则在其父级（单位）下添加同级部门
                string parentId = selectedNode.ParentId ?? selectedNode.Id;

                var inputDialog = new InputDialog("请输入部门名称");
                inputDialog.Owner = Application.Current.MainWindow;
                if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.InputText)) return;

                string deptName = inputDialog.InputText.Trim();
                // 获取父节点的子列表，检查重名
                var parentNode = selectedNode.ParentId != null ? FindNodeById(selectedNode.ParentId) : null;
                var siblings = parentNode?.Children ?? selectedNode.Children;
                if (siblings.Any(c => c.Name == deptName))
                {
                    MessageDialog.Show($"部门“{deptName}”已存在，请勿重复创建。", "提示");
                    return;
                }

                var newGroup = new Group
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProjectId = dataService.ProjectId,
                    Module = ModuleNames.Personnel,
                    ParentId = parentId,
                    Name = deptName,
                    Order = siblings.Count,
                    CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
                };

                if (dataService.AddGroup(newGroup))
                {
                    InsertGroup(newGroup);
                }
                else
                {
                    MessageDialog.Show("创建部门失败，请重试。", "错误");
                }
                return;
            }

            // 隐患模块：原有逻辑
            GroupNode targetYearMonthNode = selectedNode;
            if (selectedNode.ParentId != null)
            {
                var parentNode = FindNodeById(selectedNode.ParentId);
                if (parentNode == null)
                {
                    MessageDialog.Show("无法找到所属的年月分组", "错误");
                    return;
                }
                targetYearMonthNode = parentNode;
            }

            if (targetYearMonthNode.ParentId != null)
            {
                MessageDialog.Show("只能为年月分组添加日期", "提示");
                return;
            }

            var dialog = new AddDateDialog(dataService, targetYearMonthNode.Id, targetYearMonthNode.Name);
            dialog.Owner = Application.Current.MainWindow;
            dialog.GroupCreated += (groupId, groupName, parentId, tag) =>
            {
                InsertGroup(new Group
                {
                    Id = groupId,
                    Name = groupName,
                    ParentId = parentId,
                    Module = ModuleNames.Hazard,
                    ProjectId = dataService.ProjectId,
                    Tag = tag,
                    Order = 0,
                    CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
                });
                var newNode = FindNodeById(groupId);
                if (newNode != null) SelectedNode = newNode;
            };
            dialog.ShowDialog();
        }

        public GroupNode? FindNodeById(string id)
        {
            return FindNode(RootNodes, id);
        }

        private GroupNode? FindNode(ObservableCollection<GroupNode> nodes, string id)
        {
            foreach (var node in nodes)
            {
                if (node.Id == id) return node;
                var found = FindNode(node.Children, id);
                if (found != null) return found;
            }
            return null;
        }

        public List<string> GetExpandedTopLevelNames()
        {
            var names = new List<string>();
            foreach (var node in RootNodes)
            {
                if (node.IsExpanded)
                    names.Add(node.Name);
            }
            return names;
        }

        public void SetState(List<string> expandedTopLevelNames, string selectedNodeId)
        {
            foreach (var root in RootNodes)
                root.IsExpanded = false;

            if (expandedTopLevelNames != null)
            {
                foreach (var root in RootNodes)
                {
                    if (expandedTopLevelNames.Contains(root.Name))
                        root.IsExpanded = true;
                }
            }

            SetSelectedNodeById(selectedNodeId);

            // 如果成功设置了选中节点，请求滚动到该节点
            if (SelectedNode != null)
            {
                Log.Information("SetState: 请求滚动到选中节点 {NodeId} ({NodeName})", SelectedNode.Id, SelectedNode.Name);
                ScrollToNodeRequested?.Invoke(SelectedNode.Id);
            }
        }

        public string? GetSelectedNodeId()
        {
            return SelectedNode?.Id;
        }

        public void SetSelectedNodeById(string? nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                Log.Information("SetSelectedNodeById: 传入的 nodeId 为空，将选中第一个根节点");
                if (RootNodes.Count > 0)
                    SelectedNode = RootNodes[0];
                return;
            }

            Log.Information("SetSelectedNodeById: 尝试选中节点 {NodeId}", nodeId);
            var target = FindNodeById(nodeId);
            if (target != null)
            {
                Log.Information("SetSelectedNodeById: 成功找到节点 {NodeId}，名称为 {NodeName}", nodeId, target.Name);
                SelectedNode = target;
            }
            else
            {
                Log.Warning("SetSelectedNodeById: 未找到节点 {NodeId}，将选中第一个根节点", nodeId);
                if (RootNodes.Count > 0)
                    SelectedNode = RootNodes[0];
            }
        }

        private void ExecuteSetTagForNode(GroupNode? node)
        {
            if (node == null) return;
            // 仅日期分组（有父节点）可设置标签
            if (string.IsNullOrEmpty(node.ParentId))
            {
                MessageDialog.Show("年月分组不支持设置标签", "提示");
                return;
            }

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            // 获取当前分组对应的 Group 对象
            var group = _currentGroups.FirstOrDefault(g => g.Id == node.Id);
            if (group == null) return;

            // 获取同级分组的现有名称和标签组合（用于唯一性校验）
            var siblings = _currentGroups.Where(g => g.ParentId == node.ParentId && g.Id != node.Id).ToList();
            var existingNameTagPairs = siblings.Select(g => (Name: g.Name, Tag: g.Tag)).ToList();

            var dialog = new SetTagDialog(node.Name, group.Tag, existingNameTagPairs);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() != true) return;

            string? newTag = dialog.NewTag?.Trim();
            if (newTag == group.Tag) return; // 无变化

            // 更新数据库
            group.Tag = newTag;
            if (dataService.UpdateGroup(group))
            {
                // 更新 UI 中的节点属性
                node.Tag = newTag;
                // 刷新树（可选，仅更新显示即可，无需重新加载）
                // 因为标签显示在节点上，直接修改 node.Tag 即可
            }
            else
            {
                MessageDialog.Show("设置标签失败", "错误");
            }
        }

        private void ExecuteExportDateGroup(GroupNode? node)
        {
            if (node == null || string.IsNullOrEmpty(node.ParentId)) return;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            // 查找父节点（年月分组）获取年份月份名称
            var parentNode = FindNodeById(node.ParentId);
            if (parentNode == null) return;

            var exportControl = new ExportControl(mainWindow.ViewModel.CurrentProjectName, mainWindow.ViewModel.CurrentModuleName);
            if (exportControl.DataContext is ExportWindowViewModel exportVm)
            {
                exportVm.SelectedYearMonthText = parentNode.Name;

                // SelectedYearMonthText 设置后会同步加载 DateGroups，可以直接设置日期
                string dateDisplay = node.Name;
                if (!string.IsNullOrEmpty(node.Tag))
                    dateDisplay += $" (标签:{node.Tag})";
                exportVm.SelectedDateGroupText = dateDisplay;
            }
            mainWindow.ShowOverlay(exportControl);
        }

        // 向当日导入：跳转到数据导入界面并预选当前日期分组
        private void ExecuteImportToDateGroup(GroupNode? node)
        {
            if (node == null || string.IsNullOrEmpty(node.ParentId)) return;

            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null) return;

            // 查找父节点（年月分组）获取年份月份名称
            var parentNode = FindNodeById(node.ParentId);
            if (parentNode == null) return;

            var exportControl = new ExportControl(mainWindow.ViewModel.CurrentProjectName, mainWindow.ViewModel.CurrentModuleName);
            if (exportControl.DataContext is ExportWindowViewModel exportVm)
            {
                // 切换到数据导入模式，并勾选“导入到指定日期分组”
                exportVm.IsImportMode = true;
                exportVm.IsMonthlyRangeSelected = true;

                exportVm.SelectedYearMonthText = parentNode.Name;

                // SelectedYearMonthText 设置后会同步加载 DateGroups，可以直接设置日期
                string dateDisplay = node.Name;
                if (!string.IsNullOrEmpty(node.Tag))
                    dateDisplay += $" (标签:{node.Tag})";
                exportVm.SelectedDateGroupText = dateDisplay;
            }
            mainWindow.ShowOverlay(exportControl);
        }

        // 记录排序
        private void ExecuteSortRecords(GroupNode? node)
        {
            if (node == null) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show("请先选择一个项目", "提示");
                return;
            }

            // 获取该分组下的所有记录，按当前 DisplayIndex 排序
            var records = dataService.GetRecordsByGroup(node.Id)
                .OrderBy(r => r.DisplayIndex)
                .ToList();

            if (records.Count == 0)
            {
                MessageDialog.Show("该分组下没有记录", "提示");
                return;
            }

            // 构建标题：项目名_年月日-标签
            string title = $"记录卡排序 - {records[0].YearMonth ?? ""}";
            if (!string.IsNullOrEmpty(node.Tag))
                title += $"_{node.Tag}";
            else
                title += $"_{node.Name}";

            // 打开排序窗口
            var dialog = new Views.CardOrder(dataService, title);
            dialog.SetRecords(records);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();

            // 刷新显示
            _owner.Records.LoadRecords(node.Id);
        }

        // 单位排序：弹出弹窗手动排序 Personnel 模块所有顶级分组（单位）
        private void ExecuteSortUnits(GroupNode? node)
        {
            if (node == null) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show("请先选择一个项目", "提示");
                return;
            }

            // 获取所有顶级分组（单位），按当前 Order 排序
            var units = dataService.GetGroups(ModuleNames.Personnel)
                .OrderBy(g => g.Order)
                .ToList();

            if (units.Count == 0)
            {
                MessageDialog.Show("没有可排序的单位", "提示");
                return;
            }

            // 打开分组排序窗口
            var dialog = new Views.GroupOrder(dataService, "单位排序");
            dialog.SetGroups(units);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();

            // 刷新分组树
            LoadGroups();
        }

        // 部门排序：弹出弹窗手动排序当前选中单位下的所有部门分组
        private void ExecuteSortDepartments(GroupNode? node)
        {
            if (node == null) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show("请先选择一个项目", "提示");
                return;
            }

            // 获取当前节点下的子级分组（部门），按当前 Order 排序
            var departments = dataService.GetGroups(ModuleNames.Personnel, node.Id)
                .OrderBy(g => g.Order)
                .ToList();

            if (departments.Count == 0)
            {
                MessageDialog.Show("该单位下没有部门", "提示");
                return;
            }

            // 打开分组排序窗口
            var dialog = new Views.GroupOrder(dataService, $"部门排序 - {node.Name}");
            dialog.SetGroups(departments);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();

            // 刷新分组树
            LoadGroups();
        }

        // 人员记录卡排序：排序 Personnel 模块部门下的人员记录
        private void ExecuteSortPersonnelRecords(GroupNode? node)
        {
            if (node == null) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show("请先选择一个项目", "提示");
                return;
            }

            // 获取该分组下的所有人员记录，按退场规则预排序（在场优先，退场组内按 DisplayIndex）
            var records = dataService.GetPersonnelRecordsByGroup(node.Id)
                .OrderBy(r => r.DisplayIndex)
                .ToList();
            records = PersonnelRecordsViewModel.SortByExitTime(records);

            if (records.Count == 0)
            {
                MessageDialog.Show("该分组下没有人员记录", "提示");
                return;
            }

            // 构建标题
            string title = $"人员记录卡排序 - {node.Name}";
            if (!string.IsNullOrEmpty(node.Tag))
                title += $"_{node.Tag}";

            // 打开人员记录排序窗口
            var dialog = new Views.PersonnelCardOrder(dataService, title);
            dialog.SetRecords(records);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();

            // 刷新显示
            _owner.PersonnelRecords.LoadRecords(node.Id);
        }

        // 右键菜单命令实现
        private void ExecuteAddDateForNode(GroupNode? node)
        {
            if (node == null) return;

            string module = _owner.GetCurrentModuleKey();
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            if (module == ModuleNames.Personnel)
            {
                // 人员模块：在选中单位下添加部门
                if (node.ParentId != null)
                {
                    MessageDialog.Show("只能为单位添加部门", "提示");
                    return;
                }

                var inputDialog = new InputDialog("请输入部门名称");
                inputDialog.Owner = Application.Current.MainWindow;
                if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.InputText)) return;

                string deptName = inputDialog.InputText.Trim();
                if (node.Children.Any(c => c.Name == deptName))
                {
                    MessageDialog.Show($"部门“{deptName}”已存在，请勿重复创建。", "提示");
                    return;
                }

                var newGroup = new Group
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProjectId = dataService.ProjectId,
                    Module = ModuleNames.Personnel,
                    ParentId = node.Id,
                    Name = deptName,
                    Order = node.Children.Count,
                    CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
                };

                if (dataService.AddGroup(newGroup))
                {
                    InsertGroup(newGroup);
                }
                else
                {
                    MessageDialog.Show("创建部门失败，请重试。", "错误");
                }
                return;
            }

            // 隐患模块：只能在年月分组（根节点）下添加日期
            if (node.ParentId != null)
            {
                MessageDialog.Show("只能为年月分组添加日期", "提示");
                return;
            }

            var dialog = new AddDateDialog(dataService, node.Id, node.Name);
            dialog.Owner = Application.Current.MainWindow;
            dialog.GroupCreated += (groupId, groupName, parentId, tag) =>
            {
                InsertGroup(new Group
                {
                    Id = groupId,
                    Name = groupName,
                    ParentId = parentId,
                    Module = ModuleNames.Hazard,
                    ProjectId = dataService.ProjectId,
                    Tag = tag,
                    Order = 0,
                    CreatedDate = DateTime.Now.ToString("yyyy-MM-dd")
                });
                var newNode = FindNodeById(groupId);
                if (newNode != null) SelectedNode = newNode;
            };
            dialog.ShowDialog();
        }

        private void ExecuteAddRecordForNode(GroupNode? node)
        {
            if (node == null) return;
            string module = _owner.GetCurrentModuleKey();
            if (module == ModuleNames.Personnel)
                _owner.PersonnelRecords.AddNewRecord(node.Id);
            else
                _owner.Records.AddRecordForGroupCommand.Execute(node.Id);
        }

        private void ExecuteBatchPersonnelEntry(GroupNode? node)
        {
            if (node == null) return;
            _owner.ExecuteBatchPersonnelEntry(node);
        }

        private void ExecuteBatchHazardEntry(GroupNode? node)
        {
            if (node == null) return;
            _owner.ExecuteBatchHazardEntry(node);
        }

        private void ExecuteRenameGroup(GroupNode? node)
        {
            if (node == null) return;

            string module = _owner.GetCurrentModuleKey();

            // ===== 人员模块：单位和部门都支持自定义重命名 =====
            if (module == ModuleNames.Personnel)
            {
                string levelName = string.IsNullOrEmpty(node.ParentId) ? "单位" : "部门";
                var inputDialog = new InputDialog($"请输入{levelName}新名称", node.Name);
                inputDialog.Owner = Application.Current.MainWindow;
                if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.InputText)) return;

                string newName = inputDialog.InputText.Trim();
                if (newName == node.Name) return; // 无变化

                // 同级去重
                var siblings = !string.IsNullOrEmpty(node.ParentId)
                    ? (FindNodeById(node.ParentId)?.Children ?? RootNodes)
                    : RootNodes;
                if (siblings.Any(c => c.Name == newName && c.Id != node.Id))
                {
                    MessageDialog.Show($"“{newName}”已存在，请使用其他名称。", "提示");
                    return;
                }

                var dataService = _owner.GetProjectDataService();
                if (dataService == null) return;

                var groupToUpdate = _currentGroups.FirstOrDefault(g => g.Id == node.Id);
                if (groupToUpdate == null) return;

                groupToUpdate.Name = newName;
                if (dataService.UpdateGroup(groupToUpdate))
                {
                    LoadGroups();
                    var newNode = FindNodeById(node.Id);
                    if (newNode != null) SelectedNode = newNode;
                }
                else
                {
                    MessageDialog.Show("重命名失败，请重试。", "错误");
                }
                return;
            }

            // ===== 隐患模块：原有逻辑 =====
            // 仅日期分组（有父节点）支持重命名
            if (string.IsNullOrEmpty(node.ParentId))
            {
                MessageDialog.Show("年月分组不支持重命名", "提示");
                return;
            }

            // 获取父节点（顶级分组）以获取年份和月份
            var parentNode = FindNodeById(node.ParentId);
            if (parentNode == null)
            {
                MessageDialog.Show("找不到所属的年月分组", "错误");
                return;
            }

            // 从父节点名称中提取年份和月份（例如 "2026年06月" -> year=2026, month=6）
            var parentMatch = Regex.Match(parentNode.Name, @"(\d{4})年(\d{1,2})月");
            if (!parentMatch.Success)
            {
                MessageDialog.Show("无法解析父节点年份和月份", "错误");
                return;
            }
            int year = int.Parse(parentMatch.Groups[1].Value);
            int month = int.Parse(parentMatch.Groups[2].Value);
            int daysInMonth = DateTime.DaysInMonth(year, month);

            // 获取当前分组名称中的原日期
            var currentMatch = Regex.Match(node.Name, @"(\d{1,2})月(\d{1,2})日");
            if (!currentMatch.Success)
            {
                MessageDialog.Show("无法解析当前日期", "错误");
                return;
            }
            int currentDay = int.Parse(currentMatch.Groups[2].Value);

            // 弹出重命名对话框（输入两位数字）
            var dialog = new RenameDateDialog(year, month, currentDay, daysInMonth);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() != true) return;

            int newDay = dialog.NewDay;
            if (newDay == currentDay) return; // 无变化

            // 构建新名称（同年同月，只改日）
            string hazardNewName = $"{month:D2}月{newDay:D2}日";

            // 检查同级分组中是否存在与新名称同名的分组
            var hazardSiblings = parentNode.Children;
            var existingSameNameGroup = hazardSiblings.FirstOrDefault(c => c.Name == hazardNewName && c.Id != node.Id);

            var hazardDataService = _owner.GetProjectDataService();
            if (hazardDataService == null) return;

            var hazardGroupToUpdate = _currentGroups.FirstOrDefault(g => g.Id == node.Id);
            if (hazardGroupToUpdate == null) return;

            if (existingSameNameGroup == null)
            {
                // 目标日期不存在，直接修改名称，保留原标签
                hazardGroupToUpdate.Name = hazardNewName;
                if (hazardDataService.UpdateGroup(hazardGroupToUpdate))
                {
                    LoadGroups();
                    var newNode = FindNodeById(node.Id);
                    if (newNode != null) SelectedNode = newNode;
                }
                else
                {
                    MessageDialog.Show("重命名失败", "错误");
                }
            }
            else
            {
                // 目标日期已存在，不修改名称，只修改标签
                string? originalTag = hazardGroupToUpdate.Tag;
                string newTag = GenerateUniqueTagForExistingDate(originalTag, hazardNewName, parentNode.Id, node.Id);
                hazardGroupToUpdate.Tag = newTag;
                if (hazardDataService.UpdateGroup(hazardGroupToUpdate))
                {
                    node.Tag = newTag;
                    // 刷新显示（标签已更新）
                    // 可选：提示用户
                    MessageDialog.Show($"目标日期“{hazardNewName}”已存在，已为当前分组设置标签“{newTag}”。您可以在右键菜单中修改标签。", "提示");
                }
                else
                {
                    MessageDialog.Show("更新标签失败", "错误");
                }
            }
        }

        // 辅助方法：生成唯一标签（原标签+"Move"）
        private string GenerateUniqueTagForExistingDate(string? originalTag, string targetDateName, string parentId, string excludeGroupId)
        {
            const int MaxTagLength = 20;
            string baseTag;
            if (string.IsNullOrEmpty(originalTag))
                baseTag = "未命名Move";
            else
                baseTag = originalTag + "Move";

            if (baseTag.Length > MaxTagLength)
            {
                // 原标签过长，降级为数字标签
                return GenerateNumericTagForDate(targetDateName, parentId, excludeGroupId);
            }

            string candidateTag = baseTag;
            int suffix = 1;
            while (IsTagDuplicate(parentId, targetDateName, candidateTag, excludeGroupId))
            {
                string suffixStr = suffix.ToString();
                string temp = baseTag + suffixStr;
                if (temp.Length > MaxTagLength)
                {
                    // 超长，降级为数字标签
                    return GenerateNumericTagForDate(targetDateName, parentId, excludeGroupId);
                }
                candidateTag = temp;
                suffix++;
            }
            return candidateTag;
        }

        private string GenerateNumericTagForDate(string dateName, string parentId, string excludeGroupId)
        {
            int tagNum = 2;
            while (IsTagDuplicate(parentId, dateName, tagNum.ToString(), excludeGroupId))
                tagNum++;
            return tagNum.ToString();
        }

        private bool IsTagDuplicate(string parentId, string name, string tag, string excludeGroupId)
        {
            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return false;
            return dataService.IsTagDuplicate(parentId, name, tag, excludeGroupId);
        }

        private void ExecuteDeleteGroup(GroupNode? node)
        {
            if (node == null) return;

            string displayName;
            if (!string.IsNullOrEmpty(node.ParentId))
            {
                var parent = FindNodeById(node.ParentId);
                if (parent != null && parent.Name.Contains("年") && parent.Name.Contains("月") && node.Name.Contains("月") && node.Name.Contains("日"))
                {
                    string yearMonth = parent.Name.Trim();
                    string? dayPart = node.Name.Split('月').LastOrDefault()?.Trim();
                    if (!string.IsNullOrEmpty(dayPart))
                        displayName = $"{yearMonth}{dayPart}";
                    else
                        displayName = $"{yearMonth} - {node.Name}";
                }
                else
                {
                    displayName = $"{parent?.Name} - {node.Name}";
                }
            }
            else
            {
                displayName = node.Name;
            }

            var confirmDialog = new ConfirmDialog($"确定要删除分组 【 {displayName} 】 及其下的所有记录吗？");
            confirmDialog.Owner = App.Current.MainWindow;
            if (confirmDialog.ShowDialog() != true) return;

            var dataService = _owner.GetProjectDataService();
            if (dataService == null) return;

            // 确定删除后的候选选中节点（在 UI 移除前计算）
            string? candidateId = null;
            var parentNode = !string.IsNullOrEmpty(node.ParentId) ? FindNodeById(node.ParentId) : null;
            var siblings = parentNode != null ? parentNode.Children : RootNodes;
            int idx = siblings.IndexOf(node);
            if (siblings.Count > 1)
            {
                if (idx < siblings.Count - 1)
                    candidateId = siblings[idx + 1].Id;
                else
                    candidateId = siblings[idx - 1].Id;
            }
            else if (parentNode != null)
            {
                candidateId = parentNode.Id;
            }

            // 执行数据库删除（会级联删除子分组、记录和附件）
            if (!dataService.DeleteGroup(node.Id))
            {
                MessageDialog.Show("删除分组失败", "错误");
                return;
            }

            // 从内部缓存 _currentGroups 中删除该分组及其所有子孙分组
            RemoveGroupFromCache(node.Id);

            // 从 UI 树中移除节点（增量删除，保留其他节点的展开状态）
            // 重要：先设置新选中再移除，避免 WPF TreeView 在移除选中项后自动选中父级（年月分组）触发月度统计
            if (candidateId != null)
            {
                // 1. 先设置新选中（ViewModel 层面：旧节点取消选中 + 新节点选中）
                SetSelectedNodeById(candidateId);
                // 2. 强制刷新 WPF 绑定引擎，确保 TreeView 已确认旧节点取消选中
                Application.Current.Dispatcher.Invoke(
                    new Action(() => { }),
                    System.Windows.Threading.DispatcherPriority.DataBind);
                // 3. 确保记录已加载
                _owner.Records.LoadRecords(candidateId);
            }
            else
            {
                SelectedNode = null;
            }

            // 4. 此时旧节点已不是选中状态，移除不会触发自动选中
            if (parentNode != null)
            {
                parentNode.Children.Remove(node);
            }
            else
            {
                RootNodes.Remove(node);
            }

            // 如果删除的是顶级分组，可能需要重新排序（可选，但为了保持顺序一致，不重新加载树）
            // 注意：删除后 RootNodes 的顺序可能因缺失一个而需要重排，但由于原始顺序是降序，直接移除即可。
        }

        /// <summary>
        /// 从 _currentGroups 缓存中删除指定 Id 的分组及其所有子孙分组
        /// </summary>
        private void RemoveGroupFromCache(string groupId)
        {
            // 找到要删除的分组
            var groupToDelete = _currentGroups.FirstOrDefault(g => g.Id == groupId);
            if (groupToDelete == null) return;

            // 收集所有子孙分组的 Id（递归）
            var idsToRemove = new List<string> { groupId };
            CollectDescendantGroupIds(groupId, idsToRemove);

            // 从缓存中移除
            _currentGroups.RemoveAll(g => idsToRemove.Contains(g.Id));
        }

        /// <summary>
        /// 递归收集指定分组的所有子孙分组 Id
        /// </summary>
        private void CollectDescendantGroupIds(string parentId, List<string> ids)
        {
            var children = _currentGroups.Where(g => g.ParentId == parentId).ToList();
            foreach (var child in children)
            {
                ids.Add(child.Id);
                CollectDescendantGroupIds(child.Id, ids);
            }
        }
    }
}
