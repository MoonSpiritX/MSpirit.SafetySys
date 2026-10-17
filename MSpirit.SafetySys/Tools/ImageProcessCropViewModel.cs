// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>裁剪列表中的单个图片项</summary>
    public class CropFileItem : INotifyPropertyChanged
    {
        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);
        public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? "";

        private string _status = "待处理";
        /// <summary>处理状态（待处理/成功/失败）</summary>
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        /// <summary>列表选中状态（与 TreeViewItem.IsSelected 双向绑定）</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public CropFileItem(string path) => FilePath = path;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>裁剪列表中的文件夹分组（组内为该目录下全部图片）</summary>
    public class CropGroupItem : INotifyPropertyChanged
    {
        /// <summary>文件夹完整路径（分组的唯一标识）</summary>
        public string FolderPath { get; }

        /// <summary>显示名 = 末端文件夹名</summary>
        public string FolderName => Path.GetFileName(FolderPath.TrimEnd('\\', '/'));

        public ObservableCollection<CropFileItem> Files { get; } = new();

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        /// <summary>分组选中状态（点击分组名时用于清空图片选中，保持单选联动）</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public CropGroupItem(string folderPath) => FolderPath = folderPath;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>标注颜色选项：名称 + ARGB 色值 + 色块（2026-08-16，供下拉列表色块显示与选色）</summary>
    public sealed class AnnotationColorOption
    {
        public AnnotationColorOption(string name, uint argb)
        {
            Name = name;
            Argb = argb;
        }

        public string Name { get; }
        public uint Argb { get; }

        /// <summary>色块（WPF SolidColorBrush，下拉列表色块显示用）</summary>
        public System.Windows.Media.SolidColorBrush Brush =>
            new(System.Windows.Media.Color.FromArgb(
                (byte)(Argb >> 24), (byte)(Argb >> 16), (byte)(Argb >> 8), (byte)Argb));

        public override string ToString() => Name;
    }

    /// <summary>
    /// 标注与剪裁面板 ViewModel：分组图片列表 + 选中联动 + 添加来源（文件/文件夹/拖入加载同目录全部）。
    /// 阶段 1 先实现列表与添加；预览/裁剪框/执行在后续阶段接入。
    /// 与方向转换面板（ImageProcessViewModel）平级，互不共享图片集合。
    /// </summary>
    public class ImageProcessCropViewModel : INotifyPropertyChanged
    {
        /// <summary>分组列表（按文件夹分组，显示末端文件夹名）</summary>
        public ObservableCollection<CropGroupItem> Groups { get; } = new();

        /// <summary>已加入的全部文件路径（分组级去重，Windows 路径不区分大小写）</summary>
        private readonly HashSet<string> _filePaths = new(StringComparer.OrdinalIgnoreCase);

        private CropFileItem? _selectedFile;
        /// <summary>当前选中的图片（单选；选中即触发 SelectedFileChanged 供预览加载）</summary>
        public CropFileItem? SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (_selectedFile == value) return;
                _selectedFile = value;
                OnPropertyChanged();
                SelectedFileChanged?.Invoke(value);
                ((RelayCommand)ClearPreviewCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ApplyCropCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ApplyAndNextCommand).NotifyCanExecuteChanged();
                UpdatePositionText();
            }
        }

        /// <summary>选中图片变化事件（预览加载由视图/后续阶段订阅）</summary>
        public event Action<CropFileItem?>? SelectedFileChanged;

        /// <summary>失败文件通知（Toast 由视图订阅弹出）</summary>
        public event Action<IReadOnlyList<string>>? CropFailed;

        /// <summary>添加文件/文件夹成功通知（Toast 由视图订阅弹出），参数为新增数量</summary>
        public event Action<int>? FilesAdded;

        public int TotalFileCount => _filePaths.Count;

        public string TotalCountText => $"总计: {_filePaths.Count} 张";

        private string _positionText = "第 0/0 张";
        /// <summary>右上角位置指示：当前选中图片序号/总数（"第 3/12 张"）；未选中显示 "共 x 张"（2026-08-16 新增）</summary>
        public string PositionText
        {
            get => _positionText;
            private set { _positionText = value; OnPropertyChanged(); }
        }

        /// <summary>标注状态机（由视图注入；Items = 当前图片的标注列表，保存时随裁剪一并渲染，2026-08-16 阶段 5）</summary>
        public AnnotationTool? AnnotationTool { get; set; }

        // ═══ 标注颜色（2026-08-16：选色后新标注使用该颜色） ═══

        /// <summary>标注颜色选项（名称 + 色块，下拉列表与选色共用；2026-08-16）</summary>
        public AnnotationColorOption[] AnnotationColorOptions { get; } =
        {
            new("红色", 0xFFE53935),
            new("橙色", 0xFFFB8C00),
            new("黄色", 0xFFFDD835),
            new("绿色", 0xFF43A047),
            new("蓝色", 0xFF1E88E5),
            new("紫色", 0xFF8E24AA),
            new("黑色", 0xFF000000),
            new("白色", 0xFFFFFFFF)
        };

        /// <summary>各颜色 ARGB（与 AnnotationColorOptions 索引一一对应）</summary>
        private uint[] AnnotationColors => AnnotationColorOptions.Select(o => o.Argb).ToArray();

        private int _selectedAnnotationColorIndex;
        /// <summary>当前标注颜色索引（变化时同步给 AnnotationTool，新标注使用该色）</summary>
        public int SelectedAnnotationColorIndex
        {
            get => _selectedAnnotationColorIndex;
            set
            {
                if (_selectedAnnotationColorIndex == value) return;
                _selectedAnnotationColorIndex = value;
                OnPropertyChanged();
                if (AnnotationTool != null && value >= 0 && value < AnnotationColors.Length)
                    AnnotationTool.CurrentColorArgb = AnnotationColors[value];
            }
        }

        // ═══ 标注字号 / 线宽（2026-08-16：新标注使用的默认字号与线宽） ═══

        /// <summary>文字工具字号选项（显示名；值为像素，索引一一对应 FontSizes）</summary>
        public string[] AnnotationFontSizeOptions { get; } = { "10号", "12号", "15号", "20号", "24号", "30号", "48号", "60号" };

        private static readonly float[] AnnotationFontSizes = { 10, 12, 15, 20, 24, 30, 48, 60 };

        private int _selectedFontSizeIndex = 1; // 默认 12号
        /// <summary>文字字号索引（默认 12号；变化时同步给 AnnotationTool）</summary>
        public int SelectedFontSizeIndex
        {
            get => _selectedFontSizeIndex;
            set
            {
                if (_selectedFontSizeIndex == value) return;
                _selectedFontSizeIndex = value;
                OnPropertyChanged();
                if (AnnotationTool != null && value >= 0 && value < AnnotationFontSizes.Length)
                    AnnotationTool.CurrentFontSize = AnnotationFontSizes[value];
            }
        }

        /// <summary>线宽选项（显示名；值为像素，索引一一对应 AnnotationLineWidths）</summary>
        public string[] AnnotationLineWidthOptions { get; } = { "贼细", "细", "中", "宽" };

        private static readonly float[] AnnotationLineWidths = { 1, 2, 3, 5 };

        private int _selectedLineWidthIndex = 1; // 默认 细(2px)
        /// <summary>线宽索引（默认 细；变化时同步给 AnnotationTool）</summary>
        public int SelectedLineWidthIndex
        {
            get => _selectedLineWidthIndex;
            set
            {
                if (_selectedLineWidthIndex == value) return;
                _selectedLineWidthIndex = value;
                OnPropertyChanged();
                if (AnnotationTool != null && value >= 0 && value < AnnotationLineWidths.Length)
                    AnnotationTool.CurrentStrokeWidth = AnnotationLineWidths[value];
            }
        }

        /// <summary>标注列表增删后通知（视图刷新按钮可用态等）</summary>
        public void NotifyAnnotationsChanged()
        {
            ((RelayCommand)ApplyCropCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ApplyAndNextCommand).NotifyCanExecuteChanged();
        }

        /// <summary>按当前选中（跨分组列表顺序）刷新位置指示</summary>
        private void UpdatePositionText()
        {
            if (SelectedFile == null)
            {
                PositionText = $"共 {_filePaths.Count} 张";
                return;
            }
            var all = Groups.SelectMany(g => g.Files).ToList();
            int idx = all.FindIndex(f => f.FilePath.Equals(SelectedFile.FilePath, StringComparison.OrdinalIgnoreCase));
            PositionText = idx >= 0 ? $"第 {idx + 1}/{all.Count} 张" : $"共 {all.Count} 张";
        }

        private string _statusText = "";
        /// <summary>面板状态栏文本</summary>
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // ═══ 预览（阶段 2） ═══

        private System.Windows.Media.Imaging.BitmapSource? _previewImage;
        /// <summary>当前预览图（摆正后降采样）</summary>
        public System.Windows.Media.Imaging.BitmapSource? PreviewImage
        {
            get => _previewImage;
            set { _previewImage = value; OnPropertyChanged(); }
        }

        private int _pixelWidth;
        private int _pixelHeight;
        /// <summary>当前图片摆正后的原始像素尺寸（裁剪坐标基准）</summary>
        public int PixelWidth => _pixelWidth;
        public int PixelHeight => _pixelHeight;

        /// <summary>预览就绪事件（视图据此让裁剪框重置选区）</summary>
        public event Action? PreviewReady;

        // ═══ 裁剪比例（null=自由） ═══

        public string[] CropAspectOptions { get; } = { "自由", "1：1", "3：4", "4：3", "9：16", "16：9" };

        private int _selectedCropAspectIndex;
        public int SelectedCropAspectIndex
        {
            get => _selectedCropAspectIndex;
            set
            {
                if (_selectedCropAspectIndex == value) return;
                _selectedCropAspectIndex = value;
                OnPropertyChanged();
                CropAspectRatio = value switch
                {
                    1 => 1.0,                    // 1：1
                    2 => 3.0 / 4.0,              // 3：4
                    3 => 4.0 / 3.0,              // 4：3
                    4 => 9.0 / 16.0,             // 9：16
                    5 => 16.0 / 9.0,             // 16：9
                    _ => (double?)null
                };
            }
        }

        private double? _cropAspectRatio;
        /// <summary>当前裁剪比例（null=自由；Overlay 绑定此值重置选区）</summary>
        public double? CropAspectRatio
        {
            get => _cropAspectRatio;
            set { _cropAspectRatio = value; OnPropertyChanged(); }
        }

        // ═══ 输出方式（阶段 3） ═══

        public string[] OutputModeOptions { get; } = { "另存至原目录", "另存至其他位置", "覆盖原图" };

        private int _selectedOutputModeIndex;
        /// <summary>输出方式：0=另存至原目录(_Edit) 1=另存至其他位置 2=覆盖原图(回收站+_Edit)</summary>
        public int SelectedOutputModeIndex
        {
            get => _selectedOutputModeIndex;
            set { _selectedOutputModeIndex = value; OnPropertyChanged(); }
        }

        /// <summary>覆盖模式确认委托（视图注入，返回 true 才执行）</summary>
        public Func<bool>? ConfirmOverwrite { get; set; }

        // ═══ 裁剪执行（阶段 3） ═══

        public ICommand ApplyCropCommand { get; }
        public ICommand ApplyAndNextCommand { get; }

        private (int X, int Y, int Width, int Height)? _currentPixelSelection;

        /// <summary>由视图（裁剪控件）实时更新当前像素选区</summary>
        public void UpdatePixelSelection(int x, int y, int width, int height)
        {
            _currentPixelSelection = (x, y, width, height);
        }

        private async void ExecuteApplyCrop() => await ApplyCropAsync(andNext: false);
        private async void ExecuteApplyAndNext() => await ApplyCropAsync(andNext: true);

        private async System.Threading.Tasks.Task ApplyCropAsync(bool andNext)
        {
            var file = SelectedFile;
            if (file == null) { StatusText = "请先在左侧选择要裁剪的图片"; return; }
            if (!_currentPixelSelection.HasValue) { StatusText = "请先调整裁剪框"; return; }
            var sel = _currentPixelSelection.Value;

            if (SelectedOutputModeIndex == 2 && ConfirmOverwrite != null && !ConfirmOverwrite())
                return;

            string? targetFolder = null;
            if (SelectedOutputModeIndex == 1)
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "请选择保存裁剪图片的文件夹" };
                if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.FolderName)) return;
                targetFolder = dlg.FolderName;
            }

            try
            {
                var (outPath, deleteOriginal) = await System.Threading.Tasks.Task.Run(() =>
                {
                    using var full = CropImageProcessor.DecodeCorrected(file.FilePath); // 全尺寸解码摆正
                    if (full == null) throw new InvalidOperationException("图片解码失败");

                    // 标注与裁剪一体（2026-08-16 阶段 5）：先重绘标注到全尺寸图，再裁剪选区
                    var annotations = AnnotationTool?.Items ?? new System.Collections.Generic.List<Annotation>();
                    AnnotationRenderer.DrawAll(full, annotations);

                    using var cropped = CropImageProcessor.Crop(full, sel.X, sel.Y, sel.Width, sel.Height);
                    if (cropped == null) throw new InvalidOperationException("裁剪区域无效");

                    string sourceDir = Path.GetDirectoryName(file.FilePath) ?? "";
                    string baseName = Path.GetFileNameWithoutExtension(file.FilePath);
                    var (format, ext) = ImageProcessViewModel.GetOutputFormat(file.FilePath);
                    string baseDir = SelectedOutputModeIndex == 1 ? targetFolder! : sourceDir;
                    string outPath = ImageProcessViewModel.GetUniquePath(Path.Combine(baseDir, $"{baseName}_Edit{ext}"));

                    using var image = SkiaSharp.SKImage.FromBitmap(cropped);
                    var data = image.Encode(format, format is SkiaSharp.SKEncodedImageFormat.Jpeg or SkiaSharp.SKEncodedImageFormat.Webp ? 90 : 100);
                    if (data == null)
                    {
                        // 编码器缺失（罕见）：回退 PNG
                        outPath = ImageProcessViewModel.GetUniquePath(Path.Combine(baseDir, $"{baseName}_Edit.png"));
                        data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    }
                    using (data)
                    using (var fs = File.Create(outPath))
                    {
                        data.SaveTo(fs);
                    }
                    return (outPath, SelectedOutputModeIndex == 2);
                });

                if (deleteOriginal)
                {
                    // 覆盖模式：新文件已落盘成功，原图进回收站（顺序保证：写失败不会丢原图）
                    Helpers.ImageHelper.DeleteToRecycleBin(file.FilePath);
                }

                // 保存后文件夹级刷新（仅变更的目录分组）
                string changedDir = Path.GetDirectoryName(file.FilePath) ?? "";
                if (SelectedOutputModeIndex != 1) // 另存其他位置时源目录未变，无需刷新
                    RefreshGroup(changedDir);

                // 2026-08-16 修复：RefreshGroup 会重建列表项实例（new CropFileItem），旧实例已脱离列表——
                // 直接标旧实例状态界面不显示。须按路径定位刷新后的新实例再标记；
                // 覆盖模式下原图已进回收站（列表无此项），回退标旧实例（无害，界面不显示）。
                var refreshed = Groups.SelectMany(g => g.Files)
                    .FirstOrDefault(f => f.FilePath.Equals(file.FilePath, StringComparison.OrdinalIgnoreCase));
                (refreshed ?? file).Status = "成功";
                StatusText = $"已保存：{Path.GetFileName(outPath)}";
                if (andNext) SelectNextFile(file);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "标注与剪裁失败：{Path}", file.FilePath);
                file.Status = "失败";
                StatusText = "裁剪失败";
                CropFailed?.Invoke(new[] { file.FileName });
            }
        }

        /// <summary>文件夹级刷新：重扫目录重建该分组（保留仍在的选中项；_Edit 新文件出现、覆盖模式原图消失）</summary>
        public void RefreshGroup(string folderPath)
        {
            var group = Groups.FirstOrDefault(g => g.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
            if (group == null || string.IsNullOrEmpty(folderPath)) return;

            string? keepSelected = SelectedFile?.FilePath;
            var files = Directory.Exists(folderPath)
                ? Directory.EnumerateFiles(folderPath)
                    .Where(IsImageFile)
                    .OrderBy(f => Path.GetFileName(f), NaturalStringComparer.Instance) // 2026-08-16：与组内排序一致（自然序）
                    .ToList()
                : new List<string>();

            // 同步去重集合：移除该目录旧路径，加入新扫描路径
            _filePaths.RemoveWhere(p => (Path.GetDirectoryName(p) ?? "").Equals(folderPath, StringComparison.OrdinalIgnoreCase));
            group.Files.Clear();
            foreach (var f in files)
            {
                _filePaths.Add(f);
                group.Files.Add(new CropFileItem(f));
            }
            OnPropertyChanged(nameof(TotalCountText));
            UpdatePositionText(); // 刷新后文件数可能变化（_Edit 新增/原图删除）

            // 恢复选中（若原文件仍在列表中）
            if (keepSelected != null)
                SelectedFile = group.Files.FirstOrDefault(f => f.FilePath.Equals(keepSelected, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>选中当前图片之后的下一张（跨分组按列表顺序；无下一张时保持不变）。
        /// 2026-08-16 修复：按文件路径匹配而非引用相等——RefreshGroup 重建列表项后旧实例引用已失效，
        /// 原 `FindIndex(f => f == current)` 必然找不到（idx=-1），导致「应用并下一张」不跳转。</summary>
        private void SelectNextFile(CropFileItem current)
        {
            var all = Groups.SelectMany(g => g.Files).ToList();
            int idx = all.FindIndex(f => f.FilePath.Equals(current.FilePath, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < all.Count)
                SelectedFile = all[idx + 1];
        }

        /// <summary>选中图片变化 → 异步加载预览</summary>
        private async void OnSelectedFileChanged(CropFileItem? file)
        {
            if (file == null)
            {
                PreviewImage = null;
                StatusText = "";
                return;
            }

            StatusText = "正在加载预览…";
            try
            {
                var (bitmap, pixelW, pixelH) = await System.Threading.Tasks.Task.Run(() =>
                {
                    var size = CropImageProcessor.GetCorrectedSize(file.FilePath);
                    var bmp = CropImageProcessor.DecodeCorrected(file.FilePath, 1600);
                    return (bmp, size?.Width ?? 0, size?.Height ?? 0);
                });

                if (bitmap == null)
                {
                    StatusText = "图片加载失败";
                    return;
                }

                PreviewImage = ToBitmapSource(bitmap);
                bitmap.Dispose();
                _pixelWidth = pixelW;
                _pixelHeight = pixelH;
                OnPropertyChanged(nameof(PixelWidth));
                OnPropertyChanged(nameof(PixelHeight));
                StatusText = $"尺寸：{pixelW} × {pixelH}";
                PreviewReady?.Invoke();
                ((RelayCommand)ClearPreviewCommand).NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "预览加载失败：{Path}", file.FilePath);
                StatusText = "图片加载失败";
            }
        }

        /// <summary>SKBitmap → WPF BitmapSource（经 PNG 编码中转，格式可靠）</summary>
        private static System.Windows.Media.Imaging.BitmapSource ToBitmapSource(SkiaSharp.SKBitmap bitmap)
        {
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var ms = new System.IO.MemoryStream(data.ToArray());
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // ═══ 命令 ═══
        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand ClearPreviewCommand { get; }
        public ICommand ClearCommand { get; }

        public ImageProcessCropViewModel()
        {
            AddFilesCommand = new RelayCommand(ExecuteAddFiles);
            AddFolderCommand = new RelayCommand(ExecuteAddFolder);
            ClearPreviewCommand = new RelayCommand(ClearPreview, () => SelectedFile != null || PreviewImage != null);
            ClearCommand = new RelayCommand(ClearAll, () => Groups.Count > 0);
            ApplyCropCommand = new RelayCommand(ExecuteApplyCrop, () => SelectedFile != null);
            ApplyAndNextCommand = new RelayCommand(ExecuteApplyAndNext, () => SelectedFile != null);
            SelectedFileChanged += OnSelectedFileChanged;
        }

        /// <summary>
        /// 清空预览：移除右侧预览图，什么也不显示（列表选中保持不变）。
        /// </summary>
        public void ClearPreview()
        {
            PreviewImage = null;
            _pixelWidth = 0;
            _pixelHeight = 0;
            OnPropertyChanged(nameof(PixelWidth));
            OnPropertyChanged(nameof(PixelHeight));
            StatusText = "";
            PreviewReady?.Invoke(); // 视图据此 SetPreview(null, 0, 0) 清空裁剪控件
            ((RelayCommand)ClearPreviewCommand).NotifyCanExecuteChanged();
        }

        private void ExecuteAddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                int added = AddFiles(dlg.FileNames);
                if (added > 0) FilesAdded?.Invoke(added);
            }
        }

        private async void ExecuteAddFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "请选择要导入的文件夹（将按子目录分组导入其中所有图片）"
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
            {
                // 后台枚举（大目录避免卡 UI），添加回 UI 线程
                var folders = await System.Threading.Tasks.Task.Run(() =>
                {
                    var dirs = new List<string>();
                    try
                    {
                        dirs.AddRange(Directory.EnumerateDirectories(dlg.FolderName, "*", SearchOption.AllDirectories));
                    }
                    catch (Exception ex) { Log.Warning(ex, "枚举子目录失败：{Path}", dlg.FolderName); }
                    dirs.Add(dlg.FolderName);
                    return dirs;
                });
                int added = 0;
                foreach (var dir in folders) added += AddFolder(dir);
                OnPropertyChanged(nameof(TotalCountText));
                if (added > 0) FilesAdded?.Invoke(added);
            }
        }

        // ═══ 分组构建（纯逻辑，可测） ═══

        /// <summary>
        /// 按文件所在目录分组加入。已在列表中的路径自动去重。
        /// 返回实际新增数量。
        /// </summary>
        public int AddFiles(IEnumerable<string> paths) => AddFilesCore(paths).Count;

        /// <summary>
        /// 按文件所在目录分组加入，返回**实际新增**的路径列表（用于拖入后自动选中第一张）。
        /// 已在列表中的路径不会出现在返回值中。
        /// </summary>
        public IReadOnlyList<string> AddFilesReturningAdded(IEnumerable<string> paths) => AddFilesCore(paths);

        private List<string> AddFilesCore(IEnumerable<string> paths)
        {
            var added = new List<string>();
            foreach (var p in paths)
            {
                if (AddToGroup(p)) added.Add(p);
            }
            return added;
        }

        /// <summary>
        /// 拖入后自动选中：仅 1 张新增 → 选中它；多张 → 按自然排序选中第一张
        /// （跨分组时取"第一个分组"的第一张——分组按文件夹名自然排序，组内文件也按自然排序，
        /// 保证选中的就是列表中用户看到的第一项）。无新增则不动当前选中。
        /// 2026-08-16 新增：拖入 1 张/多张图片后右侧直接显示，无需手动点选。
        /// </summary>
        public void SelectFirstAdded(IReadOnlyList<string> addedPaths)
        {
            if (addedPaths == null || addedPaths.Count == 0) return;

            if (addedPaths.Count == 1)
            {
                var single = Groups.SelectMany(g => g.Files)
                    .FirstOrDefault(f => f.FilePath.Equals(addedPaths[0], StringComparison.OrdinalIgnoreCase));
                if (single != null) SelectedFile = single;
                return;
            }

            // 多张：第一个分组（按文件夹名自然排序）的第一张（组内已自然排序）
            var firstGroup = Groups
                .OrderBy(g => g.FolderName, NaturalStringComparer.Instance)
                .FirstOrDefault();
            if (firstGroup != null && firstGroup.Files.Count > 0)
                SelectedFile = firstGroup.Files[0];
        }

        /// <summary>
        /// 添加文件夹：递归收集其中全部图片，按子目录分组（每个子目录一组）。
        /// 目录不存在/无权限时记日志并跳过，不抛出。
        /// </summary>
        public int AddFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return 0;
            int added = 0;
            try
            {
                // 子目录递归在前，根目录最后（列表按文件夹名排序，顺序无影响）
                foreach (var dir in Directory.EnumerateDirectories(folderPath, "*", SearchOption.AllDirectories))
                    added += AddFilesInDirectory(dir);
                added += AddFilesInDirectory(folderPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "添加文件夹失败：{Path}", folderPath);
            }
            return added;
        }

        /// <summary>
        /// 拖入添加：文件夹 → 递归分组；图片文件 → 自动加载其同目录下的全部图片。
        /// 返回（实际新增数量，被忽略的不支持格式文件列表——仅直接拖入的文件计入 Ignored）。
        /// 注意：本方法会修改 ObservableCollection，须在 UI 线程调用；
        /// 拖放场景请用 CollectDroppedFiles（后台收集）+ AddFiles（UI 添加）组合。
        /// </summary>
        public (int Added, IReadOnlyList<string> Ignored) AddDroppedFiles(IEnumerable<string> paths)
        {
            var (images, ignored) = CollectDroppedFiles(paths);
            int added = AddFiles(images);
            return (added, ignored);
        }

        /// <summary>
        /// 收集拖入内容（纯 IO，可安全在后台线程调用）：返回（图片路径列表——文件含其同目录全部，文件夹递归收集，忽略列表）。
        /// 集合修改由调用方在 UI 线程通过 AddFiles 完成，避免跨线程修改 ObservableCollection。
        /// </summary>
        public static (List<string> Images, List<string> Ignored) CollectDroppedFiles(IEnumerable<string> paths)
        {
            var images = new List<string>();
            var ignored = new List<string>();
            var dirsToLoad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        foreach (var dir in Directory.EnumerateDirectories(p, "*", SearchOption.AllDirectories))
                        {
                            foreach (var f in Directory.EnumerateFiles(dir))
                                if (IsImageFile(f)) images.Add(f);
                        }
                        foreach (var f in Directory.EnumerateFiles(p))
                            if (IsImageFile(f)) images.Add(f);
                    }
                    else if (File.Exists(p))
                    {
                        if (IsImageFile(p))
                            dirsToLoad.Add(Path.GetDirectoryName(p) ?? "");
                        else
                            ignored.Add(p);
                    }
                    else
                    {
                        ignored.Add(p);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "拖入收集失败：{Path}", p);
                }
            }

            // 拖入图片 → 加载同目录全部（每目录扫一次，去重由 AddFiles 保证）
            foreach (var dir in dirsToLoad)
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                        if (IsImageFile(f)) images.Add(f);
                }
            }
            return (images, ignored);
        }

        /// <summary>扫描目录内全部图片并按目录分组加入，返回新增数量</summary>
        private int AddFilesInDirectory(string dir)
        {
            int added = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (IsImageFile(f) && AddToGroup(f)) added++;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "扫描目录图片失败：{Dir}", dir);
            }
            return added;
        }

        /// <summary>把单个文件加入其目录分组（去重），返回是否新增</summary>
        private bool AddToGroup(string filePath)
        {
            if (!_filePaths.Add(filePath)) return false;

            string dir = Path.GetDirectoryName(filePath) ?? "";
            var group = Groups.FirstOrDefault(g => g.FolderPath.Equals(dir, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                group = new CropGroupItem(dir);
                Groups.Add(group);
            }
            // 组内按文件名自然升序插入（数字感知：a2 < a10；保持列表有序，2026-08-16 由 OrdinalIgnoreCase 改自然排序）
            var item = new CropFileItem(filePath);
            int idx = 0;
            while (idx < group.Files.Count &&
                   NaturalStringComparer.Instance.Compare(group.Files[idx].FileName, item.FileName) < 0)
                idx++;
            group.Files.Insert(idx, item);
            OnPropertyChanged(nameof(TotalCountText));
            UpdatePositionText(); // 总数/序号变化（新增后当前选中序号可能后移）
            ((RelayCommand)ClearCommand).NotifyCanExecuteChanged(); // 列表非空 → 清空按钮可用
            return true;
        }

        // ═══ 移除/清空 ═══

        public void RemoveSelectedFile(CropFileItem item)
        {
            if (item == null) return;
            if (SelectedFile == item) SelectedFile = null;
            _filePaths.Remove(item.FilePath);
            var group = Groups.FirstOrDefault(g => g.Files.Contains(item));
            group?.Files.Remove(item);
            if (group != null && group.Files.Count == 0)
                Groups.Remove(group);
            OnPropertyChanged(nameof(TotalCountText));
            UpdatePositionText(); // 移除后序号前移/未选中时总数变化
            ((RelayCommand)ClearCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ClearPreviewCommand).NotifyCanExecuteChanged();
        }

        public void ClearAll()
        {
            SelectedFile = null;
            _filePaths.Clear();
            Groups.Clear();
            AnnotationTool?.Clear(); // 2026-08-16 阶段 5：清空列表同时清标注
            OnPropertyChanged(nameof(TotalCountText));
            UpdatePositionText();
            ((RelayCommand)ClearCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ClearPreviewCommand).NotifyCanExecuteChanged();
        }

        /// <summary>按扩展名判断是否支持的图片格式（与方向转换一致）</summary>
        public static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tiff" or ".tif" or ".gif";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
