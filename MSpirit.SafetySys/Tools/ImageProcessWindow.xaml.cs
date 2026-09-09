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
            // 松手时接收：方向转换=添加拖入的图片；标注与剪裁=加载图片同目录全部（按当前激活面板分派）
            Drop += async (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    if (_vm.IsCropVisible)
                    {
                        // 裁剪：后台只做 IO 收集（含加载同目录全部），UI 线程改集合（避免跨线程修改 ObservableCollection）
                        var (images, ignored) = await Task.Run(() => ImageProcessCropViewModel.CollectDroppedFiles(files));
                        var addedPaths = _vm.CropViewModel.AddFilesReturningAdded(images);
                        int added = addedPaths.Count;
                        if (added > 0)
                        {
                            // 拖入自动选中：1 张 → 选中它；多张 → 自然排序第一张（右侧立即显示，2026-08-16 新增）
                            _vm.CropViewModel.SelectFirstAdded(addedPaths);
                            ToastService.Show($"已添加 {added} 张图片（按目录分组）", this, 2000);
                        }
                        if (ignored.Count > 0)
                        {
                            var names = string.Join("、", ignored.Select(Path.GetFileName).Take(3)!);
                            if (ignored.Count > 3) names += $" 等 {ignored.Count} 个文件";
                            ToastService.Show($"已忽略不支持的图片格式：{names}", this, 2500);
                        }
                    }
                    else
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

            // ═══ 标注与剪裁：预览 + 选区接线 ═══
            _vm.CropViewModel.PreviewReady += () =>
            {
                // 预览切换：把降采样图与摆正后原始像素尺寸交给裁剪控件（内部重置裁剪框 + 重绘标注）
                CropOverlayControl.SetPreview(_vm.CropViewModel.PreviewImage,
                    _vm.CropViewModel.PixelWidth, _vm.CropViewModel.PixelHeight);
                // 切图后清除选中标注（标注属于上一张图）
                CropOverlayControl.AnnotationTool?.Clear();
                CropOverlayControl.RefreshAnnotationsVisual();
            };
            // 注入标注状态机（Items 即标注列表，保存时读取）
            _vm.CropViewModel.AnnotationTool = new AnnotationTool();
            CropOverlayControl.AnnotationTool = _vm.CropViewModel.AnnotationTool;
            // 初始标注颜色/字号/线宽同步（下拉默认：红 / 12号 / 细；setter 仅在变化时同步，故先设默认索引）
            _vm.CropViewModel.SelectedAnnotationColorIndex = 0;
            _vm.CropViewModel.SelectedFontSizeIndex = 1;    // 12号
            _vm.CropViewModel.SelectedLineWidthIndex = 1;   // 细(2px)
            // 标注下拉"用户主动关闭"识别（2026-08-17 修复"选中后下拉又弹开"）：
            // 原 80ms 重开逻辑无法区分"打开时 Popup 未布局、被 MouseUp 误判为点击外部而自动关闭"
            // 与"用户选中项/Esc/点击他处而正常关闭"，导致选完一项后下拉又被弹回、关不掉。
            // 这里在用户主动关闭的时机打标记（SelectionChanged/Esc/窗口任意点击），
            // 重开逻辑（AnnotationCombo_DropDownOpened）据此只对"误关"重开。
            // 注：须在初始索引赋值之后挂接，避免初始化时的程序化 SelectionChanged 误打标记。
            AnnotationColorCombo.SelectionChanged += AnnotationCombo_SelectionChanged;
            AnnotationFontSizeCombo.SelectionChanged += AnnotationCombo_SelectionChanged;
            AnnotationLineWidthCombo.SelectionChanged += AnnotationCombo_SelectionChanged;
            AnnotationColorCombo.PreviewKeyDown += AnnotationCombo_PreviewKeyDown;
            AnnotationFontSizeCombo.PreviewKeyDown += AnnotationCombo_PreviewKeyDown;
            AnnotationLineWidthCombo.PreviewKeyDown += AnnotationCombo_PreviewKeyDown;
            // 点击窗口其他处（其他下拉/画布/按钮）= 主动关闭，重开逻辑不干预
            PreviewMouseLeftButtonDown += (s, e) => _userDismissedAnnotationCombo = true;
            // 标注增删/工具自动复位后刷新按钮高亮（画完一个自动回选择模式，2026-08-16）
            CropOverlayControl.AnnotationsChanged += () =>
            {
                _vm.CropViewModel.NotifyAnnotationsChanged();
                UpdateAnnotButtonStates();
            };
            // 拖拽裁剪框实时显示像素选区（同步给 ViewModel 供执行）
            CropOverlayControl.PixelSelectionChanged += (x, y, w, h) =>
            {
                _vm.CropViewModel.UpdatePixelSelection(x, y, w, h);
                _vm.CropViewModel.StatusText = $"选区：{w} × {h}px（位置 {x},{y}）";
            };
            // 覆盖原图确认（视图注入：弹确认对话框）
            _vm.CropViewModel.ConfirmOverwrite = () =>
            {
                var confirm = new Views.ConfirmDialog(
                    "覆盖原图模式会把原图移动到回收站，并保存裁剪结果（文件名带 _Edit 后缀）。\n\n确定继续吗？",
                    "确认覆盖原图");
                confirm.Owner = this;
                return confirm.ShowDialog() == true;
            };
            // 裁剪失败 Toast
            _vm.CropViewModel.CropFailed += failedNames =>
            {
                var names = string.Join("、", failedNames.Take(3));
                if (failedNames.Count > 3) names += $" 等 {failedNames.Count} 个文件";
                ToastService.Show($"标注与剪裁失败 {failedNames.Count} 个：{names}", this, 2500);
            };
            // 添加文件/文件夹成功 Toast
            _vm.CropViewModel.FilesAdded += added =>
            {
                ToastService.Show($"已添加 {added} 张图片", this, 2000);
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                // 按当前面板分派：裁剪模式 → 裁剪添加文件；否则方向转换添加文件
                if (_vm.IsCropVisible)
                    _vm.CropViewModel.AddFilesCommand.Execute(null);
                else
                    _vm.RotateAddFilesCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _vm.IsCropVisible)
            {
                // 删除选中标注（2026-08-16 阶段 5）
                if (CropOverlayControl.AnnotationTool?.DeleteSelected() == true)
                {
                    CropOverlayControl.RefreshAnnotationsVisual();
                    _vm.CropViewModel.NotifyAnnotationsChanged();
                    e.Handled = true;
                }
            }
        }

        /// <summary>标注工具按钮点击：切换绘制工具（再点同按钮 = 取消回选择模式），选中态高亮</summary>
        private void AnnotTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not AnnotationKind kind) return;
            var tool = _vm.CropViewModel.AnnotationTool;
            if (tool == null) return;

            AnnotationKind? next = tool.ActiveKind == kind ? null : kind; // 再点取消
            CropOverlayControl.SetAnnotationKind(next);
            UpdateAnnotButtonStates();
        }

        /// <summary>最近一次触发 DropDownOpened 的下拉（防止定时器重开别的下拉）</summary>
        private System.Windows.Controls.ComboBox? _lastAnnotationCombo;

        /// <summary>标注下拉是否被"用户主动关闭"（选中项/Esc/点击他处；重开逻辑据此跳过）</summary>
        private bool _userDismissedAnnotationCombo;

        /// <summary>
        /// 标注行下拉弹出稳定（2026-08-16，基于 DiagCombo 日志证据）：
        /// 日志显示这些 8 项下拉偶发"打开后 ~100ms 关闭、窗口全程激活"——是 Popup 打开后布局尚未完成，
        /// MouseUp 的 hit-test 落在窗口上被判定为"点击外部"而关闭（8 项内容布局耗时更长，间歇触发）。
        /// 修复：DropDownOpened 后延迟 80ms 用 Input 优先级重设 IsDropDownOpen——若 Popup 已被误关则立即重开，
        /// 若仍开着则无操作（幂等、无闪烁）。
        /// 2026-08-17 修复"选中后下拉又弹开、关不掉"（现场反馈）：
        /// 原逻辑不区分关闭原因——用户选中某项后下拉正常关闭，挂起的定时器仍把它重新弹开，形成死循环。
        /// 现仅在"非用户主动关闭"（_userDismissedAnnotationCombo 未置位，由选中项/Esc/点击他处置位）
        /// 且仍是最新打开的那个下拉时才重开。定时器改用 Input 优先级：
        /// 避免 UI 线程排队的后台工作（如预览加载）饿死 Background 优先级的回调（现场观察到
        /// "等约 20 秒后才恢复正常"，与 Background 定时器被延迟触发吻合）。
        /// </summary>
        private void AnnotationCombo_DropDownOpened(object sender, EventArgs e)
        {
            if (sender is not System.Windows.Controls.ComboBox combo) return;
            _lastAnnotationCombo = combo;
            _userDismissedAnnotationCombo = false;
            var timer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Input,
                combo.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            timer.Tick += (s, _) =>
            {
                timer.Stop();
                if (ReferenceEquals(_lastAnnotationCombo, combo)
                    && !combo.IsDropDownOpen && IsLoaded && !_userDismissedAnnotationCombo)
                    combo.IsDropDownOpen = true;
            };
            timer.Start();
        }

        /// <summary>用户在下拉中选中某项 → 视为主动关闭（重开逻辑跳过，避免选完又弹开）</summary>
        private void AnnotationCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _userDismissedAnnotationCombo = true;
        }

        /// <summary>用户按 Esc/Alt 关闭下拉 → 视为主动关闭（重开逻辑跳过）</summary>
        private void AnnotationCombo_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key is System.Windows.Input.Key.Escape
                or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt)
                _userDismissedAnnotationCombo = true;
        }

        /// <summary>根据当前工具刷新按钮选中态（高亮背景，Button 无 IsChecked 故用 Background）</summary>
        private void UpdateAnnotButtonStates()
        {
            var active = _vm.CropViewModel.AnnotationTool?.ActiveKind;
            var selectedBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xE4, 0xFF));
            var normalBrush = System.Windows.Media.Brushes.Transparent;
            AnnotRectButton.Background = active == AnnotationKind.Rectangle ? selectedBrush : normalBrush;
            AnnotOvalButton.Background = active == AnnotationKind.Oval ? selectedBrush : normalBrush;
            AnnotLineButton.Background = active == AnnotationKind.Line ? selectedBrush : normalBrush;
            AnnotArrowButton.Background = active == AnnotationKind.Arrow ? selectedBrush : normalBrush;
            AnnotTextButton.Background = active == AnnotationKind.Text ? selectedBrush : normalBrush;
            AnnotMosaicButton.Background = active == AnnotationKind.Mosaic ? selectedBrush : normalBrush;
        }

        /// <summary>DataGrid 选中变化时同步到 ViewModel（支持 Ctrl/Shift 多选移除）</summary>
        private void RotateDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dg)
                _vm.SyncSelection(dg.SelectedItems.OfType<RotateFileItem>().ToList());
        }

        /// <summary>裁剪列表选中变化：图片项 → 选中预览；分组项 → 清空图片选中（单选联动）</summary>
        private void CropTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            switch (e.NewValue)
            {
                case CropFileItem file:
                    _vm.CropViewModel.SelectedFile = file;
                    break;
                case CropGroupItem group:
                    _vm.CropViewModel.SelectedFile = null;
                    break;
                default:
                    _vm.CropViewModel.SelectedFile = null;
                    break;
            }
        }

        /// <summary>双击文件夹节点 → 切换展开/折叠（隧道事件 + ClickCount，规避 MouseDoubleClick 在部分模板下不触发）</summary>
        private void CropTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (e.OriginalSource is DependencyObject src)
            {
                var item = FindAncestor<TreeViewItem>(src);
                if (item?.DataContext is CropGroupItem group)
                {
                    group.IsExpanded = !group.IsExpanded;
                    e.Handled = true; // 阻止第二次点击继续触发选中/其他
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T t) return t;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
