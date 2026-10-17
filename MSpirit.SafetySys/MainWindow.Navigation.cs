// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Hazard;
using MS_SafetyManagementSystem.Peoplemanagement;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// MainWindow 分部类：记录/卡片滚动定位（新增记录自动滚动、外部跳转到指定记录）。
    /// 2026-08-13 从 MainWindow.xaml.cs 拆出，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow
    {
        private void OnRecordAdded(string recordId)
        {
            // 滚动到新添加的记录
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                var activePanel = (System.Windows.Controls.UserControl?)_currentHazardPanel ?? _currentPersonnelPanel;
                if (activePanel == null) return;

                // 找到 ItemsControl（PersonnelRecordsView 或 Hazard RecordsView 的 ItemsControl）
                var itemsControl = FindVisualChild<ItemsControl>(activePanel);
                if (itemsControl == null) return;

                var targetRecord = _viewModel.Records.Records.FirstOrDefault(r => r.Id == recordId);
                if (targetRecord == null) return;

                // 延迟等待容器生成
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    var container = itemsControl.ItemContainerGenerator.ContainerFromItem(targetRecord) as FrameworkElement;
                    container?.BringIntoView();

                    // 额外延迟确保 ScrollViewer 完成滚动
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                }));
            }));
        }

        public void NavigateToRecord(string dateGroupId, string recordId)
        {
            var targetNode = _viewModel.GroupTree.FindNodeById(dateGroupId);
            if (targetNode == null) return;

            _viewModel.GroupTree.SelectedNode = targetNode;
            _currentHazardPanel?.ReloadRecordsView();
            _currentPersonnelPanel?.ReloadRecordsView();

            var module = _viewModel.GetCurrentModuleKey();
            // 延迟等待容器生成后再尝试滚动
            TryScrollToRecord(module, recordId, 0);
        }

        private void TryScrollToRecord(string module, string recordId, int retryCount)
        {
            if (retryCount > 8) return;

            var activePanel = (System.Windows.Controls.UserControl?)_currentHazardPanel ?? _currentPersonnelPanel;
            if (activePanel == null) return;

            System.Windows.Controls.ItemsControl? itemsControl = null;

            // 优先按已知控件名查找 RecordsItemsControl
            if (activePanel is PersonnelPanel pp && pp.WorkAreaInstance.Content is PersonnelRecordsView prv)
            {
                itemsControl = prv.RecordsItemsControl;
            }
            else if (activePanel is Hazard.HazardPanel hp && hp.WorkAreaInstance.Content is Hazard.RecordsView rv)
            {
                itemsControl = FindVisualChild<System.Windows.Controls.ItemsControl>(rv);
            }
            else
            {
                // 兜底：全树搜索
                itemsControl = FindVisualChild<System.Windows.Controls.ItemsControl>(activePanel);
            }

            if (itemsControl == null)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                    new Action(() => TryScrollToRecord(module, recordId, retryCount + 1)));
                return;
            }

            // 查找目标记录
            object? targetRecord = null;
            int targetIndex = -1;
            if (module == "Personnel")
            {
                var rec = _viewModel.PersonnelRecords.Records.FirstOrDefault(r => r.Id == recordId);
                if (rec == null) return;
                targetRecord = rec;
                targetIndex = _viewModel.PersonnelRecords.Records.IndexOf(rec);
            }
            else
            {
                var rec = _viewModel.Records.Records.FirstOrDefault(r => r.Id == recordId);
                if (rec == null) return;
                targetRecord = rec;
                targetIndex = _viewModel.Records.Records.IndexOf(rec);
            }

            // 确保生成容器后再获取
            EventHandler? onStatusReady = null;
            onStatusReady = (_, _) =>
            {
                if (itemsControl.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    itemsControl.ItemContainerGenerator.StatusChanged -= onStatusReady;
                    ScrollToItem(itemsControl, targetRecord, targetIndex);
                }
            };

            itemsControl.ItemContainerGenerator.StatusChanged += onStatusReady;
            itemsControl.UpdateLayout();

            // 如果容器已经生成，直接执行
            if (itemsControl.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
            {
                itemsControl.ItemContainerGenerator.StatusChanged -= onStatusReady;
                ScrollToItem(itemsControl, targetRecord, targetIndex);
            }
        }

        private void ScrollToItem(System.Windows.Controls.ItemsControl itemsControl, object targetRecord, int targetIndex)
        {
            // 先尝试按对象引用取容器
            var container = itemsControl.ItemContainerGenerator.ContainerFromItem(targetRecord) as System.Windows.FrameworkElement;

            // 若失败，按索引取容器
            if (container == null && targetIndex >= 0)
                container = itemsControl.ItemContainerGenerator.ContainerFromIndex(targetIndex) as System.Windows.FrameworkElement;

            if (container != null)
            {
                container.BringIntoView();
            }
        }

        /// <summary>在可视树中按类型查找后代元素（注意：仅 Visual 树，逻辑树（如 Run）需先转换）</summary>
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
