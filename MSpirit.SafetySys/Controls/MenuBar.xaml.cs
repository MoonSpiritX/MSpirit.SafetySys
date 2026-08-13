// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;

namespace MS_SafetyManagementSystem.Controls
{
    public partial class MenuBar : UserControl
    {
        public event Action? NewProjectClicked;
        public event Action? ManageProjectClicked;
        public event Action? ModuleSafetyClicked;
        public event Action? ModulePersonnelClicked;
        public event Action? SystemViewerClicked;
        public event Action<string>? InternalViewerStartModeClicked; // "FullScreen" / "Normal"
        public event Action? ManageCommonHazardTypesClicked;
        public event Action? ManageLinksClicked;
        public event Action? ImageProcessClicked;
        public event Action? UsageGuideClicked;
        public event Action? AboutClicked;
        public event Action? CloseProgramClicked;
        public event Action? PhotoTransferClicked;

        /// <summary>项目快速切换事件，参数为项目 Id</summary>
        public event Action<string>? ProjectSwitchRequested;

        private bool _isSafetyModuleActive = true;
        public bool IsSafetyModuleActive
        {
            get => _isSafetyModuleActive;
            set
            {
                if (_isSafetyModuleActive == value) return;
                _isSafetyModuleActive = value;
                UpdateModuleIcons();
            }
        }

        private string _currentViewerType = ImageViewerType.Internal;
        private string _currentStartMode = ImageViewerStartMode.FullScreen;

        // ── 图标资源缓存（构造时一次性查找；窗口关闭/程序退出时 App 资源树可能被拆除，
        //    此时再 FindResource 会抛"未找到资源"异常，必须用缓存字段） ──
        private readonly ImageSource _checkedIcon;
        private readonly ImageSource _uncheckedIcon;
        private readonly ImageSource _transparentIcon;
        private readonly Style _subMenuItemStyle;

        // ── 项目动态列表管理 ──
        private readonly List<MenuItem> _projectItems = new();
        private bool _hasSeparator;
        private bool _scrollViewerInjected;

        public MenuBar()
        {
            InitializeComponent();
            _checkedIcon = (ImageSource)FindResource("CheckedIcon");
            _uncheckedIcon = (ImageSource)FindResource("UncheckedIcon");
            _transparentIcon = CreateTransparentIcon();
            _subMenuItemStyle = (Style)FindResource("SubMenuItemStyle");
            UpdateModuleIcons();
            UpdateViewerIcons();
            ProjectMenu.SubmenuOpened += OnProjectMenuOpened;
        }

