// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;

namespace MS_SafetyManagementSystem.Tools
{
    public enum ImageProcessMode
    {
        Rotation, Crop, MoreFeatures
    }

    public enum RotationOperation
    {
        None, Rotate90, Rotate180, Rotate270, FlipH, FlipV, CorrectCopy
    }

    public class RotateFileItem : INotifyPropertyChanged
    {
        public string FilePath { get; }
        public string FileName => System.IO.Path.GetFileName(FilePath);
        /// <summary>文件所在目录（列表"路径"列显示，不含文件名）</summary>
        public string DirectoryPath => System.IO.Path.GetDirectoryName(FilePath) ?? "";
        public string Extension
        {
            get
            {
                string e = System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
                return e switch
                {
                    ".jpg" or ".jpeg" => "JPEG",
                    ".png" => "PNG",
                    ".bmp" => "BMP",
                    ".webp" => "WEBP",
                    ".tiff" or ".tif" => "TIFF",
                    _ => e.TrimStart('.').ToUpperInvariant()
                };
            }
        }

        private string _status = "待处理";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private int _index;
        /// <summary>列表序号（1 起，由 ViewModel 在列表增删后统一重编号）</summary>
        public int Index
        {
            get => _index;
            set { _index = value; OnPropertyChanged(); }
        }

        public RotateFileItem(string path) => FilePath = path;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class ImageProcessViewModel : INotifyPropertyChanged
    {
        // ═══ 导航 ═══
        private ImageProcessMode _selectedMode = ImageProcessMode.Rotation;
        public ImageProcessMode SelectedMode
        {
            get => _selectedMode;
            set
            {
                if (_selectedMode == value) return;
                _selectedMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRotationVisible));
                OnPropertyChanged(nameof(IsCropVisible));
                OnPropertyChanged(nameof(IsMoreFeaturesVisible));
            }
        }
        public bool IsRotationVisible => SelectedMode == ImageProcessMode.Rotation;
        public bool IsCropVisible => SelectedMode == ImageProcessMode.Crop;
        public bool IsMoreFeaturesVisible => SelectedMode == ImageProcessMode.MoreFeatures;

        // ═══ 标注与剪裁面板（独立 ViewModel，与方向转换平级、不共享图片集合） ═══
        public ImageProcessCropViewModel CropViewModel { get; } = new();

        // ═══ 方向转换：文件列表 ═══
        public BatchObservableCollection<RotateFileItem> RotateFiles { get; } = new();
        /// <summary>已添加文件的路径集合（去重加速，O(1) 查重；Windows 路径不区分大小写）。增删必须与 RotateFiles 同步。</summary>
        private readonly HashSet<string> _rotateFilePaths = new(StringComparer.OrdinalIgnoreCase);
        private RotateFileItem? _selectedRotateFile;
        public RotateFileItem? SelectedRotateFile
        {
            get => _selectedRotateFile;
            set
            {
                _selectedRotateFile = value;
                OnPropertyChanged();
            }
        }

        private readonly List<RotateFileItem> _selectedRotateFiles = new();
        /// <summary>当前实际选中的文件（支持 Ctrl/Shift 多选，由视图 DataGrid.SelectionChanged 同步）</summary>
        public IReadOnlyList<RotateFileItem> SelectedRotateFiles => _selectedRotateFiles;

        /// <summary>同步 DataGrid 的选中项集合（多选），由视图的 SelectionChanged 事件调用</summary>
        public void SyncSelection(IEnumerable<RotateFileItem> items)
        {
            _selectedRotateFiles.Clear();
            _selectedRotateFiles.AddRange(items);
            ((RelayCommand)RotateRemoveSelectedCommand).NotifyCanExecuteChanged();
        }

        public string RotateFileCountText => $"总计: {RotateFiles.Count} 张";

