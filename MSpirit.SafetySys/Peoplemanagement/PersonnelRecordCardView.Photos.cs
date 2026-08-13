// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.ImageViewer;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Views;
using Serilog;
using SkiaSharp;
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

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelRecordCardView : UserControl
    {
        #region 图片管理

        private void LoadAttachments(int? preserveIDCardSlot = null, int? preserveCertSlot = null)
        {
            if (Record == null) return;

            List<Attachment>? allAttachments = null;
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow?.ViewModel?.PersonnelRecords is PersonnelRecordsViewModel vm)
            {
                var cached = vm.GetCachedAttachments(Record.Id);
                if (cached != null)
                    allAttachments = cached.ToList();
            }

            if (allAttachments == null)
            {
                var dataService = GetDataService();
                if (dataService == null) return;
                allAttachments = dataService.GetAttachmentsByRecord(Record.Id);
            }

            // 自愈：检查物理文件是否存在，不存在则清理DB记录并从列表中移除
            var dataServiceForHeal = GetDataService();
            if (dataServiceForHeal != null && allAttachments.Count > 0)
            {
                string projectsDateRoot = GetProjectsDateRoot();
                var missingIds = new List<string>();
                foreach (var att in allAttachments)
                {
                    if (!string.IsNullOrEmpty(att.RelativePath))
                    {
                        string fullPath = Path.Combine(projectsDateRoot, att.RelativePath);
                        if (!File.Exists(fullPath))
                            missingIds.Add(att.Id);
                    }
                }
                if (missingIds.Count > 0)
                {
                    foreach (var id in missingIds)
                        dataServiceForHeal.DeleteAttachment(id);
                    allAttachments = allAttachments.Where(a => !missingIds.Contains(a.Id)).ToList();
                }
            }

            _idCardAttachments = new List<Attachment?>(new Attachment?[2]);
            foreach (var att in allAttachments.Where(a => a.AttachmentType == PersonnelConstants.AttachmentTypeIDCard))
            {
                if (att.DisplayOrder >= 1 && att.DisplayOrder <= 2)
                    _idCardAttachments[att.DisplayOrder - 1] = att;
            }

            _certAttachments = new List<Attachment?>(new Attachment?[5]);
            foreach (var att in allAttachments.Where(a => a.AttachmentType == PersonnelConstants.AttachmentTypeCertificate))
            {
                if (att.DisplayOrder >= 1 && att.DisplayOrder <= 5)
                    _certAttachments[att.DisplayOrder - 1] = att;
            }

            _currentIDCardSlot = preserveIDCardSlot ?? FindFirstNonEmptySlot(_idCardAttachments, 1);
            _currentCertSlot = preserveCertSlot ?? FindFirstNonEmptySlot(_certAttachments, 1);
            LoadDefaultCertSlotText();
            UpdatePhotoDisplay("IDCard");
            UpdatePhotoDisplay("Certificate");
            UpdateAllButtonsAppearance();
        }

        private int FindFirstNonEmptySlot(List<Attachment?> attachments, int defaultSlot)
        {
            for (int i = 0; i < attachments.Count; i++)
            {
                if (attachments[i] != null)
                    return i + 1;
            }
            return defaultSlot;
        }

        private void UpdateAllButtonsAppearance()
        {
            UpdateButtonAppearance("IDCard", 1);
            UpdateButtonAppearance("IDCard", 2);
            UpdateButtonAppearance("Cert", 1);
            UpdateButtonAppearance("Cert", 2);
            UpdateButtonAppearance("Cert", 3);
            UpdateButtonAppearance("Cert", 4);
            UpdateButtonAppearance("Cert", 5);
        }

        private void UpdateButtonAppearance(string type, int index)
        {
            Button? btn = GetSlotButton(type, index);
            if (btn == null) return;

            bool hasPhoto = (type == "IDCard" && _idCardAttachments.Count >= index && _idCardAttachments[index - 1] != null) ||
                            (type == "Cert" && _certAttachments.Count >= index && _certAttachments[index - 1] != null);
            bool isCurrent = (type == "IDCard" && _currentIDCardSlot == index) ||
                             (type == "Cert" && _currentCertSlot == index);

            if (hasPhoto && isCurrent)
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#17c3b2"));
                btn.Foreground = Brushes.White;
                btn.BorderBrush = Brushes.White;
                btn.FontWeight = FontWeights.Bold;
            }
            else if (hasPhoto && !isCurrent)
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCD4F1F9"));
                btn.Foreground = Brushes.Black;
                btn.BorderBrush = Brushes.White;
                btn.FontWeight = FontWeights.Normal;
            }
            else if (!hasPhoto && isCurrent)
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = Brushes.Black;
                btn.BorderBrush = Brushes.White;
                btn.FontWeight = FontWeights.Bold;
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = Brushes.Black;
                btn.BorderBrush = Brushes.White;
                btn.FontWeight = FontWeights.Normal;
            }
        }

        private Button? GetSlotButton(string type, int index)
        {
            if (type == "IDCard")
                return index == 1 ? IDCardFrontButton : index == 2 ? IDCardBackButton : null;
            if (type == "Cert")
                return index switch
                {
                    1 => CertPhotoButton1, 2 => CertPhotoButton2, 3 => CertPhotoButton3,
                    4 => CertPhotoButton4, 5 => CertPhotoButton5, _ => null
                };
            return null;
        }

        private void UpdatePhotoDisplay(string type)
        {
            Border? displayArea = null;
            TextBlock? placeholder = null;
            Image? image = null;
            List<Attachment?>? attachments = null;
            int currentSlot = 1;
            int totalSlots = 0;

            if (type == "IDCard")
            {
                displayArea = IDCardPhotoDisplayArea;
                placeholder = IDCardPlaceholderText;
                image = IDCardPhotoImage;
                attachments = _idCardAttachments;
                currentSlot = _currentIDCardSlot;
                totalSlots = 2;
            }
            else if (type == "Certificate")
            {
                displayArea = CertPhotoDisplayArea;
                placeholder = CertPlaceholderText;
                image = CertPhotoImage;
                attachments = _certAttachments;
                currentSlot = _currentCertSlot;
                totalSlots = 5;
            }

            if (displayArea == null || placeholder == null || image == null) return;

            var attachment = currentSlot >= 1 && currentSlot <= totalSlots && attachments != null
                ? attachments[currentSlot - 1]
                : null;

            if (attachment != null && (!string.IsNullOrEmpty(attachment.ThumbnailPath) || !string.IsNullOrEmpty(attachment.RelativePath)))
            {
                string projectsDateRoot = GetProjectsDateRoot();
                string imagePath = !string.IsNullOrEmpty(attachment.ThumbnailPath)
                    ? (string.IsNullOrEmpty(projectsDateRoot)
                        ? attachment.ThumbnailPath
                        : Path.Combine(projectsDateRoot, attachment.ThumbnailPath))
                    : (string.IsNullOrEmpty(projectsDateRoot)
                        ? attachment.RelativePath
                        : Path.Combine(projectsDateRoot, attachment.RelativePath));

                if (File.Exists(imagePath))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(imagePath);
                        bitmap.EndInit();
                        image.Source = bitmap;
                        image.Visibility = Visibility.Visible;
                        placeholder.Visibility = Visibility.Collapsed;
                        return;
                    }
                    catch (Exception ex) { Log.Debug(ex, "加载人员证件照片失败: {Path}", imagePath); }
                }
            }

            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            placeholder.Visibility = Visibility.Visible;
        }

        private void PhotoSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            var tag = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return;

            var parts = tag.Split('_');
            if (parts.Length != 2) return;

            string type = parts[0];
            string slotStr = parts[1];

            if (type == "IDCard")
            {
                int slot = slotStr == "Front" ? 1 : slotStr == "Back" ? 2 : 0;
                if (slot == 0) return;
                _currentIDCardSlot = slot;
                UpdatePhotoDisplay("IDCard");
                UpdateAllButtonsAppearance();
            }
            else if (type == "Cert")
            {
                int slot = int.Parse(slotStr);
                if (slot < 1 || slot > 5) return;
                SaveCurrentCertSlotText();
                _currentCertSlot = slot;
                LoadCurrentCertSlotText();
                UpdatePhotoDisplay("Certificate");
                UpdateAllButtonsAppearance();
            }
        }

        private Attachment? AddPhoto(string type, int slotIndex, string filePath)
        {
            try
            {
                using var input = File.OpenRead(filePath);
                return AddPhotoFromStream(type, slotIndex, input);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加图片失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        /// <summary>
        /// 添加图片核心实现：粘贴/拖入/文件对话框均汇入此入口，
        /// 统一压缩存档、生成缩略图并按统一标准命名。
        /// </summary>
        private Attachment? AddPhotoFromStream(string type, int slotIndex, Stream stream)
        {
            try
            {
                if (Record == null) return null;

                var dataService = GetDataService();
                if (dataService == null) return null;

                string unitGroupId = dataService.GetPersonnelUnitGroupId(Record?.GroupId ?? "Unknown");
                string cardFolder = $"Card_{Record?.Id}";
                string attachFolder = Path.Combine(
                    GetProjectsDateRoot(),
                    $"Project_{dataService.ProjectId}",
                    $"Personnel_{unitGroupId}",
                    cardFolder
                );
                if (!Directory.Exists(attachFolder))
                    Directory.CreateDirectory(attachFolder);

                string typePrefix = type == "IDCard" ? "idcard" : "cert";
                string guid = Guid.NewGuid().ToString("N");
                string fileName = $"{typePrefix}_{slotIndex}_{guid}.jpg";
                string thumbFileName = $"{typePrefix}_{slotIndex}_{guid}_thumb.jpg";
                string destPath = Path.Combine(attachFolder, fileName);

                string thumbFolder = Path.Combine(attachFolder, "thumb");
                if (!Directory.Exists(thumbFolder))
                    Directory.CreateDirectory(thumbFolder);
                string thumbDestPath = Path.Combine(thumbFolder, thumbFileName);

                using (var input = stream)
                using (var source = SKBitmap.Decode(input))
                {
                    if (source == null) throw new InvalidOperationException("无法解码图片");

                    using (var whiteBg = new SKBitmap(source.Width, source.Height))
                    using (var canvas = new SKCanvas(whiteBg))
                    {
                        canvas.Clear(SKColors.White);
                        canvas.DrawBitmap(source, 0, 0);
                        using var image = SKImage.FromBitmap(whiteBg);
                        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
                        using var fs = File.Create(destPath);
                        data.SaveTo(fs);
                    }

                    float ratio = (float)300 / source.Width;
                    int thumbW = 300;
                    int thumbH = (int)(source.Height * ratio);
                    if (thumbH < 1) thumbH = 1;

                    using (var thumbBg = new SKBitmap(thumbW, thumbH))
                    using (var thumbCanvas = new SKCanvas(thumbBg))
                    using (var resized = source.Resize(new SKImageInfo(thumbW, thumbH), new SKSamplingOptions(SKFilterMode.Linear)))
                    {
                        if (resized != null)
                        {
                            thumbCanvas.Clear(SKColors.White);
                            thumbCanvas.DrawBitmap(resized, 0, 0);
                            using var thumbImage = SKImage.FromBitmap(thumbBg);
                            using var thumbData = thumbImage.Encode(SKEncodedImageFormat.Jpeg, 80);
                            using var thumbFs = File.Create(thumbDestPath);
                            thumbData.SaveTo(thumbFs);
                        }
                    }
                }

                string attachType = type == "IDCard" ? PersonnelConstants.AttachmentTypeIDCard : PersonnelConstants.AttachmentTypeCertificate;
                string relativePath = $"Project_{dataService.ProjectId}/Personnel_{unitGroupId}/{cardFolder}/{fileName}";
                string thumbRelativePath = $"Project_{dataService.ProjectId}/Personnel_{unitGroupId}/{cardFolder}/thumb/{thumbFileName}";

                var newAttachment = new Attachment
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RecordId = Record!.Id,
                    FileName = fileName,
                    RelativePath = relativePath,
                    ThumbnailPath = thumbRelativePath,
                    AttachmentType = attachType,
                    DisplayOrder = slotIndex,
                    FileSize = new FileInfo(destPath).Length,
                    MimeType = "image/jpeg"
                };

                if (type == "Certificate")
                {
                    newAttachment.CertName = Record.CertName;
                    newAttachment.CertNumber = Record.CertDocNumber;
                    newAttachment.CertStart = Record.CertStart;
                    newAttachment.CertEnd = Record.CertEnd;
                }

                var attachments = type == "IDCard" ? _idCardAttachments : _certAttachments;
                if (slotIndex - 1 < attachments.Count)
                {
                    var existing = attachments[slotIndex - 1];
                    if (existing != null)
                    {
                        if (!string.IsNullOrEmpty(existing.RelativePath))
                        {
                            var oldPath = Path.Combine(GetProjectsDateRoot(), existing.RelativePath);
                            if (File.Exists(oldPath)) File.Delete(oldPath);
                        }
                        if (!string.IsNullOrEmpty(existing.ThumbnailPath))
                        {
                            var oldThumb = Path.Combine(GetProjectsDateRoot(), existing.ThumbnailPath);
                            if (File.Exists(oldThumb)) File.Delete(oldThumb);
                        }
                        dataService.DeleteAttachment(existing.Id);
                    }
                }

                dataService.AddAttachment(newAttachment);
                var mw = Application.Current.MainWindow as MainWindow;
                if (mw?.ViewModel?.PersonnelRecords is PersonnelRecordsViewModel cacheVm)
                    cacheVm.InvalidateAttachmentsCache();
                if (type == "IDCard")
                    LoadAttachments(slotIndex, null);
                else
                    LoadAttachments(null, slotIndex);

                // 身份证照片添加后自动识别：正反面槽位校正 + 字段填充（仅填空字段）
                if (type == "IDCard")
                    TriggerIdCardAutoOcr(newAttachment);

                return newAttachment;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加图片失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void DeletePhoto(string type, int slotIndex)
        {
            var attachments = type == "IDCard" ? _idCardAttachments : _certAttachments;
            if (slotIndex < 1 || slotIndex > attachments.Count) return;
            var attachment = attachments[slotIndex - 1];
            if (attachment == null) return;

            string typeLabel = type == "IDCard" ? "身份证" : "证书";
            var result = MessageDialog.Confirm($"确定要删除当前的{typeLabel}照片（第{slotIndex}张）吗？", "确认删除");
            if (result != true) return;

            var dataService = GetDataService();
            if (dataService == null) return;

            if (!string.IsNullOrEmpty(attachment.RelativePath))
            {
                var fullPath = Path.Combine(GetProjectsDateRoot(), attachment.RelativePath);
                if (File.Exists(fullPath)) ImageHelper.DeleteToRecycleBin(fullPath);
            }
            if (!string.IsNullOrEmpty(attachment.ThumbnailPath))
            {
                var thumbPath = Path.Combine(GetProjectsDateRoot(), attachment.ThumbnailPath);
                if (File.Exists(thumbPath)) ImageHelper.DeleteToRecycleBin(thumbPath);
            }
            dataService.DeleteAttachment(attachment.Id);
            var mw = Application.Current.MainWindow as MainWindow;
            if (mw?.ViewModel?.PersonnelRecords is PersonnelRecordsViewModel cacheVm)
                cacheVm.InvalidateAttachmentsCache();
            if (type == "IDCard")
                LoadAttachments(_currentIDCardSlot, null);
            else
                LoadAttachments(null, _currentCertSlot);
        }

        private async void ImageDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files) || files.Length == 0) return;

            string type = sender == IDCardPhotoDisplayArea ? "IDCard" : "Certificate";

            var emptySlots = GetEmptyPhotoSlots(type);

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
                    MessageDialog.Show($"文件\"{Path.GetFileName(firstInvalid)}\"不是支持的图片格式。\n支持的格式：{string.Join(", ", supportedExtensions)}", "提示");
                return;
            }

            for (int i = 0; i < validFiles.Count && i < emptySlots.Count; i++)
                AddPhoto(type, emptySlots[i], validFiles[i]);

            if (validFiles.Count > 0)
                SwitchToSlot(type, emptySlots[0]);
        }

        private void ImageDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
        }

        /// <summary>当前类型的空图片槽位（从 1 开始）</summary>
        private List<int> GetEmptyPhotoSlots(string type)
        {
            var attachmentsList = type == "IDCard" ? _idCardAttachments : _certAttachments;
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
        private void PastePhotosFromClipboard(string type)
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

                    using var memoryStream = new MemoryStream();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(memoryStream);
                    memoryStream.Position = 0;

                    AddPhotoFromStream(type, emptySlots[0], memoryStream);
                    SwitchToSlot(type, emptySlots[0]);
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
                        if (AddPhoto(type, emptySlots[i], validFiles[i]) != null)
                            addedCount++;
                    }

                    if (addedCount > 0)
                        SwitchToSlot(type, emptySlots[0]);

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

        private void AddPhotosFromDialog(string type, int? preferredSlot = null)
        {
            if (Record == null) return;

            int maxSlots = type == "IDCard" ? 2 : 5;
            var attachmentsList = type == "IDCard" ? _idCardAttachments : _certAttachments;
            int currentSlot = preferredSlot ?? (type == "IDCard" ? _currentIDCardSlot : _currentCertSlot);

            var openDialog = new OpenFileDialog
            {
                Title = $"添加{(type == "IDCard" ? "身份证" : "证书")}照片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true,
                InitialDirectory = _lastImportDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (openDialog.ShowDialog() != true) return;

            string? selectedDir = Path.GetDirectoryName(openDialog.FileName);
            if (!string.IsNullOrEmpty(selectedDir))
                _lastImportDirectory = selectedDir;

            string[] selectedFiles = openDialog.FileNames;

            var emptySlots = GetEmptyPhotoSlots(type);

            int addedCount = 0;

            if (selectedFiles.Length == 1)
            {
                int targetSlot = currentSlot;
                bool slotEmpty = targetSlot <= attachmentsList.Count
                    ? attachmentsList[targetSlot - 1] == null
                    : true;

                if (slotEmpty)
                {
                    AddPhoto(type, targetSlot, selectedFiles[0]);
                    addedCount = 1;
                    SwitchToSlot(type, targetSlot);
                }
                else if (emptySlots.Count > 0)
                {
                    AddPhoto(type, emptySlots[0], selectedFiles[0]);
                    addedCount = 1;
                    SwitchToSlot(type, emptySlots[0]);
                }
                else
                {
                    MessageDialog.Show("当前所有槽位已有图片，请先删除后再添加。", "提示");
                }
            }
            else
            {
                if (emptySlots.Count == 0)
                {
                    MessageDialog.Show("当前所有槽位已有图片，请先删除后再添加。", "提示");
                    return;
                }

                for (int i = 0; i < selectedFiles.Length && i < emptySlots.Count; i++)
                {
                    AddPhoto(type, emptySlots[i], selectedFiles[i]);
                    addedCount++;
                }

                if (addedCount > 0)
                    SwitchToSlot(type, emptySlots[0]);

                if (selectedFiles.Length > addedCount)
                    MessageDialog.Show($"已添加 {addedCount} 张图片，剩余 {selectedFiles.Length - addedCount} 张因槽位不足被忽略。", "提示");
            }
        }

        private void CopyPhotoToClipboard(Attachment attachment)
        {
            if (attachment == null || string.IsNullOrEmpty(attachment.RelativePath)) return;
            string projectsDateRoot = GetProjectsDateRoot();
            var fullPath = Path.Combine(projectsDateRoot, attachment.RelativePath);
            if (!File.Exists(fullPath)) return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(fullPath);
                bitmap.EndInit();
                System.Windows.Clipboard.SetImage(bitmap);
            }
            catch (Exception ex) { Log.Warning(ex, "复制照片到剪贴板失败: {Path}", fullPath); }
        }

        private void ExportPhotoToDesktop(Attachment attachment)
        {
            if (attachment == null || string.IsNullOrEmpty(attachment.RelativePath)) return;

            string projectsDateRoot = GetProjectsDateRoot();
            string sourcePath = Path.Combine(projectsDateRoot, attachment.RelativePath);
            if (!File.Exists(sourcePath)) return;

            string fileName = $"人员照片_{attachment.AttachmentType}_{attachment.DisplayOrder}_{attachment.Id[..8]}.jpg";
            string initialDir = string.IsNullOrEmpty(_lastImportDirectory) || !Directory.Exists(_lastImportDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                : _lastImportDirectory;

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
                    _lastImportDirectory = Path.GetDirectoryName(saveDialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageDialog.Show($"导出失败：{ex.Message}", "错误");
                }
            }
        }

        private void RearrangePhotos(string type)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel?.GetProjectDataService();
            if (dataService == null || Record == null) return;

            var attachments = type == "IDCard" ? _idCardAttachments : _certAttachments;
            var nonNullAttachments = attachments.Where(a => a != null).Select(a => a!).OrderBy(a => a.DisplayOrder).ToList();

            if (nonNullAttachments.Count == 0)
            {
                var target = type == "IDCard" ? (UIElement)IDCardPhotoDisplayArea : CertPhotoDisplayArea;
                ToastService.Show("没有需要整理的图片", target);
                return;
            }

            string confirmMessage = type == "IDCard"
                ? "将当前身份证照片重新排列到槽位 1、2，移除空缺。是否继续？"
                : "将当前证书照片重新排列到槽位 1~5，移除空缺。是否继续？";
            if (MessageDialog.Confirm(confirmMessage, "确认整理") != true) return;

            var renameOperations = new List<(string oldFullPath, string newFullPath)>();
            var updateAttachments = new List<Attachment>();

            try
            {
                for (int i = 0; i < nonNullAttachments.Count; i++)
                {
                    var att = nonNullAttachments[i];
                    int newOrder = i + 1;
                    if (att.DisplayOrder == newOrder) continue;

                    string oldRelativePath = att.RelativePath;
                    string oldThumbPath = att.ThumbnailPath ?? "";
                    string directory = Path.GetDirectoryName(oldRelativePath) ?? "";
                    string oldFileName = Path.GetFileName(oldRelativePath);

                    var guidMatch = Regex.Match(oldFileName, @"[0-9a-f]{32}", RegexOptions.IgnoreCase);
                    if (!guidMatch.Success) throw new Exception("无法解析原图片文件名中的 GUID");

                    string guid = guidMatch.Value;
                    string typePrefix = type == "IDCard" ? "idcard" : "cert";
                    string newFileName = $"{typePrefix}_{newOrder}_{guid}.jpg";
                    string newThumbFileName = $"{typePrefix}_{newOrder}_{guid}_thumb.jpg";
                    string newRelativePath = Path.Combine(directory, newFileName);
                    string newThumbRelativePath = Path.Combine(directory, newThumbFileName);

                    string projectsDateRoot = GetProjectsDateRoot();
                    string oldFullPath = Path.Combine(projectsDateRoot, oldRelativePath);
                    string newFullPath = Path.Combine(projectsDateRoot, newRelativePath);
                    string oldThumbFullPath = Path.Combine(projectsDateRoot, oldThumbPath);
                    string newThumbFullPath = Path.Combine(projectsDateRoot, newThumbRelativePath);

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
                    else throw new Exception($"文件不存在: {oldPath}");
                }

                foreach (var att in updateAttachments)
                {
                    if (!dataService.UpdateAttachment(att))
                        throw new Exception($"更新数据库失败: {att.Id}");
                }

                LoadAttachments();
                var toastTarget = type == "IDCard" ? (UIElement)IDCardPhotoDisplayArea : CertPhotoDisplayArea;
                ToastService.Show("整理完成", toastTarget);
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"整理失败：{ex.Message}\n请检查文件是否被其他程序占用。", "错误");
            }
        }

        private void SwitchToSlot(string type, int slot)
        {
            if (type == "IDCard")
            {
                _currentIDCardSlot = slot;
                UpdatePhotoDisplay("IDCard");
            }
            else
            {
                SaveCurrentCertSlotText();
                _currentCertSlot = slot;
                LoadCurrentCertSlotText();
                UpdatePhotoDisplay("Certificate");
            }
            UpdateAllButtonsAppearance();
        }

        private void PhotoImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (!(sender is Image clickedImage)) return;
                string type = clickedImage == IDCardPhotoImage ? "IDCard" : "Certificate";
                int currentSlot = type == "IDCard" ? _currentIDCardSlot : _currentCertSlot;
                var attachments = type == "IDCard" ? _idCardAttachments : _certAttachments;
                var attachment = currentSlot >= 1 && currentSlot <= attachments.Count ? attachments[currentSlot - 1] : null;
                if (attachment == null) return;

                string projectsDateRoot = GetProjectsDateRoot();
                string fullPath = Path.Combine(projectsDateRoot, attachment.RelativePath);
                if (!File.Exists(fullPath)) return;

                e.Handled = true;

                // 按「图片查看器设置」决定使用内置查看器还是系统默认查看器
                var settings = _settingsService ?? AppServiceProvider.SettingsService;
                string viewerType = settings?.AppSettings.ImageViewerType ?? ImageViewerType.Internal;
                string startMode = settings?.AppSettings.ImageViewerStartMode ?? ImageViewerStartMode.FullScreen;
                Log.Information($"[ImageViewer] Personnel PhotoImage: viewerType={viewerType}, startMode={startMode}");

                if (viewerType == ImageViewerType.Internal)
                {
                    // 构建当前人员卡片的照片列表（身份证 → 证书，按槽位顺序），内置查看器内可翻页
                    var images = new List<string>();
                    foreach (var att in _idCardAttachments)
                        if (att != null && !string.IsNullOrEmpty(att.RelativePath))
                            images.Add(Path.Combine(projectsDateRoot, att.RelativePath));
                    foreach (var att in _certAttachments)
                        if (att != null && !string.IsNullOrEmpty(att.RelativePath))
                            images.Add(Path.Combine(projectsDateRoot, att.RelativePath));

                    if (images.Count == 0) return;

                    int imageIndex = images.FindIndex(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
                    if (imageIndex < 0) imageIndex = 0;

                    bool startFullScreen = startMode == ImageViewerStartMode.FullScreen;
                    Log.Information($"[ImageViewer] Personnel LaunchAsProcess: imageCount={images.Count}, imageIndex={imageIndex}, startFullScreen={startFullScreen}");
                    ImageViewerLauncher.LaunchAsProcess(images, imageIndex, startFullScreen);
                }
                else
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (Exception ex) { Log.Warning(ex, "打开照片文件失败: {Path}", fullPath); }
                }
            }
        }

        private void PhotoDisplayArea_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            string type = border == IDCardPhotoDisplayArea ? "IDCard" : "Certificate";
            int currentSlot = type == "IDCard" ? _currentIDCardSlot : _currentCertSlot;

            var attachments = type == "IDCard" ? _idCardAttachments : _certAttachments;
            var attachment = (currentSlot >= 1 && currentSlot <= attachments.Count) ? attachments[currentSlot - 1] : null;

            _activeCardMenu?.Close();
            _activeCardMenu = null;

            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            var menu = new DropdownMenu();

            if (attachment == null)
            {
                int capturedSlot = currentSlot;
                menu.AddItem("添加图片", () => AddPhotosFromDialog(type, capturedSlot));
                menu.AddItem("粘贴图片", () => PastePhotosFromClipboard(type));
            }
            else
            {
                menu.AddItem("复制图片", () => CopyPhotoToClipboard(attachment));
                menu.AddItem("导出图片", () => ExportPhotoToDesktop(attachment));
                string capturedType = type;
                int capturedIndex = currentSlot;
                menu.AddItem("删除图片", () => DeletePhoto(capturedType, capturedIndex));
                menu.AddSeparator();
                if (type == "IDCard")
                    menu.AddItem("文字识别填充", async () => await PerformOcrFillForIDCard());
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
            if (area == IDCardPhotoDisplayArea)
            {
                if (direction == "Prev")
                    AnimateButtonFadeIn(IDCardPrevButton);
                else
                    AnimateButtonFadeIn(IDCardNextButton);
            }
            else if (area == CertPhotoDisplayArea)
            {
                if (direction == "Prev")
                    AnimateButtonFadeIn(CertPrevButton);
                else
                    AnimateButtonFadeIn(CertNextButton);
            }
        }

        private void HideNavigationButtons()
        {
            IDCardPrevButton.Visibility = Visibility.Collapsed;
            IDCardNextButton.Visibility = Visibility.Collapsed;
            CertPrevButton.Visibility = Visibility.Collapsed;
            CertNextButton.Visibility = Visibility.Collapsed;
        }

        private void AnimateButtonFadeIn(Button button)
        {
            if (button.Visibility != Visibility.Visible || button.Opacity < 0.99)
            {
                button.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                button.BeginAnimation(OpacityProperty, fadeIn);
            }
        }

        private void PhotoPrevButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is string type)
            {
                if (type == "IDCard")
                {
                    _currentIDCardSlot--;
                    if (_currentIDCardSlot < 1) _currentIDCardSlot = 2;
                    UpdatePhotoDisplay("IDCard");
                }
                else if (type == "Certificate")
                {
                    SaveCurrentCertSlotText();
                    _currentCertSlot--;
                    if (_currentCertSlot < 1) _currentCertSlot = 5;
                    LoadCurrentCertSlotText();
                    UpdatePhotoDisplay("Certificate");
                }
                UpdateAllButtonsAppearance();
            }
            e.Handled = true;
        }

        private void PhotoNextButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is string type)
            {
                if (type == "IDCard")
                {
                    _currentIDCardSlot++;
                    if (_currentIDCardSlot > 2) _currentIDCardSlot = 1;
                    UpdatePhotoDisplay("IDCard");
                }
                else if (type == "Certificate")
                {
                    SaveCurrentCertSlotText();
                    _currentCertSlot++;
                    if (_currentCertSlot > 5) _currentCertSlot = 1;
                    LoadCurrentCertSlotText();
                    UpdatePhotoDisplay("Certificate");
                }
                UpdateAllButtonsAppearance();
            }
            e.Handled = true;
        }

        #endregion
    }
}