        /// <summary>
        /// 首次打开项目菜单时，在 Popup 内注入 ScrollViewer 限制最大高度。
        /// 无需覆盖默认 MenuItem 模板，外观与其他菜单完全一致。
        /// </summary>
        private void OnProjectMenuOpened(object sender, RoutedEventArgs e)
        {
            if (_scrollViewerInjected) return;

            // 在 ProjectMenu 的可视树中找到 Popup
            var popup = FindVisualChild<Popup>(ProjectMenu);
            if (popup?.Child is Border border && border.Child is ItemsPresenter presenter)
            {
                var scrollViewer = new ScrollViewer
                {
                    MaxHeight = 500,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                // 用 ScrollViewer 替代 ItemsPresenter
                border.Child = scrollViewer;
                scrollViewer.Content = presenter;
            }
            _scrollViewerInjected = true;
        }

        /// <summary>
        /// 创建 16×16 透明占位图标。
        /// 项目列表菜单项统一携带 Icon（当前项目为勾选图标、其余为透明占位），
        /// 使所有项目名的文本起点对齐（SubMenuItemStyle 模板中 Icon 列占位固定宽度）。
        /// </summary>
        private static ImageSource CreateTransparentIcon()
        {
            return new System.Windows.Media.Imaging.WriteableBitmap(
                16, 16, 96, 96, PixelFormats.Bgra32, null);
        }

        /// <summary>在可视树中查找第一个指定类型的子元素</summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 刷新项目下拉列表中的动态项目菜单项。
        /// 在"新建项目"和"项目及存档管理"下方添加分隔线，然后列出所有项目。
        /// </summary>
        public void RefreshProjectList(List<ProjectInfo> projects, string currentProjectId)
        {
            // 防御：程序退出（Shutdown）流程中可能被调用，此时菜单树正在拆除，
            // 重建菜单项失败时仅记日志，不向 UI 线程抛异常（避免退出时弹窗）
            try
            {
                RefreshProjectListCore(projects, currentProjectId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "退出流程中重建项目菜单失败（忽略）");
            }
        }

        private void RefreshProjectListCore(List<ProjectInfo> projects, string currentProjectId)
        {
            // 1. 移除旧的动态项目项
            foreach (var item in _projectItems)
                ProjectMenu.Items.Remove(item);
            _projectItems.Clear();

            // 2. 分隔线管理：有项目才显示，无项目则移除
            if (projects.Count == 0)
            {
                if (_hasSeparator)
                {
                    // 分隔线在 Items[2] 位置
                    if (ProjectMenu.Items.Count > 2 && ProjectMenu.Items[2] is Separator)
                        ProjectMenu.Items.RemoveAt(2);
                    _hasSeparator = false;
                }
                return;
            }

            if (!_hasSeparator)
            {
                // 在"项目及存档管理"（Items[1]）之后插入 Separator
                ProjectMenu.Items.Insert(2, new Separator());
                _hasSeparator = true;
            }

            // 3. 添加每个项目作为菜单项
            foreach (var project in projects)
            {
                var isCurrent = project.Id == currentProjectId;

                var mi = new MenuItem
                {
                    Header = project.Name,
                    Style = _subMenuItemStyle,
                    FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                    // 所有项目项统一携带 Icon：当前项目显示勾选图标，其余项目用透明占位，
                    // 保证项目名文本起点一致（与上部分功能菜单错开，整体向后对齐）
                    Icon = new Image
                    {
                        Source = isCurrent ? _checkedIcon : _transparentIcon,
                        Width = 16,
                        Height = 16
                    }
                };

                var capturedId = project.Id;
                mi.Click += (_, _) => ProjectSwitchRequested?.Invoke(capturedId);

                ProjectMenu.Items.Add(mi);
                _projectItems.Add(mi);
            }
        }

        public void UpdateModuleIcons()
        {
            SafetyImage.Source = IsSafetyModuleActive ? _checkedIcon : _uncheckedIcon;
            PersonnelImage.Source = IsSafetyModuleActive ? _uncheckedIcon : _checkedIcon;
        }

        public void SetViewerType(string viewerType)
        {
            _currentViewerType = viewerType;
            UpdateViewerIcons();
        }

        public void SetViewerStartMode(string startMode)
        {
            _currentStartMode = startMode;
            UpdateViewerIcons();
        }

        public void UpdateViewerIcons()
        {
            bool isInternal = _currentViewerType == ImageViewerType.Internal;
            InternalViewerFullScreenIcon.Source = isInternal && _currentStartMode == ImageViewerStartMode.FullScreen ? _checkedIcon : _uncheckedIcon;
            InternalViewerNormalIcon.Source = isInternal && _currentStartMode == ImageViewerStartMode.Normal ? _checkedIcon : _uncheckedIcon;
            SystemViewerIcon.Source = !isInternal ? _checkedIcon : _uncheckedIcon;
        }

        private void OnNewProject(object sender, RoutedEventArgs e) => NewProjectClicked?.Invoke();
        private void OnManageProject(object sender, RoutedEventArgs e) => ManageProjectClicked?.Invoke();
        private void OnModuleSafety(object sender, RoutedEventArgs e)
        {
            IsSafetyModuleActive = true;
            UpdateModuleIcons();
            ModuleSafetyClicked?.Invoke();
        }
        private void OnModulePersonnel(object sender, RoutedEventArgs e)
        {
            IsSafetyModuleActive = false;
            UpdateModuleIcons();
            ModulePersonnelClicked?.Invoke();
        }
        private void OnInternalViewerFullScreen(object sender, RoutedEventArgs e)
        {
            _currentViewerType = ImageViewerType.Internal;
            _currentStartMode = ImageViewerStartMode.FullScreen;
            UpdateViewerIcons();
            InternalViewerStartModeClicked?.Invoke(ImageViewerStartMode.FullScreen);
        }
        private void OnInternalViewerNormal(object sender, RoutedEventArgs e)
        {
            _currentViewerType = ImageViewerType.Internal;
            _currentStartMode = ImageViewerStartMode.Normal;
            UpdateViewerIcons();
            InternalViewerStartModeClicked?.Invoke(ImageViewerStartMode.Normal);
        }
        private void OnSystemViewer(object sender, RoutedEventArgs e)
        {
            _currentViewerType = ImageViewerType.System;
            UpdateViewerIcons();
            SystemViewerClicked?.Invoke();
        }
        private void OnManageCommonHazardTypes(object sender, RoutedEventArgs e) => ManageCommonHazardTypesClicked?.Invoke();
        private void OnManageLinks(object sender, RoutedEventArgs e) => ManageLinksClicked?.Invoke();
        private void OnImageProcess(object sender, RoutedEventArgs e) => ImageProcessClicked?.Invoke();
        private void OnUsageGuide(object sender, RoutedEventArgs e) => UsageGuideClicked?.Invoke();

        private void OnAbout(object sender, RoutedEventArgs e) => AboutClicked?.Invoke();

        private void OnCloseProgram(object sender, RoutedEventArgs e) => CloseProgramClicked?.Invoke();

        private void OnPhotoTransfer(object sender, RoutedEventArgs e) => PhotoTransferClicked?.Invoke();

        private Point _dragStartPoint;

        private void MenuBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(this);
        }

