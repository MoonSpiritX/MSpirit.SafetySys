// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using Serilog;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>
    /// 选择目标分组对话框（人员模块用，显示单位→部门树形结构）
    /// </summary>
    public partial class SelectPersonnelGroupDialog : FluentWindow
    {
        /// <summary>
        /// 选中分组的 Id
        /// </summary>
        public string? SelectedGroupId { get; private set; }

        /// <summary>
        /// 选中分组的名称
        /// </summary>
        public string? SelectedGroupName { get; private set; }

        /// <summary>
        /// 选中分组是否为根节点（单位）
        /// </summary>
        public bool IsRootGroup { get; private set; }

        private readonly ProjectDataService _dataService;

        public SelectPersonnelGroupDialog(ProjectDataService dataService)
        {
            InitializeComponent();
            _dataService = dataService;
            TitleBarControl.Title = "选择目标分组";
            LoadGroups();
        }

        private void LoadGroups()
        {
            try
            {
                var groupModels = _dataService.GetAllGroups(Common.ModuleNames.Personnel).ToList();
                var rootNodes = new ObservableCollection<GroupNode>();
                BuildTree(groupModels, null, rootNodes);
                GroupTreeView.ItemsSource = rootNodes;
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "加载人员分组树失败");
                HintTextBlock.Text = "加载分组失败";
                HintTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private static void BuildTree(System.Collections.Generic.List<Group> groups, string? parentId, ObservableCollection<GroupNode> nodes)
        {
            var children = groups.Where(g =>
                (string.IsNullOrEmpty(g.ParentId) && parentId == null) ||
                (g.ParentId == parentId)
            );
            foreach (var g in children)
            {
                var node = new GroupNode { Id = g.Id, Name = g.Name, ParentId = parentId, Tag = g.Tag };
                nodes.Add(node);
                BuildTree(groups, g.Id, node.Children);
            }
        }

        private void GroupTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is GroupNode selectedNode)
            {
                string type = string.IsNullOrEmpty(selectedNode.ParentId) ? "单位" : "部门";
                HintTextBlock.Text = $"已选择：{selectedNode.Name}（{type}）";
                HintTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                HintTextBlock.Text = "请选择一个分组";
                HintTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (GroupTreeView.SelectedItem is GroupNode selectedNode)
            {
                SelectedGroupId = selectedNode.Id;
                SelectedGroupName = selectedNode.Name;
                IsRootGroup = string.IsNullOrEmpty(selectedNode.ParentId);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageDialog.Show("请先选择一个目标单位或部门。", "提示");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
