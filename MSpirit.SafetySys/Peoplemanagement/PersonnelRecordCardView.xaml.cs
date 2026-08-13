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
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Serilog;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelRecordCardView : UserControl
    {
        #region 字段

        private ScrollViewer? _mainScrollViewer;
        private Window? _mainWindow;
        private static string? _lastImportDirectory;

        private List<Attachment?> _idCardAttachments = new List<Attachment?>();
        private List<Attachment?> _certAttachments = new List<Attachment?>();
        private int _currentIDCardSlot = 1;
        private int _currentCertSlot = 1;

        private string? _currentShowDirection;

        private bool _suppressAutoSave;
        private bool _isLoadingCertSlot;
        private string? _cachedProjectsDateRoot;

        private SettingsService? _settingsService;

        private DispatcherTimer? _autoSaveTimer;
        private PersonnelRecord? _autoSaveRecord;

        private DispatcherTimer? _dateFormatTimer;

        private DropdownMenu? _activeCardMenu;
        private DropdownMenu? _activeTextBoxMenu;
        private TextBox? _currentRightClickTextBox;

        private string? _lastParsedIDCardNumber; // 上次已解析的身份证号，避免重复解析
        private string? _declinedIDCardNumber;   // 用户拒绝覆盖的身份证号，避免反复弹窗
        private bool _isIDCardConfirmShowing;    // 冲突确认框显示中，防止模态期间定时器级联再次弹窗

        private static IOcrService? _ocrService;
        private static readonly object _ocrServiceLock = new();

        private readonly string[] _certSlotName = new string[5];
        private readonly string[] _certSlotDocNumber = new string[5];
        private readonly string[] _certSlotStart = new string[5];
        private readonly string[] _certSlotEnd = new string[5];

        private const int AutoSaveDelayMilliseconds = 500;

        public PersonnelRecord Record
        {
            get => (PersonnelRecord)GetValue(RecordProperty);
            set => SetValue(RecordProperty, value);
        }
        public static readonly DependencyProperty RecordProperty =
            DependencyProperty.Register(nameof(Record), typeof(PersonnelRecord), typeof(PersonnelRecordCardView),
                new PropertyMetadata(null, OnRecordChanged));

        #endregion

        #region 构造函数与生命周期

        public PersonnelRecordCardView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _mainWindow = Window.GetWindow(this);
                _mainScrollViewer = FindParent<ScrollViewer>(this);
                _settingsService = AppServiceProvider.SettingsService;
                UpdateSeasonalBackground();
                Dispatcher.BeginInvoke(new Action(() => LoadAttachments()), System.Windows.Threading.DispatcherPriority.Background);
            };
            Unloaded += (s, e) =>
            {
                Log.Information("Card Unloaded: Record={Rec}, Title={Title}", Record?.Id, Record?.Title);
                _autoSaveTimer?.Stop();
                _dateFormatTimer?.Stop();
            };
        }

        private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PersonnelRecordCardView view && e.NewValue is PersonnelRecord record)
            {
                view._cachedProjectsDateRoot = null;
                view._lastParsedIDCardNumber = null; // 切换记录卡时重置身份解析跟踪
                view._declinedIDCardNumber = null;
                view._isIDCardConfirmShowing = false;
                view.SetupAutoSave(record);
            }
        }

        private void UpdateSeasonalBackground()
        {
            var bgKey = SolarTermHelper.GetSeasonBackgroundKey(DateTime.Now);
            if (Application.Current.Resources[bgKey] is ImageBrush brush)
                CardBorder.Background = brush;
        }

        #endregion

        #region 保存记录

        private void SaveRecord()
        {
            if (Record == null)
            {
                Log.Warning("SaveRecord: Record 为 null，跳过保存");
                return;
            }

            SaveCurrentCertSlotText();
            WriteCertSlotsToRecord();

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel?.GetProjectDataService();
            if (dataService != null)
            {
                Log.Information("SaveRecord: 保存记录 Id={Id}, Title={Title}, Gender={Gender}, Age={Age}",
                    Record.Id, Record.Title, Record.Gender, Record.Age);
                dataService.UpdatePersonnelRecord(Record);
            }

            TryAutoFillFromIDCard();
        }

        #endregion

        #region 身份证自动填充

        /// <summary>
        /// 校验 18 位身份证号的校验码
        /// </summary>
        private static bool ValidateIDCardNumber(string idNumber)
        {
            if (string.IsNullOrEmpty(idNumber) || idNumber.Length != 18) return false;

            // 前17位必须为数字
            for (int i = 0; i < 17; i++)
            {
                if (!char.IsDigit(idNumber[i])) return false;
            }

            // 校验码计算
            int[] weights = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
            char[] checkCodes = { '1', '0', 'X', '9', '8', '7', '6', '5', '4', '3', '2' };

            int sum = 0;
            for (int i = 0; i < 17; i++)
                sum += (idNumber[i] - '0') * weights[i];

            char expectedCheck = checkCodes[sum % 11];
            char actualCheck = char.ToUpper(idNumber[17]);

            return expectedCheck == actualCheck;
        }

        /// <summary>
        /// 保存记录后，检测身份证号并自动填充性别和出生年月
        /// </summary>
        private void TryAutoFillFromIDCard()
        {
            if (Record == null) return;

            // 冲突确认框显示期间（模态弹窗内定时器仍会触发），直接跳过，避免级联弹窗
            if (_isIDCardConfirmShowing) return;

            string idNumber = Record.IDCardDocNumber?.Trim() ?? "";
            if (string.IsNullOrEmpty(idNumber) || idNumber.Length != 18) return;

            // 与上次已解析号码相同则跳过
            if (idNumber == _lastParsedIDCardNumber) return;

            // 用户已拒绝过该号码的覆盖，不再重复询问
            if (idNumber == _declinedIDCardNumber) return;

            if (!ValidateIDCardNumber(idNumber)) return;

            // 解析性别（第17位，奇男偶女）
            char genderDigit = idNumber[16];
            string parsedGender = (genderDigit % 2 == 0) ? "女" : "男";

            // 解析出生日期（第7-14位）
            string yearStr = idNumber.Substring(6, 4);
            string monthStr = idNumber.Substring(10, 2);
            string dayStr = idNumber.Substring(12, 2);

            if (!int.TryParse(yearStr, out int year) || year < 1900 || year > 2100) return;
            if (!int.TryParse(monthStr, out int month) || month < 1 || month > 12) return;
            if (!int.TryParse(dayStr, out int day) || day < 1 || day > 31) return;

            // 验证日期有效性
            try
            {
                var parsedDate = new DateTime(year, month, day);
                if (parsedDate > DateTime.Today) return; // 出生日期不能是未来
            }
            catch { return; }

            string parsedBirthDate = $"{year:D4}-{month:D2}-{day:D2}";

            // 比对已有值
            string currentGender = Record.Gender?.Trim() ?? "";
            string currentBirthDate = Record.BirthDate?.Trim() ?? "";

            bool genderMatch = string.IsNullOrEmpty(currentGender) || currentGender == parsedGender;
            bool birthMatch = string.IsNullOrEmpty(currentBirthDate) || currentBirthDate == parsedBirthDate;

            // 都匹配 → 直接填入
            if (genderMatch && birthMatch)
            {
                _lastParsedIDCardNumber = idNumber;
                Record.Gender = parsedGender;
                Record.BirthDate = parsedBirthDate;
                // Age 自动跟随 BirthDate 计算（PersonnelRecord 已联动 PropertyChanged）
                SaveRecord();
                Log.Information("TryAutoFillFromIDCard: 自动填充 Gender={Gender}, BirthDate={BirthDate}, IDCard={ID}",
                    parsedGender, parsedBirthDate, idNumber);
                return;
            }

            // 有冲突 → 弹出确认框
            string conflictMsg = "检测到身份证号，解析结果如下：\n\n";
            conflictMsg += $"性别：{parsedGender}（当前：{(string.IsNullOrEmpty(currentGender) ? "空" : currentGender)}）\n";
            conflictMsg += $"出生年月：{parsedBirthDate}（当前：{(string.IsNullOrEmpty(currentBirthDate) ? "空" : currentBirthDate)}）\n\n";
            conflictMsg += "是否用身份证号解析结果覆盖？";

            _isIDCardConfirmShowing = true;
            try
            {
                if (MessageDialog.Confirm(conflictMsg, "身份证自动填充") == true)
                {
                    _lastParsedIDCardNumber = idNumber;
                    Record.Gender = parsedGender;
                    Record.BirthDate = parsedBirthDate;
                    SaveRecord();
                    Log.Information("TryAutoFillFromIDCard: 用户确认覆盖 Gender={Gender}, BirthDate={BirthDate}",
                        parsedGender, parsedBirthDate);
                }
                else
                {
                    // 用户拒绝覆盖，记录该号码，避免后续自动保存时反复弹窗
                    _declinedIDCardNumber = idNumber;
                    Log.Information("TryAutoFillFromIDCard: 用户拒绝覆盖 IDCard={ID}", idNumber);
                }
            }
            finally
            {
                _isIDCardConfirmShowing = false;
            }
        }

        #endregion

        #region 辅助方法

        private ProjectDataService? GetDataService()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            return mainWindow?.ViewModel?.GetProjectDataService();
        }

        private string GetProjectsDateRoot()
        {
            if (_cachedProjectsDateRoot != null)
                return _cachedProjectsDateRoot;

            var dataService = GetDataService();
            if (dataService != null)
            {
                string dbPath = dataService.GetDbPath();
                if (!string.IsNullOrEmpty(dbPath))
                {
                    var projectFolder = Path.GetDirectoryName(dbPath);
                    var root = Path.GetDirectoryName(projectFolder) ?? "";
                    _cachedProjectsDateRoot = root;
                    return root;
                }
            }
            return "";
        }

        private T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T tParent)
                    return tParent;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        public void ClearContent()
        {
            if (Record == null) return;
            _lastParsedIDCardNumber = null; // 重置身份证解析跟踪
            _declinedIDCardNumber = null;
            _isIDCardConfirmShowing = false;
            Record.Title = "";
            Record.Gender = "";
            Record.Age = "";
            Record.IDCardDocNumber = "";
            Record.NativePlace = "";
            Record.BirthDate = "";
            Record.EntryTime = "";
            Record.ExitTime = "";
            Record.EducationLevel = "";
            Record.PhoneNumber = "";
            Record.Position = "";
            Record.HomeAddress = "";
            Record.IDCardStart = "";
            Record.IDCardEnd = "";
            Record.Cert1Name = ""; Record.Cert1DocNumber = ""; Record.Cert1Start = ""; Record.Cert1End = "";
            Record.Cert2Name = ""; Record.Cert2DocNumber = ""; Record.Cert2Start = ""; Record.Cert2End = "";
            Record.Cert3Name = ""; Record.Cert3DocNumber = ""; Record.Cert3Start = ""; Record.Cert3End = "";
            Record.Cert4Name = ""; Record.Cert4DocNumber = ""; Record.Cert4Start = ""; Record.Cert4End = "";
            Record.Cert5Name = ""; Record.Cert5DocNumber = ""; Record.Cert5Start = ""; Record.Cert5End = "";
            Record.Remark = "";
            Record.ThreeCategoryPersonnel = false;
            Record.SpecialOperationsPersonnel = false;

            // 清空证书槽位缓存文本（文本框非数据绑定，需同步清空数组并刷新当前槽位显示）
            for (int i = 0; i < _certSlotName.Length; i++)
            {
                _certSlotName[i] = "";
                _certSlotDocNumber[i] = "";
                _certSlotStart[i] = "";
                _certSlotEnd[i] = "";
            }
            LoadCurrentCertSlotText();

            // 清空图片附件（身份证 + 证书：数据库记录 + 物理文件）
            var dataService = GetDataService();
            if (dataService != null)
            {
                dataService.DeleteAttachmentsByRecord(Record.Id);
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow?.ViewModel?.PersonnelRecords is PersonnelRecordsViewModel cacheVm)
                    cacheVm.InvalidateAttachmentsCache();
            }

            SaveRecord();
            LoadAttachments();
        }

        #endregion
    }
}