        // ═══ 方向转换：操作选择（下拉菜单） ═══
        private RotationOperation _selectedRotation = RotationOperation.CorrectCopy;
        public RotationOperation SelectedRotation
        {
            get => _selectedRotation;
            set
            {
                _selectedRotation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedRotationText));
            }
        }
        public string SelectedRotationText => _selectedRotation switch
        {
            RotationOperation.Rotate90 => "当前: 右转90°",
            RotationOperation.Rotate180 => "当前: 旋转180°",
            RotationOperation.Rotate270 => "当前: 左转90°",
            RotationOperation.FlipH => "当前: 水平翻转",
            RotationOperation.FlipV => "当前: 垂直翻转",
            RotationOperation.CorrectCopy => "当前: 校正副本",
            _ => "当前: 未选择"
        };

        /// <summary>方向操作下拉菜单的候选列表（与 SelectedRotationOption 的映射见 setter）</summary>
        public IReadOnlyList<string> RotationOptions { get; } = new[]
        {
            "左转90°", "旋转180°", "右转90°", "水平翻转", "垂直翻转", "校正方向"
        };

        private string _selectedRotationOption = "校正方向";
        public string SelectedRotationOption
        {
            get => _selectedRotationOption;
            set
            {
                if (_selectedRotationOption == value) return;
                _selectedRotationOption = value;
                OnPropertyChanged();
                SelectedRotation = value switch
                {
                    "左转90°" => RotationOperation.Rotate270,
                    "旋转180°" => RotationOperation.Rotate180,
                    "右转90°" => RotationOperation.Rotate90,
                    "水平翻转" => RotationOperation.FlipH,
                    "垂直翻转" => RotationOperation.FlipV,
                    "校正方向" => RotationOperation.CorrectCopy,
                    _ => RotationOperation.Rotate90
                };
            }
        }

        // ═══ 状态 ═══
        private string _statusText = "就绪";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
        private double _progressValue;
        /// <summary>执行进度，值域 0~1（0=未开始，1=完成）。消费端 ProgressBar 必须设置 Maximum="1"，否则按默认 0~100 渲染只显示不到 1%。</summary>
        public double ProgressValue { get => _progressValue; set { _progressValue = value; OnPropertyChanged(); } }
        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (_isProcessing == value) return;
                _isProcessing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotProcessing));
                // 执行中禁用列表操作（添加/文件夹/移除/清空），结束后恢复
                ((RelayCommand)RotateAddFilesCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RotateAddFolderCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RotateRemoveSelectedCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RotateClearCommand).NotifyCanExecuteChanged();
            }
        }
        /// <summary>IsProcessing 取反（供"开始执行"按钮显隐切换）</summary>
        public bool IsNotProcessing => !_isProcessing;

        /// <summary>方向转换执行完成且有失败文件时触发（参数为失败文件名列表），供 View 弹 Toast 提示</summary>
        public event Action<IReadOnlyList<string>>? RotationFailed;

        // ═══ 命令 ═══
        public ICommand SelectModeCommand { get; }
        public ICommand RotateAddFilesCommand { get; }
        public ICommand RotateAddFolderCommand { get; }
        public ICommand RotateRemoveSelectedCommand { get; }
        public ICommand RotateClearCommand { get; }
        public ICommand ExecuteRotationCommand { get; }
        public ICommand CancelRotationCommand { get; }

        // Crop
        public ICommand CropConfirmCommand { get; }

        /// <summary>执行中的取消令牌（执行结束后释放置空）</summary>
        private CancellationTokenSource? _cts;

        public ImageProcessViewModel()
        {
            SelectModeCommand = new RelayCommand<string>(s =>
            {
                SelectedMode = s switch
                {
                    "rotation" => ImageProcessMode.Rotation,
                    "crop" => ImageProcessMode.Crop,
                    "more" => ImageProcessMode.MoreFeatures,
                    _ => ImageProcessMode.Rotation
                };
            });

            RotateAddFilesCommand = new RelayCommand(ExecuteRotateAddFiles, () => !IsProcessing);
            RotateAddFolderCommand = new RelayCommand(ExecuteRotateAddFolder, () => !IsProcessing);
            RotateRemoveSelectedCommand = new RelayCommand(ExecuteRotateRemoveSelected, () => SelectedRotateFiles.Count > 0 && !IsProcessing);
            RotateClearCommand = new RelayCommand(ExecuteRotateClear, () => RotateFiles.Count > 0 && !IsProcessing);

            ExecuteRotationCommand = new RelayCommand(ExecuteRotation, () => RotateFiles.Count > 0 && !IsProcessing);
            CancelRotationCommand = new RelayCommand(CancelRotation, () => IsProcessing);
            CropConfirmCommand = new RelayCommand(() => { }, () => false); // placeholder
        }

        /// <summary>请求取消当前执行（在文件之间生效，单张图片处理不可中断）</summary>
        private void CancelRotation() => _cts?.Cancel();

        // ═══ 文件操作 ═══
        private void ExecuteRotateAddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff;*.tif;*.gif|所有文件|*.*"
            };
            if (dlg.ShowDialog() == true)
                AddFilesFromDialogPaths(dlg.FileNames);
        }

        /// <summary>把对话框选择的图片路径加入列表（自动去重），返回实际新增数；供对话框与单元测试复用</summary>
        public int AddFilesFromDialogPaths(IEnumerable<string> paths) => AddFilesToRotation(paths);

        private async void ExecuteRotateAddFolder()
        {
            // 直接选择文件夹（OpenFolderDialog，.NET 8+），导入其中所有图片及子文件夹
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "请选择要导入的文件夹（将导入其中所有图片及子文件夹）"
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FolderName))
            {
                // 后台线程枚举（大目录避免卡 UI），去重与添加回 UI 线程
                var images = await Task.Run(() => EnumerateImagesFromPaths(new[] { dlg.FolderName }).Images);
                AddFilesToRotation(images);
            }
        }

        /// <summary>
        /// 递归收集文件夹内全部图片并加入列表（自动去重），返回实际新增数。
        /// 目录不存在/无权限/IO 异常时记日志并返回 0，不向调用方抛出。
        /// </summary>
        public int AddImagesFromFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return 0;
            var images = EnumerateImagesFromPaths(new[] { folderPath }).Images;
            return AddFilesToRotation(images);
        }

        private void ExecuteRotateRemoveSelected()
        {
            // 多选移除：按实际选中项（Ctrl/Shift 多选或单选）全部移除
            foreach (var item in _selectedRotateFiles.ToList())
            {
                RotateFiles.Remove(item);
                _rotateFilePaths.Remove(item.FilePath);
            }
            RefreshRotateState();
        }

        private void ExecuteRotateClear()
        {
            RotateFiles.Clear();
            _rotateFilePaths.Clear();
            RefreshRotateState();
        }

        /// <summary>添加文件到旋转列表（HashSet O(1) 去重 + 批量添加），返回实际新增数量。须在 UI 线程调用。</summary>
        public int AddFilesToRotation(IEnumerable<string> paths)
        {
            var newItems = new List<RotateFileItem>();
            foreach (var p in paths)
            {
                if (_rotateFilePaths.Add(p))
                    newItems.Add(new RotateFileItem(p));
            }
            if (newItems.Count > 0)
                RotateFiles.AddRange(newItems); // 一次集合变更通知，避免大批量逐条 Add
            RefreshRotateState();
            return newItems.Count;
        }

        /// <summary>
        /// 从路径列表收集图片：目录递归枚举其中图片；文件按 IsImageFile 过滤。
        /// 纯 IO、无 UI 依赖，可安全在后台线程调用；单个路径无权限/IO 异常时记日志后跳过，不影响其余。
        /// 返回（图片路径列表，直接传入且不支持格式的文件列表——仅文件计入 Ignored，目录内部的不计）。
        /// </summary>
        public static (List<string> Images, List<string> Ignored) EnumerateImagesFromPaths(IEnumerable<string> paths)
        {
            var images = new List<string>();
            var ignored = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        foreach (var f in Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories))
                        {
                            if (IsImageFile(f)) images.Add(f);
                        }
                    }
                    else if (File.Exists(p))
                    {
                        if (IsImageFile(p)) images.Add(p);
                        else ignored.Add(p);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "收集图片失败：{Path}", p);
                }
            }
            return (images, ignored);
        }

        /// <summary>
        /// 拖放添加：文件按 IsImageFile 过滤格式；文件夹递归收集其中的图片。
        /// 返回（实际新增数量，被忽略的不支持格式文件列表）——仅直接拖入的文件计入 Ignored，文件夹内部的不计。
        /// 同步便捷入口（内部先收集再添加）；UI 大批量场景请拆用 EnumerateImagesFromPaths（后台） + AddFilesToRotation（UI）。
        /// </summary>
        public (int Added, IReadOnlyList<string> Ignored) AddDroppedFiles(IEnumerable<string> paths)
        {
            var (images, ignored) = EnumerateImagesFromPaths(paths);
            int added = AddFilesToRotation(images);
            return (added, ignored);
        }

        private void RefreshRotateState()
        {
            // 列表增删后统一重编号序号列（1 起）
            for (int i = 0; i < RotateFiles.Count; i++)
                RotateFiles[i].Index = i + 1;
            OnPropertyChanged(nameof(RotateFileCountText));
            ((RelayCommand)RotateClearCommand).NotifyCanExecuteChanged();
            ((RelayCommand)RotateRemoveSelectedCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ExecuteRotationCommand).NotifyCanExecuteChanged();
        }

        // ═══ 方向转换执行 ═══
        private async void ExecuteRotation()
        {
            IsProcessing = true;
            StatusText = "处理中...";
            ProgressValue = 0;
            // 复位所有文件状态：保证状态列反映本次执行，不残留上一次的结果
            // （第二次执行 / 换操作方式再执行 / 执行中取消时，未处理文件应为"待处理"而非旧的"成功"）
            ResetAllStatuses();

            int total = RotateFiles.Count;
            int done = 0, success = 0, fail = 0;
            bool cancelled = false;
            var failedFiles = new List<string>();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            // 进度节流：100 张以内每文件即时更新；超过 100 张每 50ms 更新一次（状态列始终即时）
            bool throttleProgress = total > 100;
            var progressTimer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in RotateFiles.ToList())
                    {
                        if (token.IsCancellationRequested)
                        {
                            cancelled = true;
                            break;
                        }
                        bool ok = false;
                        try
                        {
                        string dir = Path.GetDirectoryName(file.FilePath)!;
                        string name = Path.GetFileNameWithoutExtension(file.FilePath);
                        string suffix = SelectedRotation switch
                        {
                            RotationOperation.Rotate90 => "_右转90°",
                            RotationOperation.Rotate180 => "_旋转180°",
                            RotationOperation.Rotate270 => "_左转90°",
                            RotationOperation.FlipH => "_水平翻转",
                            RotationOperation.FlipV => "_垂直翻转",
                            RotationOperation.CorrectCopy => "_校正副本",
                            _ => "_rotated"
                        };
                        // 保持原格式输出；无编码支持的格式（tiff/gif）统一转 PNG（扩展名随内容同步改）
                        var (outFormat, outExt) = GetOutputFormat(file.FilePath);
                        string outPath = GetUniquePath(Path.Combine(dir, $"{name}{suffix}{outExt}"));

                        SKBitmap bitmap;
                        using (var input = SKImage.FromEncodedData(file.FilePath))
                            bitmap = SKBitmap.FromImage(input); // 解码源立即释放，降低内存峰值（不与其像素拷贝并存）

                        SKBitmap processed;
                        if (SelectedRotation == RotationOperation.CorrectCopy)
                        {
                            // 校正方向：读取 EXIF Orientation 自动摆正；无方向标记时等价复制（保持原行为）
                            var origin = ReadExifOrientation(file.FilePath);
                            processed = origin == SKEncodedOrigin.TopLeft
                                ? CopyBitmap(bitmap)
                                : ApplyExifCorrection(bitmap, origin);
                        }
                        else
                        {
                            processed = ApplyRotation(bitmap, SelectedRotation);
                        }

                        // 释放顺序：image（编码源）→ processed → bitmap，避免 Skia 像素引用悬挂
                        using (var image = SKImage.FromBitmap(processed))
                        {
                            int quality = outFormat is SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp ? 90 : 100;
                            var data = image.Encode(outFormat, quality);
                            if (data == null)
                            {
                                // 该格式无编码器（罕见）：回退 PNG，扩展名同步为 .png，保证扩展名与内容一致
                                outPath = GetUniquePath(Path.Combine(dir, $"{name}{suffix}.png"));
                                data = image.Encode(SKEncodedImageFormat.Png, 100);
                            }
                            using (data)
                            using (var fs = File.Create(outPath))
                            {
                                data.SaveTo(fs);
                            }
                        }
                        processed.Dispose();
                        bitmap.Dispose();
                        ok = true;
                        success++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "图片方向转换失败：{Path}", file.FilePath);
                        failedFiles.Add(file.FileName);
                        fail++;
                    }

                    done++;
                    // 状态即时 + 进度合并一次 Dispatcher；大批量时进度按 50ms 节流（状态不节流）
                    string status = ok ? "成功" : "失败";
                    if (!throttleProgress || progressTimer.ElapsedMilliseconds >= 50)
                    {
                        double progress = (double)done / total;
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            file.Status = status;
                            ProgressValue = progress;
                        });
                        progressTimer.Restart();
                    }
                    else
                    {
                        Application.Current?.Dispatcher.Invoke(() => file.Status = status);
                    }
                }
            });
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
            }

            if (!cancelled) ProgressValue = 1; // 取消时进度停在当前值
            StatusText = cancelled ? $"已取消：成功 {success}/{total}" : $"方向转换完成：成功 {success}/{total}";
            IsProcessing = false;
            ((RelayCommand)ExecuteRotationCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelRotationCommand).NotifyCanExecuteChanged();
            // 失败文件提示（Toast 由 View 订阅后弹出；列表"状态"列同时显示失败）
            if (fail > 0)
                RotationFailed?.Invoke(failedFiles);
        }

        /// <summary>
        /// 复位所有文件状态为"待处理"（执行开始前调用，须在 UI 线程）。
        /// 跳过已处于"待处理"的文件，减少不必要的属性通知；保证状态列始终反映本次执行结果。
        /// </summary>
        public void ResetAllStatuses()
        {
            foreach (var file in RotateFiles)
            {
                if (file.Status != "待处理")
                    file.Status = "待处理";
            }
        }

        private static SKBitmap ApplyRotation(SKBitmap source, RotationOperation op)
        {
            float angle = op switch
            {
                RotationOperation.Rotate90 => 90,
                RotationOperation.Rotate180 => 180,
                RotationOperation.Rotate270 => 270,
                _ => 0
            };

            SKBitmap rotated;
            if (angle != 0)
            {
                int w = angle % 180 == 0 ? source.Width : source.Height;
                int h = angle % 180 == 0 ? source.Height : source.Width;
                rotated = new SKBitmap(w, h);
                using var canvas = new SKCanvas(rotated);
                canvas.Translate(w / 2f, h / 2f);
                canvas.RotateDegrees(angle);
                canvas.DrawBitmap(source, -source.Width / 2f, -source.Height / 2f);
            }
            else
            {
                rotated = new SKBitmap(source.Width, source.Height);
                using var canvas = new SKCanvas(rotated);
                canvas.DrawBitmap(source, 0, 0);
            }

            if (op == RotationOperation.FlipH || op == RotationOperation.FlipV)
            {
                var flipped = new SKBitmap(rotated.Width, rotated.Height);
                using var canvas = new SKCanvas(flipped);
                float sx = op == RotationOperation.FlipH ? -1 : 1;
                float sy = op == RotationOperation.FlipV ? -1 : 1;
                canvas.Scale(sx, sy, rotated.Width / 2f, rotated.Height / 2f);
                canvas.DrawBitmap(rotated, 0, 0);
                rotated.Dispose();
                rotated = flipped;
            }

            return rotated;
        }

        // ═══ 校正方向（EXIF Orientation 自动摆正） ═══

        /// <summary>读取图片的 EXIF Orientation（编码方向）；读取失败按无需校正处理</summary>
        private static SKEncodedOrigin ReadExifOrientation(string path)
        {
            try
            {
                using var data = SKData.Create(path);
                using var codec = SKCodec.Create(data);
                return codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "读取 EXIF 方向失败，按无需校正处理：{Path}", path);
                return SKEncodedOrigin.TopLeft;
            }
        }

        /// <summary>
        /// EXIF Orientation → 摆正变换参数（angle 为顺时针角度；flipH/flipV 在旋转后应用，以画布中心为轴）。
        /// 1=TopLeft 正常；2=水平翻转；3=旋转180°；4=垂直翻转；5=旋转90°+水平翻转；6=顺时针90°（手机竖拍）；
        /// 7=旋转270°+水平翻转；8=逆时针90°。供单元测试直接验证映射。
        /// </summary>
        public static (float Angle, bool FlipH, bool FlipV) GetExifTransform(SKEncodedOrigin origin) => origin switch
        {
            SKEncodedOrigin.TopRight => (0f, true, false),
            SKEncodedOrigin.BottomRight => (180f, false, false),
            SKEncodedOrigin.BottomLeft => (0f, false, true),
            SKEncodedOrigin.LeftTop => (90f, true, false),
            SKEncodedOrigin.RightTop => (90f, false, false),
            SKEncodedOrigin.RightBottom => (270f, true, false),
            SKEncodedOrigin.LeftBottom => (270f, false, false),
            _ => (0f, false, false) // TopLeft 及其他：无需校正
        };

        /// <summary>按 EXIF 方向生成摆正后的位图（一次组合绘制，不产生中间位图）</summary>
        private static SKBitmap ApplyExifCorrection(SKBitmap source, SKEncodedOrigin origin)
        {
            var (angle, flipH, flipV) = GetExifTransform(origin);
            int w = angle % 180 == 0 ? source.Width : source.Height;
            int h = angle % 180 == 0 ? source.Height : source.Width;

            var result = new SKBitmap(w, h);
            using var canvas = new SKCanvas(result);
            canvas.Translate(w / 2f, h / 2f);
            canvas.RotateDegrees(angle);
            if (flipH || flipV)
                canvas.Scale(flipH ? -1 : 1, flipV ? -1 : 1);
            canvas.DrawBitmap(source, -source.Width / 2f, -source.Height / 2f);
            return result;
        }

        /// <summary>整图拷贝（无方向标记时校正方向退化为复制，保留原行为）</summary>
        private static SKBitmap CopyBitmap(SKBitmap source)
        {
            var copy = new SKBitmap(source.Width, source.Height);
            using var canvas = new SKCanvas(copy);
            canvas.DrawBitmap(source, 0, 0);
            return copy;
        }

        /// <summary>按扩展名判断是否支持的图片格式（供拖放预检等外部使用）</summary>
        public static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tiff" or ".tif" or ".gif";
        }

        /// <summary>
        /// 决定输出格式与扩展名：保持原格式；无编码支持的格式（tiff/gif）统一转 PNG（扩展名随内容改为 .png），
        /// 保证扩展名永远与实际编码内容一致。JPEG 扩展名保留源大小写（.jpg/.jpeg）。
        /// </summary>
        public static (SKEncodedImageFormat Format, string Ext) GetOutputFormat(string sourcePath)
        {
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            return ext switch
            {
                ".png" => (SKEncodedImageFormat.Png, ".png"),
                ".jpg" or ".jpeg" => (SKEncodedImageFormat.Jpeg, Path.GetExtension(sourcePath)),
                ".webp" => (SKEncodedImageFormat.Webp, ".webp"),
                ".bmp" => (SKEncodedImageFormat.Bmp, ".bmp"),
                _ => (SKEncodedImageFormat.Png, ".png")
            };
        }

        /// <summary>目标文件已存在时自动追加 _1/_2… 序号，避免静默覆盖已有文件</summary>
        public static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path)!;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
