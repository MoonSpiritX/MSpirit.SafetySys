// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MS_SafetyManagementSystem.Services;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Tools
{
    public partial class ImageProcessWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ImageProcessViewModel _vm;

        public ImageProcessWindow()
        {
            InitializeComponent();
            _vm = new ImageProcessViewModel();
            DataContext = _vm;

            AllowDrop = true;
            // 拖放统一允许（光标不显示禁止圆圈）：文件夹/不支持格式统一在 Drop 后过滤 + Toast 提示
            DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            };
            // 松手时接收：支持格式添加、文件夹递归收图，成功与忽略均 Toast 提示
            // 收集（枚举/过滤）放后台线程，大文件夹不卡 UI；去重与添加回 UI 线程
            Drop += async (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    var (images, ignored) = await Task.Run(() => ImageProcessViewModel.EnumerateImagesFromPaths(files));
                    int added = _vm.AddFilesToRotation(images);
                    if (added > 0 && ignored.Count == 0)
                    {
                        ToastService.Show($"已添加 {added} 张图片", this, 2000);
                    }
                    else if (added > 0 && ignored.Count > 0)
                    {
                        var names = string.Join("、", ignored.Select(Path.GetFileName).Take(3)!);
                        if (ignored.Count > 3) names += $" 等 {ignored.Count} 个文件";
                        ToastService.Show($"已添加 {added} 张图片，已忽略不支持的图片格式：{names}", this, 2500);
                    }
                    else if (ignored.Count > 0)
                    {
                        var names = string.Join("、", ignored.Select(Path.GetFileName).Take(3)!);
                        if (ignored.Count > 3) names += $" 等 {ignored.Count} 个文件";
                        ToastService.Show($"已忽略不支持的图片格式：{names}", this, 2500);
                    }
                    e.Handled = true;
                }
            };

            // 执行完成有失败文件时 Toast 提示（列表"状态"列同时显示"失败"）
            _vm.RotationFailed += failedNames =>
            {
                var names = string.Join("、", failedNames.Take(3));
                if (failedNames.Count > 3) names += $" 等 {failedNames.Count} 个文件";
                ToastService.Show($"方向转换失败 {failedNames.Count} 个：{names}", this, 2500);
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _vm.RotateAddFilesCommand.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>DataGrid 选中变化时同步到 ViewModel（支持 Ctrl/Shift 多选移除）</summary>
        private void RotateDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dg)
                _vm.SyncSelection(dg.SelectedItems.OfType<RotateFileItem>().ToList());
        }
    }
}
