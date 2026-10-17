// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;      // 新增：解决 Border, CheckBox 类型识别
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Models;
using Wpf.Ui.Controls;

namespace MS_SafetyManagementSystem.Views
{
    public partial class ManageCommonHazardTypesWindow : FluentWindow
    {
        private readonly ManageCommonHazardTypesViewModel _viewModel;
        private readonly SettingsService _settingsService;

        public ManageCommonHazardTypesWindow(CommonHazardTypeService service, SettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _viewModel = new ManageCommonHazardTypesViewModel(service, () => this, settingsService);
            DataContext = _viewModel;
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            bool isFullWindow = FullWindowRadio.IsChecked == true;
            TypesScrollViewer.IsEnabled = !isFullWindow;
        }

        // 整项点击切换复选框（Button 为真 ButtonBase：拖拽白名单自动豁免，不会误触发窗口拖动）
        private void CommonTypeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Content is CheckBox checkBox)
            {
                checkBox.IsChecked = !checkBox.IsChecked;
            }
        }
    }

    public class ManageCommonHazardTypesViewModel : INotifyPropertyChanged
    {
        private readonly CommonHazardTypeService _service;
        private readonly Func<Window> _getOwnerWindow;
        private readonly SettingsService _settingsService;
        private ObservableCollection<HazardTypeSelectItem> _allTypes = new();
        public static event Action? HazardTypeSelectorModeChanged;

        public ObservableCollection<HazardTypeSelectItem> AllTypes
        {
            get => _allTypes;
            set { _allTypes = value; OnPropertyChanged(); }
        }

        private bool _isFullWindowMode;
        public bool IsFullWindowMode
        {
            get => _isFullWindowMode;
            set { _isFullWindowMode = value; OnPropertyChanged(); }
        }

        private bool _isDropdownMenuMode;
        public bool IsDropdownMenuMode
        {
            get => _isDropdownMenuMode;
            set { _isDropdownMenuMode = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public ManageCommonHazardTypesViewModel(CommonHazardTypeService service, Func<Window> getOwnerWindow, SettingsService settingsService)
        {
            _service = service;
            _getOwnerWindow = getOwnerWindow;
            _settingsService = settingsService;

            SaveCommand = new RelayCommand(ExecuteSave);
            CancelCommand = new RelayCommand(ExecuteCancel);

            string currentMode = _settingsService.GetHazardTypeSelectorMode();
            IsFullWindowMode = (currentMode == "FullWindow");
            IsDropdownMenuMode = (currentMode == "DropdownMenu");

            LoadData();
        }

        private void LoadData()
        {
            var all = _service.GetAllTypes();
            var commonCodes = _service.GetCommonCodes();
            var commonSet = new HashSet<string>(commonCodes);

            AllTypes.Clear();
            foreach (var item in all)
            {
                AllTypes.Add(new HazardTypeSelectItem
                {
                    Code = item.Code,
                    Name = item.Name,
                    IsSelected = commonSet.Contains(item.Code)
                });
            }
        }

        private void ExecuteSave()
        {
            string mode = IsFullWindowMode ? "FullWindow" : "DropdownMenu";
            _settingsService.SetHazardTypeSelectorMode(mode);
            HazardTypeSelectorModeChanged?.Invoke();  // 触发全局刷新

            if (IsDropdownMenuMode)
            {
                var selectedCodes = AllTypes.Where(x => x.IsSelected).Select(x => x.Code).ToList();
                _service.SetCommonCodes(selectedCodes);
                CommonHazardTypeChanged?.Invoke();
            }

            var window = _getOwnerWindow();
            window.DialogResult = true;
            window.Close();
        }

        private void ExecuteCancel()
        {
            var window = _getOwnerWindow();
            window.DialogResult = false;
            window.Close();
        }

        public static event Action? CommonHazardTypeChanged;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class HazardTypeSelectItem : INotifyPropertyChanged
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}