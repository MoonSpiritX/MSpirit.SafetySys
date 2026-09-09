// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using WpfWinForms = System.Windows.Forms;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>
    /// 数据目录恢复引导窗口：程序启动时检测到"上次数据目录失效且浅扫无果"时弹出。
    /// 三种找回方式：手动选择目录（FolderBrowserDialog）/ 搜索其他位置（全盘深扫，异步可取消）/ 使用新位置继续。
    /// </summary>
    public partial class DataRootRecoveryWindow : FluentWindow
    {
        private readonly string _recordedRoot;
        private CancellationTokenSource? _cts;
        private bool _searchDone;

        /// <summary>用户最终选择的数据根路径；null 表示未选择</summary>
        public string? SelectedRootPath { get; private set; }

        /// <summary>用户选择"使用新位置继续"</summary>
        public bool UseNewLocation { get; private set; }

        public DataRootRecoveryWindow(string recordedRoot)
        {
            _recordedRoot = recordedRoot ?? "";
            InitializeComponent();
            RecordedRootTextBlock.Text = string.IsNullOrEmpty(_recordedRoot)
                ? "（无上次记录）"
                : _recordedRoot;
        }

        private class SearchResultItem
        {
            public string Path { get; set; } = "";
            public string Display => Path;
        }

        // ===== 手动选择目录 =====

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfWinForms.FolderBrowserDialog
            {
                Description = "请选择包含斜月安全管理系统数据的目录（含 ProjectsDate 文件夹的目录）",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() != WpfWinForms.DialogResult.OK)
                return;

            string chosen = dialog.SelectedPath.Trim();
            if (!DataRootLocator.IsValidDataRoot(chosen))
            {
                var confirm = new ConfirmDialog(
                    "该目录看起来不是有效的数据目录（未找到 ProjectsDate 文件夹或数据根提示文件）。\n\n仍要使用此目录吗？",
                    "目录可能无效");
                confirm.Owner = this;
                if (confirm.ShowDialog() != true)
                    return;
            }

            SelectedRootPath = chosen;
            DialogResult = true;
        }

        // ===== 搜索其他位置（全盘深扫） =====

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchButton.IsEnabled = false;
            BrowseButton.IsEnabled = false;
            UseNewButton.IsEnabled = false;
            ActionPanel.Visibility = Visibility.Collapsed;
            SearchProgressPanel.Visibility = Visibility.Visible;

            _cts = new CancellationTokenSource();
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                int percent = p.Total > 0 ? (int)(p.Done * 100.0 / p.Total) : 0;
                SearchProgressBar.Value = percent;
                SearchProgressText.Text = $"正在搜索磁盘（{p.Done}/{p.Total}）…可随时取消";
            });

            try
            {
                var results = await DataRootLocator.SearchDeepAsync(_cts.Token, progress);
                _searchDone = true;
                ShowSearchResults(results);
            }
            catch (OperationCanceledException)
            {
                Log.Information("[DataRoot] 全盘搜索已取消");
                SearchProgressText.Text = "搜索已取消。";
                // 恢复按钮行，允许重新搜索或改用其他方式
                ActionPanel.Visibility = Visibility.Visible;
                SearchButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
                UseNewButton.IsEnabled = true;
                SearchProgressPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DataRoot] 全盘搜索失败");
                SearchProgressText.Text = $"搜索失败：{ex.Message}";
                ActionPanel.Visibility = Visibility.Visible;
                SearchButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
                UseNewButton.IsEnabled = true;
            }
        }

        private void ShowSearchResults(List<string> results)
        {
            SearchProgressPanel.Visibility = Visibility.Collapsed;

            if (results.Count == 0)
            {
                // 没找到：留在窗口，恢复按钮行并提示
                SearchResultTitle.Text = "未在任何磁盘找到数据目录。";
                SearchResultTitle.Visibility = Visibility.Visible;
                SearchResultListBox.Visibility = Visibility.Collapsed;
                ResultActionPanel.Visibility = Visibility.Collapsed;
                ActionPanel.Visibility = Visibility.Visible;
                SearchButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
                UseNewButton.IsEnabled = true;
                return;
            }

            SearchResultListBox.ItemsSource = results.Select(r => new SearchResultItem { Path = r }).ToList();
            SearchResultPanel.Visibility = Visibility.Visible;
            ResultActionPanel.Visibility = Visibility.Visible;
            SearchResultListBox.SelectedIndex = 0;
        }

        private void PickResultButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchResultListBox.SelectedItem is SearchResultItem item)
            {
                SelectedRootPath = item.Path;
                DialogResult = true;
            }
        }

        private void CancelSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_searchDone && _cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel(); // 搜索中：先取消
                return;
            }
            // 搜索结果已展示：回到主按钮行
            SearchResultPanel.Visibility = Visibility.Collapsed;
            ResultActionPanel.Visibility = Visibility.Collapsed;
            ActionPanel.Visibility = Visibility.Visible;
            SearchButton.IsEnabled = true;
            BrowseButton.IsEnabled = true;
            UseNewButton.IsEnabled = true;
        }

        // ===== 使用新位置继续 =====

        private void UseNewButton_Click(object sender, RoutedEventArgs e)
        {
            UseNewLocation = true;
            DialogResult = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts?.Cancel();
            base.OnClosed(e);
        }
    }
}
