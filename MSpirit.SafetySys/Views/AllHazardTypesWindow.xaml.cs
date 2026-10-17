// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MS_SafetyManagementSystem.Views
{
    public partial class AllHazardTypesWindow : Window
    {
        private readonly AllHazardTypesViewModel _viewModel;
        private bool _isClosing = false;

        public string? SelectedTypeName { get; private set; }

        [DllImport("user32.dll")]
        private static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

        public AllHazardTypesWindow(CommonHazardTypeService service)
        {
            InitializeComponent();
            _viewModel = new AllHazardTypesViewModel(service, this);
            DataContext = _viewModel;

            // 初始透明度为 0（窗口尚未显示，先设置，等 ContentRendered 时再播放淡入）
            this.Opacity = 0;
            this.ContentRendered += OnContentRendered;

            this.Deactivated += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                FlashWindow(hwnd, true);
            };

            this.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    CloseWithFadeOut(false);
                }
            };
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            // 播放淡入动画
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(399));
            fadeIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        internal async void CloseWithFadeOut(bool? dialogResult)
        {
            if (_isClosing) return;
            _isClosing = true;

            // 播放淡出动画
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
            var tcs = new TaskCompletionSource<bool>();
            fadeOut.Completed += (s, _) => tcs.SetResult(true);
            this.BeginAnimation(OpacityProperty, fadeOut);
            await tcs.Task;

            if (dialogResult.HasValue)
                DialogResult = dialogResult;

            this.Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                return;
            DragMove();
        }

        private void RootBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var originalSource = e.OriginalSource as DependencyObject;
            if (originalSource == null) return;

            bool isButton = FindParent<Button>(originalSource) != null;
            bool isTitleBar = originalSource == TitleBarGrid || IsVisualChildOf(TitleBarGrid, originalSource);

            if (!isButton && !isTitleBar)
            {
                DragMove();
                e.Handled = true;
            }
        }

        private T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t) return t;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private bool IsVisualChildOf(DependencyObject parent, DependencyObject child)
        {
            while (child != null)
            {
                if (child == parent) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        internal void SetSelectedType(string typeName)
        {
            SelectedTypeName = typeName;
            CloseWithFadeOut(true);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isClosing)
            {
                e.Cancel = true;
                CloseWithFadeOut(DialogResult);
            }
            base.OnClosing(e);
        }
    }

    // 以下 ButtonItem, AllHazardTypesViewModel, BoolToStyleConverter 保持不变（同您当前代码）
    // 注意：AllHazardTypesViewModel 中的 "关闭窗口" 按钮命令应改为调用 _owner.CloseWithFadeOut(false)
    // 但您当前的代码中 ExecuteButtonCommand 里 "Close" 分支调用的是 _owner.Close()，需要修改为 _owner.CloseWithFadeOut(false)
    // 我会一并给出修改后的 ViewModel

    public class ButtonItem : INotifyPropertyChanged
    {
        public string DisplayText { get; set; } = "";
        public string CommandParameter { get; set; } = "";
        public bool IsActionButton { get; set; } = false;
        public string CodeText { get; set; } = "";
        public bool IsDummy { get; set; } = false;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AllHazardTypesViewModel : INotifyPropertyChanged
    {
        private readonly CommonHazardTypeService _service;
        private readonly AllHazardTypesWindow _owner;
        private ObservableCollection<ButtonItem> _allButtonItems = new();

        public ObservableCollection<ButtonItem> AllButtonItems
        {
            get => _allButtonItems;
            set { _allButtonItems = value; OnPropertyChanged(); }
        }

        public ICommand ButtonCommand { get; }

        public AllHazardTypesViewModel(CommonHazardTypeService service, AllHazardTypesWindow owner)
        {
            _service = service;
            _owner = owner;
            ButtonCommand = new RelayCommand<string>(ExecuteButtonCommand);
            LoadData();
        }

        private void LoadData()
        {
            var allTypes = _service.GetAllTypes();
            AllButtonItems.Clear();

            foreach (var item in allTypes)
            {
                AllButtonItems.Add(new ButtonItem
                {
                    DisplayText = item.Name,
                    CommandParameter = item.Code,
                    CodeText = item.Code,
                    IsActionButton = false,
                    IsDummy = false
                });
            }

            for (int i = 0; i < 3; i++)
            {
                AllButtonItems.Add(new ButtonItem
                {
                    DisplayText = "",
                    CommandParameter = "",
                    IsActionButton = true,
                    IsDummy = true
                });
            }

            AllButtonItems.Add(new ButtonItem
            {
                DisplayText = "取消类型选择",
                CommandParameter = "Clear",
                IsActionButton = true,
                IsDummy = false,
                CodeText = "  "
            });
            AllButtonItems.Add(new ButtonItem
            {
                DisplayText = "关闭窗口",
                CommandParameter = "Close",
                IsActionButton = true,
                IsDummy = false,
                CodeText = "  "
            });
        }

        private void ExecuteButtonCommand(string? param)
        {
            if (param == null) return;

            if (param == "Clear")
            {
                _owner.SetSelectedType("");
            }
            else if (param == "Close")
            {
                // 使用淡出关闭
                _owner.CloseWithFadeOut(false);
            }
            else
            {
                var typeName = _service.GetNameByCode(param);
                if (!string.IsNullOrEmpty(typeName))
                {
                    _owner.SetSelectedType(typeName);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class BoolToStyleConverter : IValueConverter
    {
        public Style? TrueStyle { get; set; }
        public Style? FalseStyle { get; set; }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool b && b)
                return TrueStyle ?? throw new ArgumentNullException(nameof(TrueStyle));
            else
                return FalseStyle ?? throw new ArgumentNullException(nameof(FalseStyle));
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}