// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dapper;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Controls;
using Serilog;
using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace MS_SafetyManagementSystem.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ConfigManager _configManager;
        private readonly SettingsService _settingsService;
        private ProjectDataService? _currentProjectDataService;
        private readonly ViewStateManager _viewStateManager;
        public string? FirstRunDateGroupId { get; private set; }

        [ObservableProperty]
        private string _currentProjectName = string.Empty;

        [ObservableProperty]
        private string _currentModuleName = string.Empty;

        /// <summary>
        /// 主窗口标题栏固定文本（项目名/模块名动态信息改由界面其他位置展示）。
        /// 版本号动态读取程序集版本（与 AboutWindow 一致），定版只改 csproj、标题自动跟随。
        /// </summary>
        public string WindowTitle
        {
            get
            {
                string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
                return $"斜月安全管理系统（个人版）V{version}";
            }
        }

        [ObservableProperty]
        private GroupTreeViewModel _groupTree;

        [ObservableProperty]
        private RecordsViewModel _records;

        [ObservableProperty]
        private PersonnelRecordsViewModel _personnelRecords;

        [ObservableProperty]
        private string _createGroupButtonText = "创建分组";

        [ObservableProperty]
        private string _addDateButtonText = "添加日期";

        [ObservableProperty]
        private string _addCardButtonText = "添加记录";

        [ObservableProperty]
        private string _clearCardsButtonText = "清空记录";

        [ObservableProperty]
        private string _deleteRecordButtonText = "删除记录";

        public MainWindowViewModel(ConfigManager configManager, SettingsService settingsService, string rootPath)
        {
            _configManager = configManager;
            _settingsService = settingsService;
            _viewStateManager = new ViewStateManager(rootPath);

            _configManager.CurrentProjectWillBeDeleted += OnCurrentProjectWillBeDeleted;

            _groupTree = new GroupTreeViewModel(this);
            _records = new RecordsViewModel(this);
            _personnelRecords = new PersonnelRecordsViewModel(this);

            // 监听人员模块选中状态变更，联动清空按钮的可用状态
            _personnelRecords.SelectedStateChanged += OnPersonnelSelectedStateChanged;

            RefreshCurrentProject();
        }

        private void OnPersonnelSelectedStateChanged()
        {
            ClearCardsCommand.NotifyCanExecuteChanged();
            DeleteRecordCommand.NotifyCanExecuteChanged();
        }

        private void OnCurrentProjectWillBeDeleted(string projectId)
        {
            _currentProjectDataService?.Dispose();
            _currentProjectDataService = null;
            GroupTree.Clear();
            Records.ClearCards();
            PersonnelRecords.ClearCollection();
        }

        public void RefreshCurrentProject()
        {
            // 验证当前项目有效性
            if (!_configManager.ValidateCurrentProject())
            {
                CurrentProjectName = "无项目";
                GroupTree.Clear();
                Records.ClearCards();
                Log.Warning("没有有效的项目，已清空界面");
                return;
            }
            Log.Information("RefreshCurrentProject 开始，当前项目Id: {ProjectId}", _configManager.GetCurrentProjectId());

            _currentProjectDataService?.Dispose();
            _currentProjectDataService = null;

            GroupTree.Clear();
            Records.ClearCards();
            PersonnelRecords.ClearCollection();

            string currentId = _configManager.GetCurrentProjectId();
            var project = _configManager.GetProjectById(currentId);
            if (project != null)
            {
                CurrentProjectName = project.Name;
                _currentProjectDataService = new ProjectDataService(project.Id, _configManager.GetProjectFolder(project.Id));
                GroupTree.LoadGroups();
                RestoreViewState();
            }
            else
            {
                CurrentProjectName = "无项目";
            }
            RefreshCurrentModule();
        }

        /// <summary>
        /// 首次运行时创建示例项目、分组和记录卡
        /// </summary>
        /// <returns>是否执行了初始化</returns>
        public bool InitializeFirstRun()
        {
            // 检查是否已有项目
            var projects = _configManager.GetProjectList();
            if (projects.Count > 0)
                return false;

            // 1. 创建“未命名项目”
            if (!_configManager.AddProject("未命名项目"))
                return false;

            string projectId = _configManager.GetCurrentProjectId();
            var projectFolder = _configManager.GetProjectFolder(projectId);
            ProjectDataService? dataService = null;
            try
            {
                dataService = new ProjectDataService(projectId, projectFolder);
                _currentProjectDataService = dataService;

                var now = DateTime.Now;

                // 2. 创建年月分组
                string yearMonthGroupName = $"{now.Year}年{now.Month:D2}月";
                var yearMonthGroup = new Group
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProjectId = projectId,
                    Module = ModuleNames.Hazard,
                    ParentId = null,
                    Name = yearMonthGroupName,
                    Order = 0,
                    CreatedDate = now.ToString("yyyy-MM-dd")
                };
                if (!dataService.AddGroup(yearMonthGroup))
                    throw new Exception("创建年月分组失败");
                Log.Information("年月分组创建成功: {Name}", yearMonthGroupName);

                // 3. 创建日期分组
                string dateGroupName = $"{now.Month:D2}月{now.Day:D2}日";
                var dateGroup = new Group
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProjectId = projectId,
                    Module = ModuleNames.Hazard,
                    ParentId = yearMonthGroup.Id,
                    Name = dateGroupName,
                    Order = 0,
                    CreatedDate = now.ToString("yyyy-MM-dd")
                };
                if (!dataService.AddGroup(dateGroup))
                    throw new Exception("创建日期分组失败");
                Log.Information("日期分组创建成功: {Name}", dateGroupName);

                // 4. 创建空记录卡
                var newRecord = new Record
                {
                    Id = Guid.NewGuid().ToString("N"),
                    GroupId = dateGroup.Id,
                    Title = "示例记录",
                    Content = "",
                    CreatedAt = now.ToString("yyyy-MM-dd HH:mm:ss"),
                    YearMonth = $"{now.Year}-{now.Month:D2}"
                };
                if (!dataService.AddRecord(newRecord))
                    throw new Exception("创建示例记录卡失败");
                Log.Information("示例记录卡创建成功");

                // 强制刷新数据库连接池，确保数据落盘
                var dbPath = dataService.GetDbPath();
                using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
                {
                    conn.Open();
                    conn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
                }

                FirstRunDateGroupId = dateGroup.Id;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "初始化示例数据失败，将删除已创建的项目");

                // 回滚：删除刚创建的项目
                try
                {
                    _configManager.DeleteProject(projectId);
                }
                catch (Exception delEx)
                {
                    Log.Error(delEx, "回滚删除项目失败");
                }

                dataService?.Dispose();
                _currentProjectDataService = null;
                return false;
            }
        }

        public void RefreshCurrentModule()
        {
            CurrentModuleName = _settingsService.GetCurrentModule() == ModuleNames.Hazard ? "隐患管理模块" : "人员管理模块";

            if (_settingsService.GetCurrentModule() == ModuleNames.Hazard)
            {
                CreateGroupButtonText = "创建分组";
                AddDateButtonText = "添加日期";
                AddCardButtonText = "添加记录";
                ClearCardsButtonText = "清空卡片"; 
                DeleteRecordButtonText = "删除卡片"; 
            }
            else
            {
                CreateGroupButtonText = "添加单位";
                AddDateButtonText = "添加部门";
                AddCardButtonText = "添加人员";
                ClearCardsButtonText = "移动卡片";
                DeleteRecordButtonText = "删除卡片";
            }
        }

        public void RefreshCurrentModuleData()
        {
            Records.ClearCards();              // 先清空记录
            PersonnelRecords.ClearCollection();     // 清空人员记录
            GroupTree.LoadGroups();            // 重新加载新模块的分组树
            RestoreViewState();                // 恢复新模块的状态（展开、选中节点，会触发记录加载）
        }

        [RelayCommand]
        private void ShowWindow()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.ShowWindow();
        }

        [RelayCommand]
        private void Exit()
        {
            // 统一走退出管理器：保存记录/视图状态等清理由 MainWindow 注册的钩子执行
            AppExitManager.RequestExit(ExitReason.TrayMenu);
        }

        public string GetCurrentModuleKey() => _settingsService.GetCurrentModule();

        public ProjectDataService? GetProjectDataService() => _currentProjectDataService;

        [RelayCommand]
        private void CreateGroup() => GroupTree.CreateGroupCommand.Execute(null);

        [RelayCommand]
        private void CreateRecord()
        {
            if (GetCurrentModuleKey() == ModuleNames.Personnel)
            {
                var selectedNode = GroupTree.SelectedNode;
                if (selectedNode != null)
                    PersonnelRecords.AddNewRecord(selectedNode.Id);
            }
            else
            {
                Records.AddRecordCommand.Execute(null);
            }
        }

        [RelayCommand]
        private void AddCard()
        {
            if (GetCurrentModuleKey() == ModuleNames.Personnel)
            {
                var selectedNode = GroupTree.SelectedNode;
                if (selectedNode != null)
                    PersonnelRecords.AddNewRecord(selectedNode.Id);
            }
            else
            {
                Records.AddRecordCommand.Execute(null);
            }
        }

        [RelayCommand(CanExecute = nameof(CanClearCards))]
        private void ClearCards()
        {
            if (GetCurrentModuleKey() == ModuleNames.Personnel)
                PersonnelRecords.MoveSelectedCards();
            else
                Records.ClearCardsContentCommand.Execute(null);
        }

        /// <summary>
        /// 从部门右键菜单"批量人员录入"调用。打开 LAN 录入窗口。
        /// </summary>
        public void ExecuteBatchPersonnelEntry(GroupNode node)
        {
            var window = new Controls.LanPersonnelEntryWindow(node.Id, node.Name);
            window.Owner = Application.Current.MainWindow;
            window.Show();
        }

        /// <summary>
        /// 从隐患年月分组右键菜单"隐患手机录入"调用。打开隐患 LAN 录入窗口。
        /// </summary>
        public void ExecuteBatchHazardEntry(GroupNode node)
        {
            // node.Name 格式如 "2026年06月"，转换为 "2026-06"
            string yearMonth = node.Name.Replace("年", "-").Replace("月", "");
            var window = new Controls.LanHazardEntryWindow(node.Id, node.Name, yearMonth);
            window.Owner = Application.Current.MainWindow;
            window.Show();
        }

        private bool CanClearCards()
        {
            if (GetCurrentModuleKey() == ModuleNames.Personnel)
                return PersonnelRecords.HasSelectedCards();
            else
                return Records.Records.Any(r => r.IsSelected);
        }

        [RelayCommand(CanExecute = nameof(CanDeleteRecord))]
        private void DeleteRecord()
        {
            if (GetCurrentModuleKey() == ModuleNames.Personnel)
            {
                PersonnelRecords.DeleteSelectedCards();
            }
            else
            {
                Records.DeleteSelectedRecordsCommand.Execute(null);
            }
        }

        private bool CanDeleteRecord()
        {
            if (GetCurrentModuleKey() == ModuleNames.Personnel)
                return PersonnelRecords.HasSelectedCards();
            else
                return Records.Records.Any(r => r.IsSelected);
        }

        // ========== 修改保存方法 ==========
        public void SaveCurrentViewState()
        {
            if (_currentProjectDataService == null) return;
            string projectId = _configManager.GetCurrentProjectId();
            string module = GetCurrentModuleKey();
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(module)) return;

            var expandedNames = GroupTree.GetExpandedTopLevelNames();
            var selectedNodeId = GroupTree.GetSelectedNodeId();

            // 获取滚动偏移
            double scrollOffset = 0;
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow != null)
            {
                scrollOffset = mainWindow.GetGroupTreeScrollOffset();
            }

            Log.Information("保存视图状态: ProjectId={ProjectId}, Module={Module}, SelectedNodeId={NodeId}, ScrollOffset={Offset}",
                projectId, module, selectedNodeId ?? "null", scrollOffset);

            _viewStateManager.SaveState(projectId, module, expandedNames, selectedNodeId ?? "", scrollOffset);
        }

        private void RestoreViewState()
        {
            if (_currentProjectDataService == null) return;
            string projectId = _configManager.GetCurrentProjectId();
            string module = GetCurrentModuleKey();
            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(module)) return;

            var (expandedNames, selectedNodeId, scrollOffset) = _viewStateManager.LoadState(projectId, module);
            Log.Information("加载视图状态: ProjectId={ProjectId}, Module={Module}, 选中节点ID={NodeId}, 滚动偏移={Offset}",
                projectId, module, selectedNodeId ?? "null", scrollOffset);

            GroupTree.SetState(expandedNames ?? new List<string>(), selectedNodeId ?? "");

            // 通知主窗口恢复滚动偏移
            // 防御：Application.Current 在 WPF Shutdown 过程中会被置为 null，加 ?. 避免退出时的 NRE
            var mainWindow = Application.Current?.MainWindow as MainWindow;
            mainWindow?.SetGroupTreeScrollOffset(scrollOffset);
        }
    }
}