        private void MenuBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var win = Window.GetWindow(this);
            if (win != null) win.DragMove();
        }

        /// <summary>
        /// 项目列表菜单禁用引用计数。
        /// 多个来源共用同一禁用：LAN 批量录入窗口打开期间、导出/导入执行期间。
        /// 每个来源 Request 一次、Release 一次，计数归零才恢复可用，避免互相踩踏。
        /// </summary>
        private int _projectMenuDisableCount;

        /// <summary>请求禁用项目列表菜单（引用计数 +1，须与 ReleaseProjectMenuDisable 成对调用）</summary>
        public void RequestProjectMenuDisable()
        {
            _projectMenuDisableCount++;
            ProjectMenu.IsEnabled = false;
        }

        /// <summary>释放一次禁用请求（引用计数 -1）；全部释放后项目菜单才恢复可用</summary>
        public void ReleaseProjectMenuDisable()
        {
            if (_projectMenuDisableCount > 0) _projectMenuDisableCount--;
            if (_projectMenuDisableCount == 0)
                ProjectMenu.IsEnabled = true;
        }

        // ══════════════════════════════════════════
        //  链接菜单（政府信息官网 / 外部AI官网 / 自定义网址）
        // ══════════════════════════════════════════

        /// <summary>
        /// 从 FavoriteLinksService 获取分类数据，刷新 3 个链接菜单的内容。
        /// </summary>
        public void RefreshLinkMenus(List<LinkCategory> categories)
        {
            BuildLinkMenu(GovernmentMenu, categories, "政府信息官网", "(暂无内容)");
            BuildLinkMenu(AIMenu, categories, "外部AI官网", "(暂无内容)");
            BuildLinkMenu(CustomMenu, categories, "自定义网址", "暂无自定义网址内容");
        }

        /// <summary>
        /// 为指定父菜单根据分类名构建子菜单项。
        /// </summary>
        private void BuildLinkMenu(MenuItem parentMenu, List<LinkCategory> categories, string categoryName, string emptyText)
        {
            parentMenu.Items.Clear();

            var cat = categories.FirstOrDefault(c => c.Name == categoryName);
            if (cat == null || cat.Links.Count == 0)
            {
                // 无链接时显示提示项
                var emptyItem = new MenuItem
                {
                    Header = emptyText,
                    Style = (Style)FindResource("SubMenuItemStyle"),
                    IsEnabled = false
                };
                parentMenu.Items.Add(emptyItem);
                return;
            }

            foreach (var link in cat.Links.OrderBy(l => l.Order))
            {
                var mi = new MenuItem
                {
                    Header = link.Name,
                    Style = (Style)FindResource("SubMenuItemStyle"),
                    ToolTip = string.IsNullOrEmpty(link.Description) ? link.Url : $"{link.Description}\n{link.Url}"
                };
                var url = link.Url;
                mi.Click += (_, _) => OpenUrl(url);
                parentMenu.Items.Add(mi);
            }
        }

        /// <summary>用默认浏览器打开网址</summary>
        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开网址：{ex.Message}", "错误");
            }
        }
    }
}
