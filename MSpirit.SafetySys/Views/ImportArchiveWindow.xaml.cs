// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Helpers;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>导入向导行模型</summary>
    public partial class ImportRowViewModel : ObservableObject
    {
        public string Folder { get; set; } = "";
        public string Name { get; set; } = "";
        public string Created { get; set; } = "";
        public ProjectInfo? TargetLocalProject { get; set; }
        /// <summary>所属存档包文件路径（多包合并时用于按文件分组执行导入）</summary>
        public string SourceFile { get; set; } = "";

        [ObservableProperty]
        private bool _isSelected = true;

        [ObservableProperty]
        private string _localStatusText = "";

        public ObservableCollection<StrategyOption> StrategyOptions { get; } = new();

        [ObservableProperty]
        private StrategyOption? _selectedStrategy;
    }

    /// <summary>处理方式下拉选项（显示文本 + 策略）</summary>
    public class StrategyOption
    {
        public string Display { get; set; } = "";
        public ImportStrategy Strategy { get; set; }
        public override string ToString() => Display;
    }

    public partial class ImportArchiveWindow : FluentWindow
    {
        private readonly List<string> _archiveFiles;
        private readonly ConfigManager _configManager;
        private readonly string _rootPath;

        /// <summary>行集合（绑定 DataGrid）</summary>
        public ObservableCollection<ImportRowViewModel> Rows { get; } = new();

        public ImportArchiveWindow(List<string> archiveFiles, ConfigManager configManager)
        {
            InitializeComponent();
            _archiveFiles = archiveFiles;
            _configManager = configManager;
            _rootPath = AppServiceProvider.GetDataPathService().RootPath;
            LoadInspection();
        }

        /// <summary>遍历所有存档包读取清单，合并填充到同一个导入列表</summary>
        private void LoadInspection()
        {
            string? manifestInfo = null;
            try
            {
                foreach (var archiveFile in _archiveFiles)
                {
                    var inspection = ArchiveService.InspectArchive(archiveFile, _rootPath);

                    // 记录首个包的类型信息（单包时展示导出时间/版本）
                    if (manifestInfo == null)
                    {
                        manifestInfo = inspection.HasManifest
                            ? $"导出时间 {inspection.Manifest?.CreatedAt}"
                            : "无版本信息";
                    }

                    foreach (var item in inspection.Items)
                    {
                        var row = new ImportRowViewModel
                        {
                            Folder = item.Folder,
                            Name = item.Name,
                            Created = item.Created,
                            TargetLocalProject = item.LocalProject,
                            SourceFile = archiveFile,
                            LocalStatusText = item.LocalProject != null
                                ? $"本地已有：{item.LocalProject.Name}"
                                : "新项目"
                        };

                        if (item.LocalProject != null)
                        {
                            // 有本地匹配：提供 合并/覆盖/新建副本/跳过，默认新建副本（安全）
                            AddOption(row, "新建副本", ImportStrategy.Duplicate);
                            AddOption(row, "合并", ImportStrategy.Merge);
                            AddOption(row, "覆盖", ImportStrategy.Overwrite);
                            AddOption(row, "跳过", ImportStrategy.Skip);
                            row.SelectedStrategy = row.StrategyOptions.First(o => o.Strategy == ImportStrategy.Duplicate);
                        }
                        else
                        {
                            // 无本地匹配：直接导入或跳过
                            AddOption(row, "导入", ImportStrategy.Duplicate);
                            AddOption(row, "跳过", ImportStrategy.Skip);
                            row.SelectedStrategy = row.StrategyOptions.First(o => o.Strategy == ImportStrategy.Duplicate);
                        }

                        Rows.Add(row);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "读取存档包失败");
                MessageDialog.Show($"读取存档包失败：{ex.Message}", "错误");
                Close();
                return;
            }

            if (Rows.Count == 0)
            {
                MessageDialog.Show("所选存档包中没有找到项目数据", "提示");
                Close();
                return;
            }

            // 顶部信息：多个包显示汇总；单包保留导出时间/版本信息
            ArchiveInfoText.Text = _archiveFiles.Count > 1
                ? $"已选择 {_archiveFiles.Count} 个存档包，共 {Rows.Count} 个项目（可逐项选择处理方式）"
                : $"存档包：{System.IO.Path.GetFileName(_archiveFiles[0])}（{Rows.Count} 个项目，{manifestInfo}）";
        }

        private static void AddOption(ImportRowViewModel row, string display, ImportStrategy strategy)
        {
            row.StrategyOptions.Add(new StrategyOption { Display = display, Strategy = strategy });
        }

        /// <summary>
        /// 处理方式下拉框选择变更：显式写回行模型的 SelectedStrategy。
        /// DataGrid 模板列中 ComboBox 的 TwoWay 绑定在部分环境下不可靠（实测选择不生效、
        /// 始终以默认"新建副本"执行），这里用事件强制写回，确保用户选择真正生效。
        /// </summary>
        private void StrategyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo && combo.SelectedItem is StrategyOption option
                && combo.DataContext is ImportRowViewModel row)
            {
                row.SelectedStrategy = option;
            }
        }

        private async void StartImport_Click(object sender, RoutedEventArgs e)
        {
            var selectedRows = Rows.Where(r => r.IsSelected && r.SelectedStrategy != null).ToList();
            if (selectedRows.Count == 0)
            {
                MessageDialog.Show("未勾选任何项目", "提示");
                return;
            }

            // 覆盖是破坏性操作，二次确认（项目专用确认弹窗）
            if (selectedRows.Any(r => r.SelectedStrategy!.Strategy == ImportStrategy.Overwrite))
            {
                var result = MessageDialog.Confirm(this,
                    "选择了「覆盖」方式，将删除本地同名项目并整体替换，此操作不可逆！\n确定继续吗？",
                    "覆盖确认");
                if (result != true) return;
            }

            SetBusy(true, "正在导入，请稍候…");
            try
            {
                // 多包合并：按所属存档包分组执行（每个包独立解压/导入），汇总结果
                var allResults = new List<ArchiveImportResult>();
                foreach (var group in selectedRows.GroupBy(r => r.SourceFile))
                {
                    var plans = group.Select(r => new ArchiveImportPlan
                    {
                        Folder = r.Folder,
                        Name = r.Name,
                        Created = r.Created,
                        Strategy = r.SelectedStrategy!.Strategy,
                        TargetLocalProject = r.TargetLocalProject
                    }).ToList();

                    var fileResults = await System.Threading.Tasks.Task.Run(() =>
                        ArchiveService.ImportArchive(_rootPath, group.Key, plans));
                    allResults.AddRange(fileResults);
                }

                SetBusy(false, "");
                ShowImportResults(allResults);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "导入存档失败");
                SetBusy(false, "");
                MessageDialog.Show($"导入失败：{ex.Message}", "错误");
            }
        }

        /// <summary>
        /// 展示逐项目导入结果：每个项目显示是否成功及进行了怎样的操作。
        /// 全部成功显示「导入完成」，有失败项显示「部分失败」并列出失败项。
        /// </summary>
        private void ShowImportResults(List<ArchiveImportResult> results)
        {
            if (results.Count == 0)
            {
                MessageDialog.Show("没有可导入的项目", "提示");
                return;
            }

            var sb = new System.Text.StringBuilder();
            bool allSuccess = true;
            foreach (var r in results)
            {
                sb.AppendLine(r.Success ? $"✔ {r.Name}：{r.Message}" : $"✘ {r.Name}：{r.Message}");
                if (!r.Success) allSuccess = false;
            }

            string title = allSuccess ? "导入完成" : "导入完成（部分失败）";
            MessageDialog.Show(sb.ToString().TrimEnd(), title);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void SetBusy(bool busy, string status)
        {
            StartImportButton.IsEnabled = !busy;
            ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = status;
        }
    }
}
