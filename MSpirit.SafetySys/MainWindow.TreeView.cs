// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Hazard;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// MainWindow 分部类：分组树交互与统计联动（选中根节点显示月度/人员统计表、节点展开与滚动定位辅助）。
    /// 2026-08-13 从 MainWindow.xaml.cs 拆出，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow
    {
        private void LoadGroupTree()
        {
            _viewModel.GroupTree.LoadGroups();
        }

        private async void GroupTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as GroupNode;
            if (node == null)
            {
                _currentHazardPanel?.ReloadRecordsView();
                _currentPersonnelPanel?.ReloadRecordsView();
                _currentHazardPanel?.UpdateStatusText();
                _currentPersonnelPanel?.UpdateStatusText();
                return;
            }

            var dataService = _viewModel.GetProjectDataService();
            if (dataService != null && string.IsNullOrEmpty(node.ParentId) && _viewModel.GetCurrentModuleKey() == "Hazard")
            {
                // 根节点：先清空显示内容，避免 LoadRecords 触发记录卡闪烁
                _currentHazardPanel?.WorkAreaInstance.ClearContent();
                _viewModel.GroupTree.SelectedNode = node;
                var vm = new MonthlyStatisticsViewModel(dataService, node.Id, node.Name);
                await vm.LoadDataAsync();
                var statsView = new MonthlyStatisticsView(vm);
                _currentHazardPanel?.ShowMonthlyStatistics(statsView);
            }
            else if (dataService != null && string.IsNullOrEmpty(node.ParentId) && _viewModel.GetCurrentModuleKey() == "Personnel")
            {
                // 根节点：先清空显示内容，避免 LoadRecords 触发记录卡闪烁
                _currentPersonnelPanel?.WorkAreaInstance.ClearContent();
                _viewModel.GroupTree.SelectedNode = node;
                await ShowPersonnelStatisticsAsync(node);
            }
            else
            {
                if (_viewModel.GroupTree.SelectedNode != node)
                    _viewModel.GroupTree.SelectedNode = node;
                _currentHazardPanel?.ReloadRecordsView();
                _currentPersonnelPanel?.ReloadRecordsView();
                _viewModel.Records.LoadRecords(node.Id);
            }

            // 更新记录显示区标题
            _currentHazardPanel?.UpdateStatusText();
            _currentPersonnelPanel?.UpdateStatusText();
        }

        /// <summary>
        /// 构建并显示人员统计数据表（根节点专用），含标题过滤订阅
        /// </summary>
        private async Task ShowPersonnelStatisticsAsync(GroupNode node)
        {
            var dataService = _viewModel.GetProjectDataService();
            if (dataService == null) return;

            var vm = new PersonnelStatisticsViewModel(dataService, node.Id, node.Name);
            await vm.LoadDataAsync();
            var statsView = new PersonnelStatisticsView(vm);
            _currentPersonnelPanel?.ShowPersonnelStatistics(statsView);

            // 退订旧过滤事件，订阅新 VM 的过滤变更 → 更新标题
            if (_currentPersonnelStatsFilterHandler != null)
            {
                // 无法退订旧 VM，但旧 VM 会被 GC，这里清理引用即可
                _currentPersonnelStatsFilterHandler = null;
            }
            var panel = _currentPersonnelPanel;
            _currentPersonnelStatsFilterHandler = () =>
            {
                string scope = vm.PersonnelStatus == "全部人员" ? "" : $"·{vm.PersonnelStatus}";
                string category = vm.Category == "全部人员" ? "" : $"·{vm.Category}";
                string filter = (scope + category).TrimStart('·');
                string suffix = string.IsNullOrEmpty(filter) ? "" : $" ({filter})";
                panel?.SetStatusTextDirect($"【人员统计】{vm.GroupName}_{vm.FilteredCount}条{suffix}");
            };
            vm.FilterChanged += _currentPersonnelStatsFilterHandler;

            // 初始标题
            _currentPersonnelStatsFilterHandler();
        }

        private void OnScrollToNodeRequested(string nodeId)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var node = _viewModel.GroupTree.FindNodeById(nodeId);
                if (node == null) return;

                ExpandAncestors(node);
                var treeView = _currentHazardPanel?.TreeView ?? _currentPersonnelPanel?.TreeView;
                treeView?.UpdateLayout();

                TryBringNodeIntoView(node, 0);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private async void TryBringNodeIntoView(GroupNode node, int retryCount)
        {
            if (retryCount > 10) return;

            await Task.Delay(50);

            var treeView = _currentHazardPanel?.TreeView ?? _currentPersonnelPanel?.TreeView;
            if (treeView == null) return;

            var scrollViewer = FindVisualChild<ScrollViewer>(treeView);
            if (scrollViewer == null || scrollViewer.ViewportHeight == 0)
            {
                TryBringNodeIntoView(node, retryCount + 1);
                return;
            }

            var container = treeView.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
            if (container != null)
            {
                container.BringIntoView();
                await Dispatcher.InvokeAsync(() =>
                {
                    var transform = container.TransformToAncestor(scrollViewer);
                    var pos = transform.Transform(new Point(0, 0));
                    if (pos.Y > scrollViewer.ViewportHeight || pos.Y < 0)
                        scrollViewer.ScrollToVerticalOffset(pos.Y - 30);
                    container.Focus();
                }, DispatcherPriority.Render);
                return;
            }

            var parentId = node.ParentId;
            if (!string.IsNullOrEmpty(parentId))
            {
                var parentNode = _viewModel.GroupTree.FindNodeById(parentId);
                if (parentNode != null && !parentNode.IsExpanded)
                {
                    parentNode.IsExpanded = true;
                    treeView.UpdateLayout();
                }
            }

            TryBringNodeIntoView(node, retryCount + 1);
        }

        private void ExpandAncestors(GroupNode node)
        {
            var parentId = node.ParentId;
            while (!string.IsNullOrEmpty(parentId))
            {
                var parentNode = _viewModel.GroupTree.FindNodeById(parentId);
                if (parentNode != null)
                {
                    parentNode.IsExpanded = true;
                    parentId = parentNode.ParentId;
                }
                else
                    break;
            }
        }

        public double GetGroupTreeScrollOffset()
        {
            return _currentHazardPanel?.GetGroupTreeScrollOffset()
                ?? _currentPersonnelPanel?.GetGroupTreeScrollOffset()
                ?? 0;
        }

        public void SetGroupTreeScrollOffset(double offset)
        {
            _currentHazardPanel?.SetGroupTreeScrollOffset(offset);
            _currentPersonnelPanel?.SetGroupTreeScrollOffset(offset);
        }
    }
}
