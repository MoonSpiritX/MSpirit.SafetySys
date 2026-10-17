// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.ImageViewer;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class RecordCardView : UserControl
    {
        #region 图片管理 - 加载与显示

        private void LoadAttachments()
        {
            _hazardAttachments.Clear();
            _remedyAttachments.Clear();
            for (int i = 1; i <= 3; i++)
            {
                _hazardAttachments.Add(null);
                _remedyAttachments.Add(null);
            }

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null || Record == null) return;

            var attachments = dataService.GetAttachmentsByRecord(Record.Id);
            foreach (var att in attachments.Where(a => a != null))
            {
                // 自愈：物理文件不存在则清理DB记录并跳过
                if (!string.IsNullOrEmpty(att.RelativePath))
                {
                    string fullPath = Path.Combine(_attachmentsRootPath, att.RelativePath);
                    if (!File.Exists(fullPath))
                    {
                        dataService.DeleteAttachment(att.Id);
                        continue;
                    }
                }

                if (att.AttachmentType == "Hazard" && att.DisplayOrder >= 1 && att.DisplayOrder <= 3)
                {
                    _hazardAttachments[att.DisplayOrder - 1] = att;
                }
                else if (att.AttachmentType == "Remedy" && att.DisplayOrder >= 1 && att.DisplayOrder <= 3)
                {
                    _remedyAttachments[att.DisplayOrder - 1] = att;
                }
            }

            // 隐患照片：选择第一个有图片的槽位，否则索引为1
            _currentHazardIndex = 1;
            for (int i = 0; i < _hazardAttachments.Count; i++)
            {
                if (_hazardAttachments[i] != null)
                {
                    _currentHazardIndex = i + 1;
                    break;
                }
            }

            // 整改照片：选择第一个有图片的槽位，否则索引为1
            _currentRemedyIndex = 1;
            for (int i = 0; i < _remedyAttachments.Count; i++)
            {
                if (_remedyAttachments[i] != null)
                {
                    _currentRemedyIndex = i + 1;
                    break;
                }
            }

            UpdateAllButtonsAppearance();
            DisplayCurrentPhoto("Hazard");
            DisplayCurrentPhoto("Remedy");
        }

        private void UpdateAllButtonsAppearance()
        {
            UpdateButtonAppearance("Hazard", 1);
            UpdateButtonAppearance("Hazard", 2);
            UpdateButtonAppearance("Hazard", 3);
            UpdateButtonAppearance("Remedy", 1);
            UpdateButtonAppearance("Remedy", 2);
            UpdateButtonAppearance("Remedy", 3);
        }

        private void UpdateButtonAppearance(string type, int index)
        {
            Button btn = type == "Hazard"
                ? index == 1 ? HazardPhotoButton1 : index == 2 ? HazardPhotoButton2 : HazardPhotoButton3
                : index == 1 ? RemedyPhotoButton1 : index == 2 ? RemedyPhotoButton2 : RemedyPhotoButton3;
            if (btn == null) return;

            bool hasPhoto = (type == "Hazard" && _hazardAttachments[index - 1] != null) ||
                            (type == "Remedy" && _remedyAttachments[index - 1] != null);
            bool isCurrent = (type == "Hazard" && _currentHazardIndex == index) ||
                             (type == "Remedy" && _currentRemedyIndex == index);

            if (hasPhoto && isCurrent)
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#17c3b2"));
                btn.Foreground = Brushes.White;
                btn.BorderBrush = Brushes.White;
                btn.FontWeight = FontWeights.Bold;
                btn.ClearValue(Button.FontSizeProperty);
            }
            else if (hasPhoto && !isCurrent)
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCD4F1F9"));
                btn.Foreground = Brushes.Black;
                btn.BorderBrush = Brushes.White;
                btn.FontWeight = FontWeights.Normal;
                btn.ClearValue(Button.FontSizeProperty);
            }
            else if (!hasPhoto && isCurrent)
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = Brushes.Black;
                btn.BorderBrush = Brushes.White;
                btn.FontWeight = FontWeights.Bold;
                btn.ClearValue(Button.FontSizeProperty);
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = Brushes.Black;
                btn.BorderBrush = Brushes.White;
                btn.FontWeight = FontWeights.Normal;
                btn.ClearValue(Button.FontSizeProperty);
            }
        }

        private async void DisplayCurrentPhoto(string type)
        {
            var displayBorder = type == "Hazard" ? HazardPhotoDisplayArea : RemedyPhotoDisplayArea;
            var imageControl = type == "Hazard" ? HazardPhotoImage : RemedyPhotoImage;
            var placeholder = type == "Hazard" ? HazardPlaceholderText : RemedyPlaceholderText;

            int index = type == "Hazard" ? _currentHazardIndex : _currentRemedyIndex;
            Attachment? attachment = type == "Hazard"
                ? (_hazardAttachments.Count >= index ? _hazardAttachments[index - 1] : null)
                : (_remedyAttachments.Count >= index ? _remedyAttachments[index - 1] : null);

            if (attachment != null && !string.IsNullOrEmpty(attachment.ThumbnailPath))
            {
                string fullPath = Path.Combine(_attachmentsRootPath ?? "", attachment.ThumbnailPath);
                if (File.Exists(fullPath))
                {
                    var thumbnail = await ImageHelper.LoadThumbnailAsync(fullPath, 300, 300);
                    if (thumbnail != null)
                    {
                        imageControl.Source = thumbnail;
                        imageControl.Visibility = Visibility.Visible;
                        placeholder.Visibility = Visibility.Collapsed;
                        return;
                    }
                }
            }
            imageControl.Visibility = Visibility.Collapsed;
            placeholder.Visibility = Visibility.Visible;
        }

        private async Task AddPhoto(string type, int index, string filePath)
        {
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                await AddPhoto(type, index, fileStream, Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"添加图片失败: {ex.Message}", "错误");
            }
        }

        /// <summary>
        /// 添加图片核心实现：粘贴/拖入/文件对话框均汇入此入口，
        /// 统一经 ImageHelper.SaveImageToRecordAsync 存档、生成缩略图并命名。
        /// </summary>
        private async Task AddPhoto(string type, int index, Stream stream, string fileName)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null || Record == null) return;

            Attachment? oldAttachment = null;
            if (type == "Hazard")
                oldAttachment = _hazardAttachments[index - 1];
            else
                oldAttachment = _remedyAttachments[index - 1];

            var btn = GetButton(type, index);
            btn.Content = "●";

            try
            {
                long fileSize = stream.Length;
                var (originalPath, thumbPath) = await ImageHelper.SaveImageToRecordAsync(
                    stream,
                    Record.Id,
                    fileName,
                    _attachmentsRootPath,
                    _projectId,
                    _currentModule,
                    Record.YearMonth ?? "Unknown",
                    type,
                    index);

                var newAttachment = new Attachment
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RecordId = Record.Id,
                    FileName = fileName,
                    RelativePath = originalPath,
                    ThumbnailPath = thumbPath,
                    AttachmentType = type,
                    DisplayOrder = index,
                    FileSize = fileSize,
                    MimeType = "image/jpeg"
                };

                if (dataService.AddAttachment(newAttachment))
                {
                    if (type == "Hazard")
                        _hazardAttachments[index - 1] = newAttachment;
                    else
                        _remedyAttachments[index - 1] = newAttachment;

                    if ((type == "Hazard" && _currentHazardIndex == index) ||
                        (type == "Remedy" && _currentRemedyIndex == index))
                    {
                        DisplayCurrentPhoto(type);
                    }
                    UpdateButtonAppearance(type, index);

                    if (oldAttachment != null)
                    {
                        string oldOriginalFullPath = Path.Combine(_attachmentsRootPath ?? "", oldAttachment.RelativePath);
                        if (File.Exists(oldOriginalFullPath))
                            File.Delete(oldOriginalFullPath);
                        if (!string.IsNullOrEmpty(oldAttachment.ThumbnailPath))
                        {
                            string oldThumbFullPath = Path.Combine(_attachmentsRootPath ?? "", oldAttachment.ThumbnailPath);
                            if (File.Exists(oldThumbFullPath))
                                File.Delete(oldThumbFullPath);
                        }
                        dataService.DeleteAttachment(oldAttachment.Id);
                    }
                }
                else
                {
                    MessageDialog.Show("保存图片信息失败", "错误");
                }
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"添加图片失败: {ex.Message}", "错误");
            }
            finally
            {
                btn.Content = index.ToString();
            }
        }

        private void SwitchPhoto(string type, int index)
        {
            if (type == "Hazard")
            {
                _currentHazardIndex = index;
                UpdateAllButtonsAppearance();
                DisplayCurrentPhoto("Hazard");
            }
            else
            {
                _currentRemedyIndex = index;
                UpdateAllButtonsAppearance();
                DisplayCurrentPhoto("Remedy");
            }
        }

        private Button GetButton(string type, int index)
        {
            return type == "Hazard"
                ? index == 1 ? HazardPhotoButton1 : index == 2 ? HazardPhotoButton2 : HazardPhotoButton3
                : index == 1 ? RemedyPhotoButton1 : index == 2 ? RemedyPhotoButton2 : RemedyPhotoButton3;
        }

        private void DeletePhoto(string type, int index, bool silent = false)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null || Record == null) return;

            Attachment? attachment = type == "Hazard" ? _hazardAttachments[index - 1] : _remedyAttachments[index - 1];
            if (attachment == null) return;

            if (!silent)
            {
                var result = MessageDialog.Confirm($"确定要删除当前的{(type == "Hazard" ? "隐患" : "整改")}照片（第{index}张）吗？", "确认删除");
                if (result != true) return;
            }

            string originalFullPath = Path.Combine(_attachmentsRootPath ?? "", attachment.RelativePath);
            if (File.Exists(originalFullPath))
                ImageHelper.DeleteToRecycleBin(originalFullPath);

            if (!string.IsNullOrEmpty(attachment.ThumbnailPath))
            {
                string thumbFullPath = Path.Combine(_attachmentsRootPath ?? "", attachment.ThumbnailPath);
                if (File.Exists(thumbFullPath))
                    ImageHelper.DeleteToRecycleBin(thumbFullPath);
            }

            if (dataService.DeleteAttachment(attachment.Id))
            {
                if (type == "Hazard")
                    _hazardAttachments[index - 1] = null;
                else
                    _remedyAttachments[index - 1] = null;

                if ((type == "Hazard" && _currentHazardIndex == index) ||
                    (type == "Remedy" && _currentRemedyIndex == index))
                {
                    int newIndex = 1;
                    for (int i = 1; i <= 3; i++)
                    {
                        bool hasPhoto = (type == "Hazard" && _hazardAttachments[i - 1] != null) ||
                                        (type == "Remedy" && _remedyAttachments[i - 1] != null);
                        if (hasPhoto)
                        {
                            newIndex = i;
                            break;
                        }
                    }
                    if (type == "Hazard")
                    {
                        _currentHazardIndex = newIndex;
                        DisplayCurrentPhoto("Hazard");
                    }
                    else
                    {
                        _currentRemedyIndex = newIndex;
                        DisplayCurrentPhoto("Remedy");
                    }
                }

                UpdateAllButtonsAppearance();
            }
            else
            {
                if (!silent) MessageDialog.Show("删除图片失败", "错误");
            }
        }

        private async void RearrangePhotos(string type)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null || Record == null) return;

            List<Attachment> attachments;
            if (type == "Hazard")
                attachments = _hazardAttachments.Where(a => a != null).Select(a => a!).OrderBy(a => a.DisplayOrder).ToList();
            else
                attachments = _remedyAttachments.Where(a => a != null).Select(a => a!).OrderBy(a => a.DisplayOrder).ToList();

            if (attachments.Count == 0)
            {
                ToastService.Show("没有需要整理的图片", type == "Hazard" ? HazardPhotoDisplayArea : RemedyPhotoDisplayArea);
                return;
            }

            string confirmMessage = type == "Hazard"
                ? "将当前卡片中的隐患照片重新排列到槽位 1、2、3，移除空缺。是否继续？"
                : "将当前卡片中的整改照片重新排列到槽位 1、2、3，移除空缺。是否继续？";
            if (MessageDialog.Confirm(confirmMessage, "确认整理") != true)
                return;

            var renameOperations = new List<(string oldFullPath, string newFullPath)>();
            var updateAttachments = new List<Attachment>();

            try
            {
                for (int i = 0; i < attachments.Count; i++)
                {
                    var att = attachments[i];
                    int newOrder = i + 1;
                    if (att.DisplayOrder == newOrder) continue;

                    string oldRelativePath = att.RelativePath;
                    string oldThumbPath = att.ThumbnailPath ?? "";
                    string directory = Path.GetDirectoryName(oldRelativePath) ?? "";
                    string oldFileName = Path.GetFileName(oldRelativePath);
                    string oldThumbFileName = Path.GetFileName(oldThumbPath) ?? "";

                    var guidMatch = Regex.Match(oldFileName, @"[0-9a-f]{32}", RegexOptions.IgnoreCase);
                    if (!guidMatch.Success)
                        throw new Exception("无法解析原图片文件名中的 GUID");

                    string guid = guidMatch.Value;
                    string newFileName = $"{type}-{newOrder}_{guid}.jpg";
                    string newThumbFileName = $"{type}-{newOrder}_{guid}_thumb.jpg";
                    string newRelativePath = Path.Combine(directory, newFileName);
                    string newThumbRelativePath = Path.Combine(directory, newThumbFileName);

                    string oldFullPath = Path.Combine(_attachmentsRootPath ?? "", oldRelativePath);
                    string newFullPath = Path.Combine(_attachmentsRootPath ?? "", newRelativePath);
                    string oldThumbFullPath = Path.Combine(_attachmentsRootPath ?? "", oldThumbPath);
                    string newThumbFullPath = Path.Combine(_attachmentsRootPath ?? "", newThumbRelativePath);

                    if (File.Exists(newFullPath) || File.Exists(newThumbFullPath))
                        throw new Exception($"目标文件已存在，无法重命名: {newFileName}");

                    renameOperations.Add((oldFullPath, newFullPath));
                    renameOperations.Add((oldThumbFullPath, newThumbFullPath));

                    att.DisplayOrder = newOrder;
                    att.RelativePath = newRelativePath;
                    att.ThumbnailPath = newThumbRelativePath;
                    updateAttachments.Add(att);
                }

                foreach (var (oldPath, newPath) in renameOperations)
                {
                    if (File.Exists(oldPath))
                        File.Move(oldPath, newPath);
                    else
                        throw new Exception($"文件不存在: {oldPath}");
                }

                foreach (var att in updateAttachments)
                {
                    if (!dataService.UpdateAttachment(att))
                        throw new Exception($"更新数据库失败: {att.Id}");
                }
                LoadAttachments();
                ToastService.Show("整理完成", type == "Hazard" ? HazardPhotoDisplayArea : RemedyPhotoDisplayArea);
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"整理失败：{ex.Message}\n请检查文件是否被其他程序占用。", "错误");
            }
        }

        #endregion

        #region 图片事件处理

        private void PhotoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                var parts = tag.Split('_');
                if (parts.Length == 2)
                {
                    string type = parts[0];
                    int index = int.Parse(parts[1]);
                    SwitchPhoto(type, index);
                }
            }
        }

        private async void ImageDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            Border? targetBorder = sender as Border;
            if (targetBorder == null) return;

            string targetType = targetBorder == HazardPhotoDisplayArea ? "Hazard" : "Remedy";

            var emptySlots = GetEmptyPhotoSlots(targetType);

            if (emptySlots.Count == 0)
            {
                MessageDialog.Show("当前所有槽位已有图片，请先删除后再添加。", "提示");
                return;
            }

            var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
            };

            var validFiles = files
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                .Take(emptySlots.Count)
                .ToList();

            if (validFiles.Count == 0)
            {
                string firstInvalid = files.FirstOrDefault(f => !supportedExtensions.Contains(Path.GetExtension(f))) ?? "";
                if (!string.IsNullOrEmpty(firstInvalid))
                    MessageDialog.Show($"文件“{Path.GetFileName(firstInvalid)}”不是支持的图片格式。\n支持的格式：{string.Join(", ", supportedExtensions)}", "提示");
                return;
            }

            for (int i = 0; i < validFiles.Count && i < emptySlots.Count; i++)
            {
                await AddPhoto(targetType, emptySlots[i], validFiles[i]);
            }

            if (validFiles.Count > 0)
                SwitchPhoto(targetType, emptySlots[0]);

            if (files.Length > validFiles.Count)
            {
                MessageDialog.Show($"仅支持图片文件。已添加 {validFiles.Count} 张图片，{files.Length - validFiles.Count} 个非图片文件被忽略。", "提示");
            }
        }

        private void ImageDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void AddPhotosFromDialog(string type)
        {
            if (Record == null) return;

            var emptySlots = GetEmptyPhotoSlots(type);

            if (emptySlots.Count == 0)
            {
                MessageDialog.Show("当前所有槽位已有图片，请先删除后再添加。", "提示");
                return;
            }

            var openFileDialog = new OpenFileDialog
            {
                Title = $"添加{(type == "Hazard" ? "隐患" : "整改")}照片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp",
                Multiselect = true,
                InitialDirectory = _lastImportDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            string? selectedDir = Path.GetDirectoryName(openFileDialog.FileName);
            if (!string.IsNullOrEmpty(selectedDir))
                _lastImportDirectory = selectedDir;

            string[] selectedFiles = openFileDialog.FileNames;
            int addedCount = 0;
            int slotIndex = 0;

            foreach (var filePath in selectedFiles)
            {
                if (slotIndex >= emptySlots.Count)
                    break;

                int targetSlot = emptySlots[slotIndex];
                await AddPhoto(type, targetSlot, filePath);
                addedCount++;
                slotIndex++;
            }

            if (addedCount > 0)
                SwitchPhoto(type, emptySlots[0]);

            if (selectedFiles.Length > addedCount)
            {
                MessageDialog.Show($"已添加 {addedCount} 张图片，剩余 {selectedFiles.Length - addedCount} 张因槽位不足被忽略。", "提示");
            }
        }

        /// <summary>当前类型的空图片槽位（从 1 开始）</summary>
        private List<int> GetEmptyPhotoSlots(string type)
        {
            var attachmentsList = type == "Hazard" ? _hazardAttachments : _remedyAttachments;
            var emptySlots = new List<int>();
            for (int i = 0; i < attachmentsList.Count; i++)
            {
                if (attachmentsList[i] == null)
                    emptySlots.Add(i + 1);
            }
            return emptySlots;
        }

        /// <summary>
        /// 从剪贴板粘贴图片：支持资源管理器复制的图片文件，也支持 Word/网页/截图等复制的图像数据。
        /// 与“添加图片/拖入”一样统一走 AddPhoto 存档、生成缩略图并命名。
        /// </summary>
        private async void PastePhotosFromClipboard(string type)
        {
            if (Record == null) return;

            var emptySlots = GetEmptyPhotoSlots(type);
            if (emptySlots.Count == 0)
            {
                MessageDialog.Show("当前所有槽位已有图片，请先删除后再添加。", "提示");
                return;
            }

            try
            {
                // 1) 剪贴板中是图像数据（网页/文档/截图工具中复制，格式不限，含 webp）
                if (Clipboard.ContainsImage())
                {
                    var bitmap = Clipboard.GetImage();
                    if (bitmap == null)
                    {
                        ToastService.Show("剪贴板中没有可用的图片", this);
                        return;
                    }

                    string fileName = $"Pasted-{DateTime.Now:yyyyMMddHHmmss}.png";
                    using var memoryStream = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(memoryStream);
                    memoryStream.Position = 0;

                    await AddPhoto(type, emptySlots[0], memoryStream, fileName);
                    SwitchPhoto(type, emptySlots[0]);
                    return;
                }

                // 2) 剪贴板中是复制的图片文件（文件夹/资源管理器中复制）
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                    var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
                    };
                    var validFiles = files
                        .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                        .ToList();

                    if (validFiles.Count == 0)
                    {
                        ToastService.Show("剪贴板中的文件不是支持的图片格式", this);
                        return;
                    }

                    int addedCount = 0;
                    for (int i = 0; i < validFiles.Count && i < emptySlots.Count; i++)
                    {
                        await AddPhoto(type, emptySlots[i], validFiles[i]);
                        addedCount++;
                    }

                    if (addedCount > 0)
                        SwitchPhoto(type, emptySlots[0]);

                    if (validFiles.Count > addedCount)
                        MessageDialog.Show($"已粘贴 {addedCount} 张图片，剩余 {validFiles.Count - addedCount} 张因槽位不足被忽略。", "提示");
                    return;
                }

                ToastService.Show("剪贴板中没有图片", this);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "粘贴图片失败");
                ToastService.Show($"粘贴图片失败：{ex.Message}", this);
            }
        }

        private void PhotoImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                OpenCurrentPhoto(sender as Image);
                e.Handled = true;
            }
        }

        private void OpenCurrentPhoto(Image? clickedImage)
        {
            if (clickedImage == null) return;

            string type = clickedImage == HazardPhotoImage ? "Hazard" : "Remedy";
            int currentIndex = type == "Hazard" ? _currentHazardIndex : _currentRemedyIndex;
            Attachment? attachment = type == "Hazard" ? _hazardAttachments[currentIndex - 1] : _remedyAttachments[currentIndex - 1];
            if (attachment == null)
            {
                MessageDialog.Show("当前没有图片可查看", "提示");
                return;
            }

            string fullPath = Path.Combine(_attachmentsRootPath ?? "", attachment.RelativePath);
            if (!File.Exists(fullPath))
            {
                MessageDialog.Show("图片文件不存在，可能已被删除。", "提示");
                return;
            }

            if (_settingsService == null)
            {
                MessageDialog.Show("配置服务未初始化", "错误");
                return;
            }

            string viewerType = _settingsService.AppSettings.ImageViewerType;
            string startMode = _settingsService.AppSettings.ImageViewerStartMode;
            Log.Information($"[ImageViewer] OpenCurrentPhoto: viewerType={viewerType}, startMode={startMode}");

            if (viewerType == ImageViewerType.Internal)
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var dataService = mainWindow?.ViewModel.GetProjectDataService();
                if (dataService == null)
                {
                    MessageDialog.Show("数据服务未初始化", "错误");
                    return;
                }

                var recordsInGroup = dataService.GetRecordsByGroup(Record.GroupId)
                    .OrderBy(r => r.DisplayIndex)
                    .Select(r => r.Id)
                    .ToList();

                string baseDir = Path.Combine(_attachmentsRootPath, $"Project_{_projectId}",
                    $"{_currentModule}_{Record.YearMonth}");

                var (allImages, imageIndex) = ImageViewerHelper.BuildModuleImageIndex(
                    baseDir, recordsInGroup, fullPath);

                bool startFullScreen = startMode == ImageViewerStartMode.FullScreen;
                Log.Information($"[ImageViewer] LaunchAsProcess: allImages.Count={allImages.Count}, imageIndex={imageIndex}, startFullScreen={startFullScreen}");
                ImageViewerLauncher.LaunchAsProcess(allImages, imageIndex, startFullScreen);
                _pendingPhotoRefresh = true;
            }
            else
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception ex)
                {
                    MessageDialog.Show($"无法打开图片：{ex.Message}", "错误");
                }
            }
        }

        private void PhotoPrevButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is string type)
            {
                int currentIndex = type == "Hazard" ? _currentHazardIndex : _currentRemedyIndex;
                int newIndex = currentIndex - 1;
                if (newIndex < 1) newIndex = 3;
                SwitchPhoto(type, newIndex);
            }
            e.Handled = true;
        }

        private void PhotoNextButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is string type)
            {
                int currentIndex = type == "Hazard" ? _currentHazardIndex : _currentRemedyIndex;
                int newIndex = currentIndex + 1;
                if (newIndex > 3) newIndex = 1;
                SwitchPhoto(type, newIndex);
            }
            e.Handled = true;
        }

        private void CopyPhotoToClipboard(Attachment attachment)
        {
            try
            {
                string fullPath = Path.Combine(_attachmentsRootPath ?? "", attachment.RelativePath);
                if (!File.Exists(fullPath))
                {
                    MessageDialog.Show("图片文件不存在", "提示");
                    return;
                }
                var bitmap = new BitmapImage(new Uri(fullPath));
                Clipboard.SetImage(bitmap);
                var target = attachment.AttachmentType == "Hazard" ? HazardPhotoDisplayArea : RemedyPhotoDisplayArea;
                ToastService.Show("图片已复制到剪贴板", target);
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"复制失败：{ex.Message}", "错误");
            }
        }

        private void ExportPhotoToDesktop(Attachment attachment)
        {
            string sourcePath = Path.Combine(_attachmentsRootPath ?? "", attachment.RelativePath);
            if (!File.Exists(sourcePath))
            {
                MessageDialog.Show("图片文件不存在", "提示");
                return;
            }

            string fullDate = GetRecordFullDate();
            int globalIndex = GetGlobalCardIndex();
            string attachmentType = attachment.AttachmentType;
            int displayOrder = attachment.DisplayOrder;
            string idShort = attachment.Id.Length >= 8 ? attachment.Id[..8] : attachment.Id;
            string fileName = $"{fullDate}_{globalIndex}_{attachmentType}-{displayOrder}_{idShort}.jpg";

            string initialDir = string.IsNullOrEmpty(_lastExportDirectory) || !Directory.Exists(_lastExportDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : _lastExportDirectory;

            var saveDialog = new SaveFileDialog
            {
                Title = "导出图片",
                Filter = "JPEG 图片|*.jpg",
                FileName = fileName,
                InitialDirectory = initialDir
            };
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(sourcePath, saveDialog.FileName, true);
                    _lastExportDirectory = Path.GetDirectoryName(saveDialog.FileName);
                    UIElement target = attachment.AttachmentType == "Hazard" ? HazardPhotoDisplayArea : RemedyPhotoDisplayArea;
                    ToastService.Show("图片已导出", target);
                }
                catch (Exception ex)
                {
                    MessageDialog.Show($"导出失败：{ex.Message}", "错误");
                }
            }
        }

        #endregion

        #region 图片右键菜单

        private void PhotoDisplayArea_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            string type = border == HazardPhotoDisplayArea ? "Hazard" : "Remedy";
            int currentIndex = type == "Hazard" ? _currentHazardIndex : _currentRemedyIndex;
            Attachment? attachment = type == "Hazard" ? _hazardAttachments[currentIndex - 1] : _remedyAttachments[currentIndex - 1];

            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            // 关闭已有菜单
            _activeCardMenu?.Close();
            _activeCardMenu = null;

            var menu = new DropdownMenu();

            if (attachment == null)
            {
                menu.AddItem("添加图片", () => AddPhotosFromDialog(type));
                menu.AddItem("粘贴图片", () => PastePhotosFromClipboard(type));
            }
            else
            {
                menu.AddItem("复制图片", () => CopyPhotoToClipboard(attachment));
                menu.AddItem("导出图片", () => ExportPhotoToDesktop(attachment));
                string capturedType = type;
                int capturedIndex = currentIndex;
                menu.AddItem("删除图片", () => DeletePhoto(capturedType, capturedIndex));
                menu.AddItem("整理图片排序", () => RearrangePhotos(type));
            }

            menu.Closed += () => _activeCardMenu = null;
            _activeCardMenu = menu;
            menu.ShowAt(wpfPoint);

            e.Handled = true;
        }

        private void PhotoDisplayArea_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        #endregion

        #region 图片导航动画

        private string? _currentShowDirection;

        private void PhotoDisplayArea_MouseMove(object sender, MouseEventArgs e)
        {
            var area = sender as Border;
            if (area == null) return;

            var point = e.GetPosition(area);
            double width = area.ActualWidth;
            if (width <= 0) return;

            string? newDirection = null;
            if (point.X < width / 5)
                newDirection = "Prev";
            else if (point.X > width * 4 / 5)
                newDirection = "Next";

            if (newDirection != _currentShowDirection)
            {
                _currentShowDirection = newDirection;
                if (newDirection == null)
                    HideNavigationButtons();
                else
                    ShowNavigationButton(area, newDirection);
            }
        }

        private void PhotoDisplayArea_MouseLeave(object sender, MouseEventArgs e)
        {
            _currentShowDirection = null;
            HideNavigationButtons();
        }

        private void ShowNavigationButton(Border area, string direction)
        {
            if (area == HazardPhotoDisplayArea)
            {
                if (direction == "Prev")
                {
                    if (HazardPrevButton.Visibility != Visibility.Visible || HazardPrevButton.Opacity < 0.99)
                        AnimateButtonFadeIn(HazardPrevButton);
                    AnimateButtonFadeOut(HazardNextButton);
                }
                else
                {
                    if (HazardNextButton.Visibility != Visibility.Visible || HazardNextButton.Opacity < 0.99)
                        AnimateButtonFadeIn(HazardNextButton);
                    AnimateButtonFadeOut(HazardPrevButton);
                }
            }
            else if (area == RemedyPhotoDisplayArea)
            {
                if (direction == "Prev")
                {
                    if (RemedyPrevButton.Visibility != Visibility.Visible || RemedyPrevButton.Opacity < 0.99)
                        AnimateButtonFadeIn(RemedyPrevButton);
                    AnimateButtonFadeOut(RemedyNextButton);
                }
                else
                {
                    if (RemedyNextButton.Visibility != Visibility.Visible || RemedyNextButton.Opacity < 0.99)
                        AnimateButtonFadeIn(RemedyNextButton);
                    AnimateButtonFadeOut(RemedyPrevButton);
                }
            }
        }

        private void HideNavigationButtons()
        {
            AnimateButtonFadeOut(HazardPrevButton);
            AnimateButtonFadeOut(HazardNextButton);
            AnimateButtonFadeOut(RemedyPrevButton);
            AnimateButtonFadeOut(RemedyNextButton);
        }

        private void AnimateButtonFadeIn(Button btn)
        {
            if (btn == null) return;
            if (btn.Visibility == Visibility.Visible && Math.Abs(btn.Opacity - 1.0) < 0.01)
                return;
            btn.BeginAnimation(Button.OpacityProperty, null);
            btn.Visibility = Visibility.Visible;
            btn.Opacity = 0;
            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            animation.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            btn.BeginAnimation(Button.OpacityProperty, animation);
        }

        private void AnimateButtonFadeOut(Button btn)
        {
            if (btn == null) return;
            if (btn.Visibility != Visibility.Visible)
                return;
            btn.BeginAnimation(Button.OpacityProperty, null);
            var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            animation.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            animation.Completed += (s, _) =>
            {
                btn.Visibility = Visibility.Collapsed;
                btn.Opacity = 1;
            };
            btn.BeginAnimation(Button.OpacityProperty, animation);
        }

        #endregion

        #region 辅助方法（卡片信息查询）

        private string GetRecordFullDate()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null) return DateTime.Now.ToString("yyyy-MM-dd");

            var allGroups = dataService.GetAllGroups(_currentModule);
            var group = allGroups.FirstOrDefault(g => g.Id == Record.GroupId);
            if (group == null || string.IsNullOrEmpty(group.ParentId))
                return DateTime.Now.ToString("yyyy-MM-dd");

            var parent = allGroups.FirstOrDefault(g => g.Id == group.ParentId);
            if (parent == null) return DateTime.Now.ToString("yyyy-MM-dd");

            var yearMatch = Regex.Match(parent.Name, @"(\d{4})年(\d{1,2})月");
            if (!yearMatch.Success) return DateTime.Now.ToString("yyyy-MM-dd");
            int year = int.Parse(yearMatch.Groups[1].Value);
            int month = int.Parse(yearMatch.Groups[2].Value);

            var dayMatch = Regex.Match(group.Name, @"(\d{1,2})月(\d{1,2})日");
            if (!dayMatch.Success) return DateTime.Now.ToString("yyyy-MM-dd");
            int day = int.Parse(dayMatch.Groups[2].Value);

            return $"{year:D4}-{month:D2}-{day:D2}";
        }

        private int GetGlobalCardIndex()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null) return Record.DisplayIndex;

            string fullDate = GetRecordFullDate();
            var parts = fullDate.Split('-');
            if (parts.Length != 3) return Record.DisplayIndex;
            int year = int.Parse(parts[0]);
            int month = int.Parse(parts[1]);
            int day = int.Parse(parts[2]);

            string yearMonthName = $"{year}年{month:D2}月";
            var yearMonthGroups = dataService.GetGroups(_currentModule, null);
            var yearMonthGroup = yearMonthGroups.FirstOrDefault(g => g.Name == yearMonthName);
            if (yearMonthGroup == null) return Record.DisplayIndex;

            var dateGroups = dataService.GetGroups(_currentModule, yearMonthGroup.Id)
                .Where(g => Regex.IsMatch(g.Name, $@"{month:D2}月{day:D2}日"))
                .ToList();

            var orderedGroups = dateGroups
                .OrderBy(g => string.IsNullOrEmpty(g.Tag) ? 0 : 1)
                .ThenBy(g => g.Tag, StringComparer.Ordinal)
                .ToList();

            var allCards = new List<Record>();
            foreach (var grp in orderedGroups)
            {
                var cards = dataService.GetRecordsByGroup(grp.Id)
                    .OrderBy(r => r.DisplayIndex)
                    .ToList();
                allCards.AddRange(cards);
            }

            int index = allCards.FindIndex(r => r.Id == Record.Id);
            return index >= 0 ? index + 1 : Record.DisplayIndex;
        }

        private int GetRecordCurrentYear()
        {
            if (Record == null) return DateTime.Now.Year;
            if (!string.IsNullOrEmpty(Record.YearMonth))
            {
                var parts = Record.YearMonth.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int year))
                    return year;
            }
            return DateTime.Now.Year;
        }

        private int GetRecordCurrentMonth()
        {
            if (Record == null) return DateTime.Now.Month;
            if (!string.IsNullOrEmpty(Record.YearMonth))
            {
                var parts = Record.YearMonth.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[1], out int month))
                    return month;
            }
            return DateTime.Now.Month;
        }

        #endregion
    }
}
