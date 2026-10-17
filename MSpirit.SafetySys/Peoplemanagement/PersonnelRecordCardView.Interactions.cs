// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Views;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelRecordCardView : UserControl
    {
        #region OCR 服务

        internal static IOcrService GetOcrService()
        {
            if (_ocrService != null) return _ocrService;

            lock (_ocrServiceLock)
            {
                if (_ocrService != null) return _ocrService;

                var paddle = new PaddleOcrService();
                try
                {
                    Task.Run(async () => await paddle.EnsureInitializedAsync()).GetAwaiter().GetResult();
                    if (paddle.IsAvailable)
                    {
                        Log.Information("OCR服务选择: PaddleOCR V5");
                        _ocrService = paddle;
                        return _ocrService;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "PaddleOCR 初始化失败，将降级到 Windows OCR");
                }

                Log.Information("OCR服务选择: Windows.Media.Ocr（降级）");
                paddle.Dispose();
                _ocrService = new WindowsOcrService();
                return _ocrService;
            }
        }

        private async Task AutoOcrFillAsync(Attachment attachment)
        {
            if (Record == null || attachment == null) return;

            string capturedRecordId = Record.Id;

            try
            {
                string projectsDateRoot = GetProjectsDateRoot();
                string fullPath = Path.Combine(projectsDateRoot, attachment.RelativePath);
                if (!File.Exists(fullPath)) return;

                var ocrService = GetOcrService();
                var blocks = await ocrService.RecognizeAsync(fullPath);
                if (blocks.Count == 0) return;

                var ocrResult = Helpers.OcrService.ParseFields(blocks);
                if (!ocrResult.HasAnyField) return;

                bool isIdCard = attachment.AttachmentType == PersonnelConstants.AttachmentTypeIDCard;
                if (!isIdCard) return;

                var dataService = GetDataService();
                if (dataService == null) return;

                var dbRecord = dataService.GetPersonnelRecord(capturedRecordId);
                if (dbRecord == null) return;

                bool changed = false;

                if (!string.IsNullOrEmpty(ocrResult.Name) && string.IsNullOrEmpty(dbRecord.Title))
                { dbRecord.Title = ocrResult.Name; changed = true; }
                if (!string.IsNullOrEmpty(ocrResult.Gender) && string.IsNullOrEmpty(dbRecord.Gender))
                { dbRecord.Gender = ocrResult.Gender; changed = true; }
                if (!string.IsNullOrEmpty(ocrResult.Age) && string.IsNullOrEmpty(dbRecord.Age))
                { dbRecord.Age = ocrResult.Age; changed = true; }
                if (!string.IsNullOrEmpty(ocrResult.NativePlace) && string.IsNullOrEmpty(dbRecord.NativePlace))
                { dbRecord.NativePlace = ocrResult.NativePlace; changed = true; }
                if (!string.IsNullOrEmpty(ocrResult.IDNumber) && string.IsNullOrEmpty(dbRecord.IDCardDocNumber))
                { dbRecord.IDCardDocNumber = ocrResult.IDNumber; changed = true; }
                if (!string.IsNullOrEmpty(ocrResult.ValidFrom) && string.IsNullOrEmpty(dbRecord.IDCardStart))
                { dbRecord.IDCardStart = ocrResult.ValidFrom; changed = true; }
                if (!string.IsNullOrEmpty(ocrResult.ValidTo) && string.IsNullOrEmpty(dbRecord.IDCardEnd))
                { dbRecord.IDCardEnd = ocrResult.ValidTo; changed = true; }
                if (!string.IsNullOrEmpty(ocrResult.BirthDate) && string.IsNullOrEmpty(dbRecord.BirthDate))
                { dbRecord.BirthDate = ocrResult.BirthDate; changed = true; }

                if (changed)
                {
                    dataService.UpdatePersonnelRecord(dbRecord);
                    Log.Information("AutoOcrFill: 已自动填充身份证字段 {AttachmentId}", attachment.Id);

                    var mainWindow = Application.Current.MainWindow as MainWindow;
                    mainWindow?.ViewModel?.PersonnelRecords?.RefreshSingleRecord(capturedRecordId);

                    if (IsLoaded && Record != null && Record.Id == capturedRecordId)
                    {
                        Record.Title = dbRecord.Title;
                        Record.Gender = dbRecord.Gender;
                        Record.Age = dbRecord.Age;
                        Record.NativePlace = dbRecord.NativePlace;
                        Record.BirthDate = dbRecord.BirthDate;
                        Record.IDCardDocNumber = dbRecord.IDCardDocNumber;
                        Record.IDCardStart = dbRecord.IDCardStart;
                        Record.IDCardEnd = dbRecord.IDCardEnd;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AutoOcrFill: 自动OCR填充失败，不影响已有数据");
            }
        }

        /// <summary>
        /// 添加身份证照片后自动执行：正反面槽位校正 + OCR 字段填充（仅填空字段）
        /// </summary>
        private async void TriggerIdCardAutoOcr(Attachment attachment)
        {
            try
            {
                // 槽位校正：先识别正反面并整理槽位顺序
                if (_idCardAttachments.Count == 2 &&
                    _idCardAttachments[0] != null && _idCardAttachments[1] != null)
                    await CorrectIdCardSlotsAsync(_idCardAttachments[0]!, _idCardAttachments[1]!);
                else
                    await CorrectSingleIdCardSlotAsync(attachment);

                // 自动填充身份证字段
                await AutoOcrFillAsync(attachment);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TriggerIdCardAutoOcr: 自动识别流程异常");
            }
        }

        private async Task CorrectIdCardSlotsAsync(Attachment attA, Attachment attB)
        {
            try
            {
                if (attA == null || attB == null) return;

                string projectsDateRoot = GetProjectsDateRoot();
                string fullPathA = Path.Combine(projectsDateRoot, attA.RelativePath);
                string fullPathB = Path.Combine(projectsDateRoot, attB.RelativePath);

                if (!File.Exists(fullPathA) || !File.Exists(fullPathB)) return;

                var ocrService = GetOcrService();
                var blocksA = await ocrService.RecognizeAsync(fullPathA);
                var blocksB = await ocrService.RecognizeAsync(fullPathB);

                string? sideA = Helpers.OcrService.DetectIdCardSide(blocksA);
                string? sideB = Helpers.OcrService.DetectIdCardSide(blocksB);

                if (sideA == null || sideB == null) return;

                if (attA.DisplayOrder == 1 && attB.DisplayOrder == 2
                    && sideA == "Back" && sideB == "Front")
                {
                    var dataService = GetDataService();
                    if (dataService == null) return;

                    attA.DisplayOrder = 2;
                    attB.DisplayOrder = 1;

                    dataService.UpdateAttachment(attA);
                    dataService.UpdateAttachment(attB);

                    (attA.ThumbnailPath, attB.ThumbnailPath) = (attB.ThumbnailPath, attA.ThumbnailPath);

                    dataService.UpdateAttachment(attA);
                    dataService.UpdateAttachment(attB);

                    var mw = Application.Current.MainWindow as MainWindow;
                    mw?.ViewModel?.PersonnelRecords?.InvalidateAttachmentsCache();

                    LoadAttachments(_currentIDCardSlot, null);

                    Log.Information("CorrectIdCardSlots: 交换正面/背面槽位 {A}-&gt;{B}", attA.Id, attB.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CorrectIdCardSlots: 校正槽位失败");
            }
        }

        private async Task CorrectSingleIdCardSlotAsync(Attachment attachment)
        {
            try
            {
                if (attachment == null) return;
                string projectsDateRoot = GetProjectsDateRoot();
                string fullPath = Path.Combine(projectsDateRoot, attachment.RelativePath);
                if (!File.Exists(fullPath)) return;

                var ocrService = GetOcrService();
                var blocks = await ocrService.RecognizeAsync(fullPath);
                string? side = Helpers.OcrService.DetectIdCardSide(blocks);
                if (side == null) return;

                int correctSlot = side == "Front" ? 1 : 2;
                if (attachment.DisplayOrder == correctSlot) return;

                var dataService = GetDataService();
                if (dataService == null) return;

                var targetAttachment = _idCardAttachments[correctSlot - 1];
                if (targetAttachment == null)
                {
                    int oldSlot = attachment.DisplayOrder;
                    attachment.DisplayOrder = correctSlot;
                    dataService.UpdateAttachment(attachment);

                    _idCardAttachments[correctSlot - 1] = attachment;
                    if (oldSlot >= 1 && oldSlot <= _idCardAttachments.Count)
                        _idCardAttachments[oldSlot - 1] = null;

                    Log.Information("CorrectSingleIdCardSlot: 将 {Side} 从槽位 {Old} 移到槽位 {New}", side, oldSlot, correctSlot);
                }
                else
                {
                    int oldSlot = attachment.DisplayOrder;
                    attachment.DisplayOrder = correctSlot;
                    targetAttachment.DisplayOrder = oldSlot;

                    dataService.UpdateAttachment(attachment);
                    dataService.UpdateAttachment(targetAttachment);

                    (attachment.ThumbnailPath, targetAttachment.ThumbnailPath)
                        = (targetAttachment.ThumbnailPath, attachment.ThumbnailPath);
                    dataService.UpdateAttachment(attachment);
                    dataService.UpdateAttachment(targetAttachment);

                    Log.Information("CorrectSingleIdCardSlot: 交换槽位 {Slot1} <-> {Slot2}（{Side}）", oldSlot, correctSlot, side);
                }

                var mw = Application.Current.MainWindow as MainWindow;
                mw?.ViewModel?.PersonnelRecords?.InvalidateAttachmentsCache();

                LoadAttachments(_currentIDCardSlot, null);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CorrectSingleIdCardSlot: 校正槽位失败");
            }
        }

        private async Task PerformOcrFillForIDCard()
        {
            if (Record == null) return;

            var validSlots = new List<(int Slot, Attachment Att)>();
            for (int i = 0; i < _idCardAttachments.Count; i++)
            {
                if (_idCardAttachments[i] != null)
                    validSlots.Add((i + 1, _idCardAttachments[i]!));
            }

            if (validSlots.Count == 0)
            {
                MessageDialog.Show("没有可识别的身份证图片", "提示");
                return;
            }

            try
            {
                var ocrService = GetOcrService();
                ToastService.Show($"正在使用 {ocrService.Name} 识别身份证图片中的文字...", IDCardPhotoDisplayArea);

                string projectsDateRoot = GetProjectsDateRoot();

                var mergedResult = new Helpers.OcrResult();
                foreach (var slot in validSlots)
                {
                    string fullPath = Path.Combine(projectsDateRoot, slot.Att.RelativePath);
                    var blocks = await ocrService.RecognizeAsync(fullPath);
                    if (blocks.Count == 0) continue;
                    var result = Helpers.OcrService.ParseFields(blocks);
                    MergeOcrResult(mergedResult, result);
                }

                if (!mergedResult.HasAnyField)
                {
                    MessageDialog.Show("未能从图片中提取有效信息，请重试或手动填写。", "提示");
                    return;
                }

                string slotInfo = validSlots.Count == 2 ? "正面+背面" : $"槽位{validSlots[0].Slot}";
                string confirmMsg = $"已识别到以下信息（{slotInfo}）：\n{mergedResult.ToConfirmText(true)}\n\n是否填入记录卡对应信息？";
                if (MessageDialog.Confirm(confirmMsg, "确认填入") != true)
                    return;

                bool changed = false;
                if (!string.IsNullOrEmpty(mergedResult.Name) && string.IsNullOrEmpty(Record.Title))
                { Record.Title = mergedResult.Name; changed = true; }
                if (!string.IsNullOrEmpty(mergedResult.Gender) && string.IsNullOrEmpty(Record.Gender))
                { Record.Gender = mergedResult.Gender; changed = true; }
                if (!string.IsNullOrEmpty(mergedResult.Age) && string.IsNullOrEmpty(Record.Age))
                { Record.Age = mergedResult.Age; changed = true; }
                if (!string.IsNullOrEmpty(mergedResult.NativePlace) && string.IsNullOrEmpty(Record.NativePlace))
                { Record.NativePlace = mergedResult.NativePlace; changed = true; }
                if (!string.IsNullOrEmpty(mergedResult.IDNumber) && string.IsNullOrEmpty(Record.IDCardDocNumber))
                { Record.IDCardDocNumber = mergedResult.IDNumber; changed = true; }
                if (!string.IsNullOrEmpty(mergedResult.ValidFrom) && string.IsNullOrEmpty(Record.IDCardStart))
                { Record.IDCardStart = mergedResult.ValidFrom; changed = true; }
                if (!string.IsNullOrEmpty(mergedResult.ValidTo) && string.IsNullOrEmpty(Record.IDCardEnd))
                { Record.IDCardEnd = mergedResult.ValidTo; changed = true; }
                if (!string.IsNullOrEmpty(mergedResult.BirthDate) && string.IsNullOrEmpty(Record.BirthDate))
                { Record.BirthDate = mergedResult.BirthDate; changed = true; }

                if (changed) SaveRecord();
                ToastService.Show("OCR 填充完成", IDCardPhotoDisplayArea);
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"OCR 识别失败：{ex.Message}", "错误");
            }
        }

        private static void MergeOcrResult(Helpers.OcrResult dest, Helpers.OcrResult src)
        {
            if (!string.IsNullOrEmpty(src.Name) && string.IsNullOrEmpty(dest.Name)) dest.Name = src.Name;
            if (!string.IsNullOrEmpty(src.Gender) && string.IsNullOrEmpty(dest.Gender)) dest.Gender = src.Gender;
            if (!string.IsNullOrEmpty(src.Age) && string.IsNullOrEmpty(dest.Age)) dest.Age = src.Age;
            if (!string.IsNullOrEmpty(src.NativePlace) && string.IsNullOrEmpty(dest.NativePlace)) dest.NativePlace = src.NativePlace;
            if (!string.IsNullOrEmpty(src.IDNumber) && string.IsNullOrEmpty(dest.IDNumber)) dest.IDNumber = src.IDNumber;
            if (!string.IsNullOrEmpty(src.ValidFrom) && string.IsNullOrEmpty(dest.ValidFrom)) dest.ValidFrom = src.ValidFrom;
            if (!string.IsNullOrEmpty(src.ValidTo) && string.IsNullOrEmpty(dest.ValidTo)) dest.ValidTo = src.ValidTo;
            if (!string.IsNullOrEmpty(src.BirthDate) && string.IsNullOrEmpty(dest.BirthDate)) dest.BirthDate = src.BirthDate;
        }

        #endregion

        #region 证书槽位文本管理

        private void WriteCertSlotsToRecord()
        {
            if (Record == null) return;
            Record.Cert1Name = _certSlotName[0] ?? "";
            Record.Cert1DocNumber = _certSlotDocNumber[0] ?? "";
            Record.Cert1Start = _certSlotStart[0] ?? "";
            Record.Cert1End = _certSlotEnd[0] ?? "";

            Record.Cert2Name = _certSlotName[1] ?? "";
            Record.Cert2DocNumber = _certSlotDocNumber[1] ?? "";
            Record.Cert2Start = _certSlotStart[1] ?? "";
            Record.Cert2End = _certSlotEnd[1] ?? "";

            Record.Cert3Name = _certSlotName[2] ?? "";
            Record.Cert3DocNumber = _certSlotDocNumber[2] ?? "";
            Record.Cert3Start = _certSlotStart[2] ?? "";
            Record.Cert3End = _certSlotEnd[2] ?? "";

            Record.Cert4Name = _certSlotName[3] ?? "";
            Record.Cert4DocNumber = _certSlotDocNumber[3] ?? "";
            Record.Cert4Start = _certSlotStart[3] ?? "";
            Record.Cert4End = _certSlotEnd[3] ?? "";

            Record.Cert5Name = _certSlotName[4] ?? "";
            Record.Cert5DocNumber = _certSlotDocNumber[4] ?? "";
            Record.Cert5Start = _certSlotStart[4] ?? "";
            Record.Cert5End = _certSlotEnd[4] ?? "";
        }

        private void SaveCurrentCertSlotText()
        {
            int idx = _currentCertSlot - 1;
            if (idx < 0 || idx >= 5) return;
            _certSlotName[idx] = CertNameTextBox.Text ?? "";
            _certSlotDocNumber[idx] = CertDocNumberTextBox.Text ?? "";
            _certSlotStart[idx] = CertStartTextBox.Text ?? "";
            _certSlotEnd[idx] = CertEndTextBox.Text ?? "";
            Log.Verbose("CertSlot saved [{Slot}]: Name={Name}, Number={Num}", _currentCertSlot, _certSlotName[idx], _certSlotDocNumber[idx]);
        }

        private void LoadCurrentCertSlotText()
        {
            if (Record == null) return;

            _isLoadingCertSlot = true;
            _suppressAutoSave = true;
            try
            {
                int idx = _currentCertSlot - 1;
                if (idx < 0 || idx >= 5) return;
                CertNameTextBox.Text = _certSlotName[idx] ?? "";
                CertDocNumberTextBox.Text = _certSlotDocNumber[idx] ?? "";
                CertStartTextBox.Text = _certSlotStart[idx] ?? "";
                CertEndTextBox.Text = _certSlotEnd[idx] ?? "";
            }
            finally
            {
                _suppressAutoSave = false;
                _isLoadingCertSlot = false;
            }
        }

        private void LoadDefaultCertSlotText()
        {
            if (Record == null) return;

            _certSlotName[0] = Record.Cert1Name ?? "";
            _certSlotDocNumber[0] = Record.Cert1DocNumber ?? "";
            _certSlotStart[0] = Record.Cert1Start ?? "";
            _certSlotEnd[0] = Record.Cert1End ?? "";

            _certSlotName[1] = Record.Cert2Name ?? "";
            _certSlotDocNumber[1] = Record.Cert2DocNumber ?? "";
            _certSlotStart[1] = Record.Cert2Start ?? "";
            _certSlotEnd[1] = Record.Cert2End ?? "";

            _certSlotName[2] = Record.Cert3Name ?? "";
            _certSlotDocNumber[2] = Record.Cert3DocNumber ?? "";
            _certSlotStart[2] = Record.Cert3Start ?? "";
            _certSlotEnd[2] = Record.Cert3End ?? "";

            _certSlotName[3] = Record.Cert4Name ?? "";
            _certSlotDocNumber[3] = Record.Cert4DocNumber ?? "";
            _certSlotStart[3] = Record.Cert4Start ?? "";
            _certSlotEnd[3] = Record.Cert4End ?? "";

            _certSlotName[4] = Record.Cert5Name ?? "";
            _certSlotDocNumber[4] = Record.Cert5DocNumber ?? "";
            _certSlotStart[4] = Record.Cert5Start ?? "";
            _certSlotEnd[4] = Record.Cert5End ?? "";

            _currentCertSlot = FindFirstNonEmptyCertSlot();
            LoadCurrentCertSlotText();
        }

        private void CertTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isLoadingCertSlot) return;
            SaveCurrentCertSlotText();
            if (!_suppressAutoSave)
            {
                _autoSaveTimer?.Stop();
                _autoSaveTimer?.Start();
            }

            if (ReferenceEquals(sender, CertStartTextBox) || ReferenceEquals(sender, CertEndTextBox))
                ScheduleDateFormat();
        }

        private int FindFirstNonEmptyCertSlot()
        {
            for (int i = 0; i < 5; i++)
            {
                if (!string.IsNullOrEmpty(_certSlotName[i]) ||
                    !string.IsNullOrEmpty(_certSlotDocNumber[i]) ||
                    (_certAttachments.Count > i && _certAttachments[i] != null))
                    return i + 1;
            }
            return 1;
        }

        #endregion

        #region 右键菜单

        private void CardBorder_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hitElement = e.OriginalSource as DependencyObject;
            if (IsOverInteractiveControl(hitElement))
                return;

            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            ShowCardContextMenu(wpfPoint);
            e.Handled = true;
        }

        private bool IsOverInteractiveControl(DependencyObject? hitElement)
        {
            if (hitElement == null) return false;
            while (hitElement != null)
            {
                if (hitElement is Button || hitElement is TextBox || hitElement is CheckBox ||
                    hitElement is ToggleButton ||
                    (hitElement is Border border && (border.Name == "IDCardPhotoDisplayArea" || border.Name == "CertPhotoDisplayArea")) ||
                    hitElement is Image || hitElement is Popup)
                    return true;
                hitElement = VisualTreeHelper.GetParent(hitElement);
            }
            return false;
        }

        private void ShowCardContextMenu(Point screenPoint)
        {
            _activeCardMenu?.Close();
            _activeCardMenu = null;

            var menu = new DropdownMenu();

            menu.AddItem("生成图片", () => GenerateImage());

            menu.AddSeparator();

            menu.AddItem("清空卡片", () =>
            {
                if (MessageDialog.Confirm("确定要清空当前记录卡的所有内容吗？", "确认清空") == true)
                    ClearContent();
            });

            menu.AddItem("删除卡片", () =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                mainWindow?.ViewModel?.PersonnelRecords?.DeleteRecord(Record);
            });

            menu.Closed += () => _activeCardMenu = null;
            _activeCardMenu = menu;
            menu.ShowAt(screenPoint);
        }

        private void GenerateImage()
        {
            if (Record == null) return;

            string projectName = "未命名项目";
            if (Application.Current.MainWindow is MainWindow mainWindow && mainWindow.ViewModel != null)
            {
                projectName = mainWindow.ViewModel.CurrentProjectName;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
                projectName = projectName.Replace(c.ToString(), "_");
            projectName = projectName.Replace(" ", "_");

            string fileName = $"{projectName}_Personnel_{DateTime.Now:yyyy-MM-dd}_{Record.Id[..8]}.png";

            var originalEffect = CardBorder.Effect;
            CardBorder.Effect = null;
            CardBorder.UpdateLayout();

            ScreenshotHelper.CaptureToFile(
                CardBorder, fileName, ref _lastImportDirectory,
                padding: 5, autoCrop: true,
                onBeforeRender: null,
                onAfterRender: () => CardBorder.Effect = originalEffect);
        }

        private void TextBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;
            if (textBox.Name == "GenderTextBox") return; // 性别为只读下拉，不显示右键菜单

            _currentRightClickTextBox = textBox;
            var mouseScreenPoint = System.Windows.Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(new Point(mouseScreenPoint.X, mouseScreenPoint.Y));

            ShowTextBoxContextMenu(textBox, wpfPoint);
            e.Handled = true;
        }

        private void TextBox_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as TextBox)?.Name == "GenderTextBox") return;
            e.Handled = true;
        }

        /// <summary>
        /// 性别只读下拉：左键点击弹出 男/女/清空 菜单，禁止自由输入
        /// </summary>
        private void GenderTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Record == null) return;
            e.Handled = true;

            // 已打开则关闭（切换）
            if (_activeTextBoxMenu != null)
            {
                _activeTextBoxMenu.Close();
                _activeTextBoxMenu = null;
                return;
            }

            var menu = new DropdownMenu();
            menu.AddItem("男", () => { Record.Gender = "男"; });
            menu.AddItem("女", () => { Record.Gender = "女"; });
            menu.AddSeparator();
            menu.AddItem("清空", () => { Record.Gender = ""; });

            menu.Closed += () => _activeTextBoxMenu = null;
            _activeTextBoxMenu = menu;
            menu.ShowBelow((FrameworkElement)sender);
        }

        private void ShowTextBoxContextMenu(TextBox textBox, Point screenPoint)
        {
            _activeTextBoxMenu?.Close();
            _activeTextBoxMenu = null;

            var menu = new DropdownMenu();
            menu.AddItem("粘贴 (Ctrl+V)", () => PasteStandard(textBox));
            menu.AddItem("复制 (Ctrl+C)", () => CopyFullText(textBox));
            menu.AddItem("剪切 (Ctrl+X)", () => CutFullText(textBox));
            menu.AddItem("清空 (Ctrl+Q)", () => ClearFullText(textBox));

            menu.Closed += () => _activeTextBoxMenu = null;
            _activeTextBoxMenu = menu;
            menu.ShowAt(screenPoint);
        }

        private void CloseTextBoxPopup()
        {
            if (_activeTextBoxMenu != null)
            {
                _activeTextBoxMenu.Close();
                _activeTextBoxMenu = null;
            }
        }

        private void PasteStandard(TextBox textBox)
        {
            textBox.Paste();
        }

        private void CopyFullText(TextBox textBox)
        {
            if (!string.IsNullOrEmpty(textBox.Text))
                Clipboard.SetText(textBox.Text);
        }

        private void CutFullText(TextBox textBox)
        {
            if (!string.IsNullOrEmpty(textBox.Text))
            {
                Clipboard.SetText(textBox.Text);
                textBox.Text = "";
            }
        }

        private void ClearFullText(TextBox textBox)
        {
            textBox.Text = "";
        }

        #endregion

        #region 键盘与全局事件

        private void RootControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyboardDevice.Modifiers != ModifierKeys.Control) return;
            if (!(e.OriginalSource is TextBox textBox)) return;

            if (e.Key == Key.V) { textBox.Paste(); e.Handled = true; }
            else if (e.Key == Key.C) { CopyFullText(textBox); e.Handled = true; }
            else if (e.Key == Key.X) { CutFullText(textBox); e.Handled = true; }
            else if (e.Key == Key.Q) { ClearFullText(textBox); e.Handled = true; }
        }

        private void RootControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_activeCardMenu != null)
            {
                var hitElement = e.OriginalSource as DependencyObject;
                bool isInteractive = IsInteractiveElement(hitElement);
                if (!isInteractive)
                {
                    _activeCardMenu.Close();
                    _activeCardMenu = null;
                }
            }
        }

        private bool IsInteractiveElement(DependencyObject source)
        {
            if (source == null) return false;
            var current = source;
            while (current != null)
            {
                if (current is Button || current is TextBox || current is Image || current is Border)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        #endregion
    }
}
