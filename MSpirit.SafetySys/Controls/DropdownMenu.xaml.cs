// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Controls
{
    public partial class DropdownMenu : UserControl
    {
        /// <summary>
        /// 菜单关闭时触发（包括点击外部关闭、窗口失活、代码调用 Close）
        /// </summary>
        public event Action? Closed;

        private readonly StackPanel _menuPanel;
        private Popup? _activePopup;
        private Window? _ownerWindow;
        private FrameworkElement? _triggerElement;

        /// <summary>
        /// 关闭重入守卫。见 <see cref="Close"/> 注释：释放鼠标捕获会同步重派发 MouseDown，
        /// 调用方的 toggle 逻辑可能再次调用 Close()，没有该标记时外层会用到已被内层置空的字段。
        /// </summary>
        private bool _isClosing;

        // ====== 搜索建议模式（实时过滤下拉） ======
        private static readonly Brush SuggestionHighlightBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xF3, 0xFF));
        private readonly List<Button> _suggestionButtons = new();
        private int _selectedSuggestionIndex = -1;
        private ScrollViewer? _activeScrollViewer;

        public DropdownMenu()
        {
            InitializeComponent();
            _menuPanel = new StackPanel();
        }

        /// <summary>
        /// 添加一个菜单项按钮
        /// </summary>
        public Button AddItem(string text, Action? clickAction = null)
        {
            var btn = new Button
            {
                Content = text,
                Style = (Style)Resources["DropdownMenuItemStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (clickAction != null)
            {
                btn.Click += (s, e) =>
                {
                    clickAction();
                    Close();
                };
            }
            _menuPanel.Children.Add(btn);
            return btn;
        }

        /// <summary>
        /// 添加一条分隔线
        /// </summary>
        public Separator AddSeparator()
        {
            var separator = new Separator
            {
                Margin = new Thickness(0, 1, 0, 1)
            };
            _menuPanel.Children.Add(separator);
            return separator;
        }

        /// <summary>
        /// 当前弹层是否打开（供调用方判断键盘导航是否生效）
        /// </summary>
        public bool IsOpen => _activePopup?.IsOpen == true;

        /// <summary>
        /// 清空全部菜单项（搜索建议模式重建列表时使用）
        /// </summary>
        public void ClearItems()
        {
            _menuPanel.Children.Clear();
            _suggestionButtons.Clear();
            _selectedSuggestionIndex = -1;
        }

        /// <summary>
        /// 搜索建议模式：添加一个建议项，支持键盘导航高亮
        /// </summary>
        public Button AddSuggestionItem(string text, Action? clickAction = null)
        {
            var btn = AddItem(text, clickAction);
            _suggestionButtons.Add(btn);
            return btn;
        }

        /// <summary>
        /// 搜索建议模式：处理键盘导航（↑/↓ 移动高亮、Enter 选中、Esc 关闭）。
        /// 返回 true 表示按键已被消费。
        /// </summary>
        public bool HandleNavigationKey(Key key)
        {
            if (_activePopup?.IsOpen != true || _suggestionButtons.Count == 0)
                return false;

            switch (key)
            {
                case Key.Down:
                    MoveSuggestionSelection(1);
                    return true;
                case Key.Up:
                    MoveSuggestionSelection(-1);
                    return true;
                case Key.Enter:
                    if (_selectedSuggestionIndex >= 0 && _selectedSuggestionIndex < _suggestionButtons.Count)
                    {
                        _suggestionButtons[_selectedSuggestionIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        return true;
                    }
                    Close();
                    return true;
                case Key.Escape:
                    Close();
                    return true;
                default:
                    return false;
            }
        }

        private void MoveSuggestionSelection(int delta)
        {
            if (_suggestionButtons.Count == 0) return;
            int newIndex = _selectedSuggestionIndex + delta;
            if (newIndex < 0) newIndex = 0;
            if (newIndex >= _suggestionButtons.Count) newIndex = _suggestionButtons.Count - 1;
            if (newIndex == _selectedSuggestionIndex) return;
            _selectedSuggestionIndex = newIndex;
            UpdateSuggestionHighlight();
            ScrollSelectedSuggestionIntoView();
        }

        private void UpdateSuggestionHighlight()
        {
            for (int i = 0; i < _suggestionButtons.Count; i++)
            {
                var btn = _suggestionButtons[i];
                if (i == _selectedSuggestionIndex)
                    btn.Background = SuggestionHighlightBrush;
                else
                    btn.ClearValue(Button.BackgroundProperty);
            }
        }

        private void ScrollSelectedSuggestionIntoView()
        {
            if (_activeScrollViewer == null || _selectedSuggestionIndex < 0) return;
            var btn = _suggestionButtons[_selectedSuggestionIndex];
            if (btn == null) return;

            double top = btn.TranslatePoint(new Point(0, 0), _menuPanel).Y;
            double bottom = top + btn.ActualHeight;
            double viewTop = _activeScrollViewer.VerticalOffset;
            double viewBottom = viewTop + _activeScrollViewer.ViewportHeight;

            if (top < viewTop)
                _activeScrollViewer.ScrollToVerticalOffset(top);
            else if (bottom > viewBottom)
                _activeScrollViewer.ScrollToVerticalOffset(bottom - _activeScrollViewer.ViewportHeight);
        }

        /// <summary>
        /// 在指定屏幕坐标位置弹出菜单（右键菜单模式）
        /// </summary>
        public void ShowAt(Point screenPoint)
        {
            ShowAtOffset(screenPoint, -5, -10);
        }

        /// <summary>
        /// 在指定元素的正下方弹出菜单（文本框下拉菜单模式）
        /// 使用 PlacementTarget + PlacementMode.Bottom 避免跨 DPI 坐标转换问题
        /// </summary>
        public void ShowBelow(FrameworkElement target)
        {
            Close();

            // 如果菜单项超过 10 个，添加滚动
            object content = _menuPanel;
            _activeScrollViewer = null;
            if (_menuPanel.Children.Count > 10)
            {
                _activeScrollViewer = new ScrollViewer
                {
                    Content = _menuPanel,
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                content = _activeScrollViewer;
            }

            var contentControl = new ContentControl
            {
                Style = (Style)Resources["DropdownContainerStyle"],
                Content = content
            };
            contentControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            _activePopup = new Popup
            {
                Child = contentControl,
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = -8,
                VerticalOffset = -8,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.Slide
            };
            _activePopup.IsOpen = true;

            _triggerElement = target;

            CaptureForAutoClose(contentControl);
            HookOwnerWindow();
        }

        /// <summary>
        /// 在指定元素的右侧弹出菜单（二级子菜单模式，如 ExportControl 的日期子菜单）
        /// 使用 PlacementTarget + PlacementMode.Right 避免跨 DPI 坐标转换问题
        /// </summary>
        public void ShowRightOf(FrameworkElement target)
        {
            Close();

            object content = _menuPanel;
            _activeScrollViewer = null;
            if (_menuPanel.Children.Count > 10)
            {
                _activeScrollViewer = new ScrollViewer
                {
                    Content = _menuPanel,
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                content = _activeScrollViewer;
            }

            var contentControl = new ContentControl
            {
                Style = (Style)Resources["DropdownContainerStyle"],
                Content = content
            };
            contentControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            _activePopup = new Popup
            {
                Child = contentControl,
                PlacementTarget = target,
                Placement = PlacementMode.Right,
                HorizontalOffset = -8,
                VerticalOffset = -10,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.Slide
            };
            _activePopup.IsOpen = true;

            _triggerElement = target;

            CaptureForAutoClose(contentControl);
            HookOwnerWindow();
        }

        private void ShowAtOffset(Point screenPoint, double horizontalOffset, double verticalOffset)
        {
            Close();

            // 如果菜单项超过 10 个，添加滚动
            object content = _menuPanel;
            _activeScrollViewer = null;
            if (_menuPanel.Children.Count > 10)
            {
                _activeScrollViewer = new ScrollViewer
                {
                    Content = _menuPanel,
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                content = _activeScrollViewer;
            }

            var contentControl = new ContentControl
            {
                Style = (Style)Resources["DropdownContainerStyle"],
                Content = content
            };
            contentControl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            _activePopup = new Popup
            {
                Child = contentControl,
                Placement = PlacementMode.AbsolutePoint,
                HorizontalOffset = screenPoint.X + horizontalOffset,
                VerticalOffset = screenPoint.Y + verticalOffset,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.Slide
            };
            _activePopup.IsOpen = true;

            CaptureForAutoClose(contentControl);
            HookOwnerWindow();
        }

        /// <summary>
        /// 关闭菜单。
        /// <para>
        /// ⚠️ 必须防重入（2026-10-17 修复导出界面「自定义时间范围」必崩的 NullReferenceException）：
        /// </para>
        /// <para>
        /// 关闭时先 <see cref="ReleaseCaptureForAutoClose(Popup?)"/> 释放鼠标捕获，而 <c>Mouse.Capture(null)</c>
        /// 会把当前这次 MouseDown **同步重新派发**给真实命中元素；调用方的 toggle 逻辑（如
        /// ExportControl 的「点触发元素 → 关闭菜单」）往往又调一次 Close()（内层）。
        /// 没有守卫时执行顺序是：外层通过判空 → 内层执行完并把 _activePopup 置为 null → 外层回到
        /// <c>_activePopup.IsOpen = false</c> 时字段已空 → 空引用崩溃（栈指向本方法）。
        /// </para>
        /// 此处用「局部变量 + _isClosing 守卫 + 引用比对」三重防护：内层直接返回、外层不依赖可被改写的字段、
        /// 重入期间若被重新打开则不清新弹层的状态。
        /// </summary>
        public void Close()
        {
            if (_isClosing) return;

            var popup = _activePopup;
            if (popup == null)
            {
                DetachOwnerWindow();
                ResetTransientState();
                return;
            }

            _isClosing = true;
            try
            {
                ReleaseCaptureForAutoClose(popup);
                popup.IsOpen = false;

                // 释放捕获期间可能被调用方「关旧开新」重新赋值，只有仍是本次要关闭的那个弹层时才摘字段
                if (ReferenceEquals(_activePopup, popup))
                    _activePopup = null;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[DropdownMenu] 关闭菜单时出错（已忽略）");
            }
            finally
            {
                _isClosing = false;
            }

            // 若期间已有新弹层打开，保留其 owner 监听与状态，不再触发 Closed
            if (_activePopup == null)
            {
                DetachOwnerWindow();
                ResetTransientState();
                Closed?.Invoke();
            }
        }

        /// <summary>解绑 owner 窗口的失活 / 外部点击 / 滚轮监听</summary>
        private void DetachOwnerWindow()
        {
            if (_ownerWindow == null) return;
            _ownerWindow.PreviewMouseDown -= OnExternalPreviewMouseDown;
            _ownerWindow.Deactivated -= OnOwnerDeactivated;
            _ownerWindow.PreviewMouseWheel -= OnPreviewMouseWheel;
            _ownerWindow = null;
        }

        /// <summary>清理与「当前这一次弹出」相关的临时状态（不影响菜单项本身）</summary>
        private void ResetTransientState()
        {
            _triggerElement = null;
            _activeScrollViewer = null;
            _suggestionButtons.Clear();
            _selectedSuggestionIndex = -1;
        }

        /// <summary>
        /// 鼠标捕获自动关闭：把鼠标捕获到菜单内容（CaptureMode.SubTree），
        /// 点击 Popup 外部任意位置（含其他窗口）即触发 <see cref="Mouse.PreviewMouseDownOutsideCapturedElementEvent"/> 关闭菜单。
        /// 这是 WPF 下拉/菜单的标准关闭机制，不依赖"找激活窗口挂 PreviewMouseDown"（独立窗口/托盘场景下不可靠）。
        /// </summary>
        private void CaptureForAutoClose(ContentControl contentControl)
        {
            Mouse.Capture(contentControl, CaptureMode.SubTree);
            contentControl.AddHandler(
                Mouse.PreviewMouseDownOutsideCapturedElementEvent,
                new MouseButtonEventHandler(OnOutsideMouseDown));
        }

        /// <summary>
        /// 释放自动关闭用的鼠标捕获。
        /// ⚠️ 这里必须接收 popup 参数而不是读字段：Close() 会在字段被改写前调用它，
        /// 且 <c>Mouse.Capture(null)</c> 会同步触发调用方逻辑（可能再次进入 Close）。
        /// </summary>
        private void ReleaseCaptureForAutoClose(Popup? popup)
        {
            if (popup?.Child is UIElement child)
            {
                child.RemoveHandler(
                    Mouse.PreviewMouseDownOutsideCapturedElementEvent,
                    new MouseButtonEventHandler(OnOutsideMouseDown));
                if (ReferenceEquals(Mouse.Captured, child))
                    Mouse.Capture(null);
            }
        }

        /// <summary>点击捕获子树外部（菜单区域外任意处）时关闭菜单</summary>
        private void OnOutsideMouseDown(object sender, MouseButtonEventArgs e)
        {
            // ⚠️ outside 事件的 e.OriginalSource 是捕获元素（Popup 内容），不是实际点击的目标，
            // 不能据此判断"点了哪里"（见 IsMouseOverElement 说明）。
            if (_triggerElement != null && IsMouseOverElement(_triggerElement))
            {
                // 点在触发元素（ShowBelow/ShowRightOf 锚点）上：**释放鼠标捕获**，让本次点击的
                // 路由事件（PreviewMouseDown / PreviewMouseLeftButtonDown）重新派发到触发元素，
                // 由调用方处理 toggle（弹出状态下点击触发元素 → 调用方关闭菜单）。
                // 注意：不释放捕获的话，点击事件被 Mouse.Capture 重定向到 Popup 内容，
                // 触发元素永远收不到事件 → toggle 无效（表现为"点击无效果"）。
                ReleaseCaptureForAutoClose(_activePopup);
                return;
            }

            Close();
        }

        /// <summary>鼠标当前位置是否在元素范围内（用于 outside 点击时判断是否点中了触发元素）</summary>
        private static bool IsMouseOverElement(FrameworkElement element)
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
            Point p;
            try
            {
                p = Mouse.GetPosition(element);
            }
            catch
            {
                Serilog.Log.Debug("[DropdownMenu] 获取鼠标位置失败（视为不在元素上）");
                return false;
            }
            return p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
        }

        /// <summary>
        /// 监听 owner 窗口：失活关闭 + 外部点击关闭 + 滚轮拦截。
        /// 与鼠标捕获机制互为双保险（捕获为主，owner 监听兜底窗口失活场景）。
        /// </summary>
        private void HookOwnerWindow()
        {
            _ownerWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (_ownerWindow != null)
            {
                _ownerWindow.PreviewMouseDown += OnExternalPreviewMouseDown;
                _ownerWindow.Deactivated += OnOwnerDeactivated;
                _ownerWindow.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }

        private void OnExternalPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var popupChild = _activePopup?.Child as ContentControl;
            if (popupChild == null)
            {
                Close();
                return;
            }

            var hitElement = e.OriginalSource as DependencyObject;

            // 点击的是触发元素（ShowBelow 的目标），不自动关闭——交由调用方处理 toggle
            if (_triggerElement != null && hitElement != null &&
                IsVisualChildOf(_triggerElement, hitElement))
                return;

            if (!IsVisualChildOf(popupChild, hitElement))
                Close();
        }

        private static bool IsVisualChildOf(DependencyObject? parent, DependencyObject? child)
        {
            if (parent == null || child == null) return false;
            var current = EnsureVisual(child);
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        /// <summary>
        /// 确保对象是 Visual。对于非 Visual 对象（如 Run 等 TextElement），
        /// 沿逻辑树向上查找第一个 Visual 祖先。
        /// </summary>
        private static DependencyObject? EnsureVisual(DependencyObject? obj)
        {
            if (obj == null) return null;
            if (obj is Visual) return obj;
            while (obj != null && !(obj is Visual))
                obj = LogicalTreeHelper.GetParent(obj);
            return obj;
        }

        private void OnOwnerDeactivated(object? sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// 菜单弹出时拦截鼠标滚轮，防止后方记录区滚动
        /// </summary>
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
        }
    }
}
