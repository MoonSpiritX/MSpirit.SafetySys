// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using Serilog;

namespace MS_SafetyManagementSystem.Views
{
    public partial class ProjectItemViewModel : ObservableObject
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Created { get; set; } = "";
        public int Index { get; set; }

        [ObservableProperty]
        private bool _isSelected;

        /// <summary>选中状态变化回调（宿主 ViewModel 注入，用于刷新统计与全选状态）</summary>
        public Action<ProjectItemViewModel>? SelectionChanged;

        partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this);
    }

    public partial class ManageProjectsViewModel : ObservableObject
    {
        private readonly ConfigManager _configManager;
        private readonly Func<Window> _getOwnerWindow;
        private List<ProjectInfo> _allProjects = new();

        /// <summary>已勾选的项目 ID 集合（搜索重建列表时用于保留勾选状态）</summary>
        private readonly HashSet<string> _selectedIds = new();

        [ObservableProperty]
        private ObservableCollection<ProjectItemViewModel> _projects = new();

        [ObservableProperty]
        private ProjectItemViewModel? _selectedProject;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _currentProjectName = string.Empty;

        [ObservableProperty]
        private string _selectedCountText = "已选 0 项";

        partial void OnSelectedProjectChanged(ProjectItemViewModel? value)
        {
            // 刷新所有依赖选中项目的命令
            TopCommand.NotifyCanExecuteChanged();
            UpCommand.NotifyCanExecuteChanged();
            DownCommand.NotifyCanExecuteChanged();
            BottomCommand.NotifyCanExecuteChanged();
            RenameCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            SelectProjectCommand.NotifyCanExecuteChanged();
        }

        partial void OnSearchTextChanged(string value)
        {
            FilterProjects();
        }

        private void FilterProjects()
        {
            try
            {
                Projects.Clear();
                var filtered = string.IsNullOrWhiteSpace(SearchText)
                    ? _allProjects
                    : _allProjects.Where(p => p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

                int index = 1;
                foreach (var proj in filtered)
                {
                    var item = new ProjectItemViewModel
                    {
                        Id = proj.Id,
                        Name = proj.Name,
                        Created = proj.Created,
                        Index = index++,
                        IsSelected = _selectedIds.Contains(proj.Id)  // 搜索重建列表时保留勾选状态
                    };
                    item.SelectionChanged = OnRowSelectionChanged;
                    Projects.Add(item);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "筛选项目列表失败");
                MessageDialog.Show($"筛选项目时发生错误: {ex.Message}", "错误");
            }
        }

        public ManageProjectsViewModel(ConfigManager configManager, Func<Window> getOwnerWindow)
        {
            _configManager = configManager;
            _getOwnerWindow = getOwnerWindow;
            LoadProjects();

            // 标题栏固定显示窗口名（当前加载项目已在主窗口标题栏显示，此处不再重复）
            CurrentProjectName = "项目及存档管理";
        }

        private void LoadProjects()
        {
            try
            {
                _allProjects = _configManager.GetProjectList();
                FilterProjects();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载项目列表失败");
                MessageDialog.Show($"加载项目列表失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand]
        private void ClearSearch()
        {
            try
            {
                SearchText = string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "清除搜索框失败");
                MessageDialog.Show($"清除搜索框时发生错误: {ex.Message}", "错误");
            }
        }

        /// <summary>行勾选状态变化回调：同步选中集合并刷新统计</summary>
        private void OnRowSelectionChanged(ProjectItemViewModel row)
        {
            if (row.IsSelected) _selectedIds.Add(row.Id);
            else _selectedIds.Remove(row.Id);
            UpdateSelectionSummary();
        }

        /// <summary>刷新"已选 N 项"文本</summary>
        private void UpdateSelectionSummary()
        {
            int count = Projects.Count(p => p.IsSelected);
            SelectedCountText = $"已选 {count} 项";
        }

        /// <summary>批量设置勾选状态（false=全不选，true=全选）</summary>
        public void SetSelectAll(bool selectAll)
        {
            foreach (var p in Projects) p.IsSelected = selectAll;
        }

        /// <summary>全选 / 取消 切换（底部按钮）：全部已选则取消，否则全选</summary>
        [RelayCommand]
        private void ToggleSelectAll()
        {
            bool allSelected = Projects.Count > 0 && Projects.All(p => p.IsSelected);
            SetSelectAll(!allSelected);
        }

        [RelayCommand]
        private async Task ExportSelected()
        {
            var selected = Projects.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0)
            {
                ToastService.Show("请先勾选要导出的项目", _getOwnerWindow());
                return;
            }

            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出存档",
                    Filter = "存档包 (*.msarch)|*.msarch",
                    DefaultExt = ".msarch",
                    FileName = selected.Count == 1
                        ? $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{selected[0].Name}.msarch"
                        : $"{DateTime.Now:yyyy-MM-dd_HHmmss}_多个项目.msarch"
                };
                if (dlg.ShowDialog(_getOwnerWindow()) != true) return;

                var infos = selected.Select(s => new ProjectInfo
                {
                    Id = s.Id,
                    Name = s.Name,
                    Created = s.Created
                }).ToList();

                string rootPath = new DataPathService().RootPath;
                await System.Threading.Tasks.Task.Run(() =>
                    ArchiveService.ExportProjects(rootPath, infos, dlg.FileName));

                MessageDialog.Show($"导出完成：{System.IO.Path.GetFileName(dlg.FileName)}", "成功");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "导出存档失败");
                MessageDialog.Show($"导出失败：{ex.Message}", "错误");
            }
        }

        [RelayCommand]
        private void ImportArchive()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择存档包（可多选）",
                    Filter = "存档包 (*.msarch)|*.msarch",
                    Multiselect = true
                };
                if (dlg.ShowDialog(_getOwnerWindow()) != true) return;

                // 支持多选：所有存档包合并到一个导入向导窗口处理
                var window = new ImportArchiveWindow(dlg.FileNames.ToList(), _configManager);
                window.Owner = _getOwnerWindow();
                if (window.ShowDialog() == true)
                {
                    LoadProjects(); // 导入后刷新项目列表
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "导入存档失败");
                MessageDialog.Show($"导入失败：{ex.Message}", "错误");
            }
        }

        [RelayCommand(CanExecute = nameof(CanModifyProject))]
        private void Top()
        {
            try
            {
                if (SelectedProject == null) return;
                _configManager.MoveToTop(SelectedProject.Id);
                LoadProjects();
                SelectedProject = Projects.Count > 0 ? Projects[0] : null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "置顶项目失败");
                MessageDialog.Show($"置顶项目失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand(CanExecute = nameof(CanModifyProject))]
        private void Up()
        {
            try
            {
                if (SelectedProject == null) return;
                int idx = Projects.IndexOf(SelectedProject);
                if (idx <= 0) return;
                _configManager.MoveProject(SelectedProject.Id, -1);
                LoadProjects();
                SelectedProject = idx - 1 < Projects.Count ? Projects[idx - 1] : null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "上移项目失败");
                MessageDialog.Show($"上移项目失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand(CanExecute = nameof(CanModifyProject))]
        private void Down()
        {
            try
            {
                if (SelectedProject == null) return;
                int idx = Projects.IndexOf(SelectedProject);
                if (idx < 0 || idx >= Projects.Count - 1) return;
                _configManager.MoveProject(SelectedProject.Id, 1);
                LoadProjects();
                SelectedProject = idx + 1 < Projects.Count ? Projects[idx + 1] : null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "下移项目失败");
                MessageDialog.Show($"下移项目失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand(CanExecute = nameof(CanModifyProject))]
        private void Bottom()
        {
            try
            {
                if (SelectedProject == null) return;
                _configManager.MoveToBottom(SelectedProject.Id);
                LoadProjects();
                SelectedProject = Projects.Count > 0 ? Projects[^1] : null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "移至最后失败");
                MessageDialog.Show($"移至最后失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand(CanExecute = nameof(CanModifyProject))]
        private void Rename()
        {
            try
            {
                if (SelectedProject == null) return;

                var renameDialog = new RenameProjectWindow(_configManager, SelectedProject.Id, SelectedProject.Name);
                renameDialog.Owner = _getOwnerWindow();   // 关键：设置为当前窗口
                if (renameDialog.ShowDialog() == true)
                {
                    LoadProjects();
                    var renamed = Projects.FirstOrDefault(p => p.Name == renameDialog.NewName);
                    if (renamed != null) SelectedProject = renamed;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "重命名项目失败");
                MessageDialog.Show($"重命名项目失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand]
        private void New()
        {
            try
            {
                var newProjectWindow = new NewProjectWindow(_configManager);
                newProjectWindow.Owner = _getOwnerWindow();   // 使用当前窗口作为所有者
                if (newProjectWindow.ShowDialog() == true)
                {
                    LoadProjects();
                    SelectedProject = Projects.LastOrDefault();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "新建项目失败");
                MessageDialog.Show($"新建项目失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand(CanExecute = nameof(CanModifyProject))]
        private void Delete()
        {
            try
            {
                if (SelectedProject == null) return;

                var confirmDialog = new DeleteProjectConfirmationWindow(SelectedProject.Name);
                confirmDialog.Owner = Application.Current.MainWindow;
                if (confirmDialog.ShowDialog() == true)
                {
                    int oldIndex = Projects.IndexOf(SelectedProject);
                    _configManager.DeleteProject(SelectedProject.Id);
                    LoadProjects();

                    if (Projects.Count > 0)
                    {
                        if (oldIndex < Projects.Count)
                            SelectedProject = Projects[oldIndex];
                        else
                            SelectedProject = Projects[^1];
                    }
                    else
                    {
                        SelectedProject = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "删除项目失败");
                MessageDialog.Show($"删除项目失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand(CanExecute = nameof(CanSelectProject))]
        private void SelectProject()
        {
            try
            {
                if (SelectedProject == null) return;
                _configManager.SetCurrentProjectId(SelectedProject.Id);
                var window = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
                window?.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "选择项目失败");
                MessageDialog.Show($"选择项目失败: {ex.Message}", "错误");
            }
        }

        [RelayCommand]
        private void Close()
        {
            try
            {
                var window = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
                window?.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "关闭窗口失败");
                MessageDialog.Show($"关闭窗口时发生错误: {ex.Message}", "错误");
            }
        }

        private bool CanModifyProject() => SelectedProject != null;
        private bool CanSelectProject() => SelectedProject != null;
    }
}