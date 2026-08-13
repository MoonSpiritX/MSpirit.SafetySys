// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Wpf.Ui.Controls;
using System.Windows.Media;

namespace MS_SafetyManagementSystem.Views
{
    /// <summary>
    /// 关于软件窗口：左侧选项（关于软件 / 作者信息），右侧内容区显示对应内容；
    /// 「作者信息」栏附带可复制联系方式（邮箱/GitHub/抖音用户/群号）与抖音交流群二维码。
    /// </summary>
    public partial class AboutWindow : FluentWindow
    {
        public AboutWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                AboutListBox.SelectedIndex = 0; // 默认选中「关于软件」
                ContentScrollViewer.ScrollToTop();
            };
        }

        private void AboutListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AboutContentText == null || AuthorInfoPanel == null || ChangelogPanel == null) return;

            bool isAuthor = AboutListBox.SelectedItem == AuthorItem;
            bool isChangelog = AboutListBox.SelectedItem == ChangelogItem;

            if (isAuthor)
            {
                AboutContentText.Visibility = Visibility.Collapsed;
                ChangelogPanel.Visibility = Visibility.Collapsed;
                AuthorInfoPanel.Visibility = Visibility.Visible;
            }
            else if (isChangelog)
            {
                ShowChangelog();
                ChangelogPanel.Visibility = Visibility.Visible;
                AboutContentText.Visibility = Visibility.Collapsed;
                AuthorInfoPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                AboutContentText.Text = SoftwareGuide;
                AboutContentText.Visibility = Visibility.Visible;
                ChangelogPanel.Visibility = Visibility.Collapsed;
                AuthorInfoPanel.Visibility = Visibility.Collapsed;
            }

            ContentScrollViewer?.ScrollToTop();
        }

        /// <summary>
        /// 复制按钮通用事件：复制按钮 Tag 中保存的文本到剪贴板，并给出提示。
        /// </summary>
        private void CopyText_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string text && !string.IsNullOrEmpty(text))
            {
                try
                {
                    Clipboard.SetText(text);
                    ToastService.Show("已复制到剪贴板", this);
                }
                catch (Exception ex)
                {
                    ToastService.Show($"复制失败：{ex.Message}", this);
                }
            }
        }

        /// <summary>
        /// GitHub 超链接：用默认浏览器打开。
        /// </summary>
        private void OpenLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                ToastService.Show($"打开链接失败：{ex.Message}", this);
            }
        }

        // ========== 各选项内容 ==========

        private static readonly string SoftwareGuide = BuildSoftwareGuide();

        /// <summary>
        /// 更新日志数据（用户视角，依据 CHANGELOG.md 转译，简明列出近几个版本的功能变更）。
        /// 注意：此内容面向最终用户，避免开发者术语（类名/文件名/依赖包名）；发版时需与 CHANGELOG.md 同步更新。
        /// </summary>
        private static readonly (string Title, string Date, string[] Lines)[] ChangelogData =
        {
            ("V1.1.0.0", "2026-08-12", new[]
            {
                "新增：手机图片传输——手机扫码、原图上传，按拍摄日期自动归档到电脑对应文件夹",
                "新增：托盘图标右键多级菜单（办公工具 / 信息官网 / 外部AI / 快捷网址），主窗口隐藏时也能快捷使用",
                "新增：设置菜单「关闭程序」（可最小化到托盘或完全退出）",
                "新增：各窗口内容区拖拽平移（图片处理 / 手机图片传输 / 使用提示 / 关于软件 / 项目及存档管理等）",
                "新增：图片方向转换空列表提示、重复执行时状态自动复位",
                "优化：图片方向转换批量导入速度（大文件夹快速加载、重复文件去重提速）",
                "优化：程序退出统一流程（托盘退出不再导致可能的丢失正在编辑的内容）",
                "修复：多个局域网服务同时开启时的端口冲突（自动分配）",
                "修复：手机端页面缓存不更新、上传完成提示不显示",
                "修复：图片方向转换窗口水印导致程序启动崩溃",
                "修复：常用隐患类型管理窗口点击类型项误触发窗口拖动"
            }),
            ("V1.0.1.0", "2026-08-11", new[]
            {
                "新增：使用提示窗口、关于软件窗口",
                "新增：数据自动备份（启动/退出时自动备份，最多保留最近 30 天）",
                "新增：更新包制作工具（全量包 / 增量包）",
                "修复：全新电脑首次安装时新建项目数据失败",
                "修复：人员模块双击图片的打开方式与设置不一致"
            }),
            ("V1.0.0.0（首个内测版）", "2026-08-11", new[]
            {
                "首个内测版本：隐患管理、人员管理、图片查看器、智能导入导出、统计分析等核心功能。"
            })
        };

        /// <summary>
        /// 构建「更新日志」面板：版本标题 = 勾选状态复选框（样式与常用隐患类型管理窗口一致，纯展示不可点）+ 版本号 + 版本日期；
        /// 内容行无前缀符号。每次切换选项时重建，保证新增版本后无需手工刷新。
        /// </summary>
        private void ShowChangelog()
        {
            ChangelogPanel.Children.Clear();

            // 11pt 的 DIU 值（WPF 中 XAML 的 "11pt" = 11 * 96/72 像素）
            double fontSize = 11 * 96.0 / 72.0;

            for (int i = 0; i < ChangelogData.Length; i++)
            {
                var (title, date, lines) = ChangelogData[i];

                // 版本标题行：复选框（始终勾选、不可点击更改）+ 版本号文本
                var header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 0)
                };
                header.Children.Add(new CheckBox
                {
                    IsChecked = true,          // 始终选中
                    IsHitTestVisible = false,  // 纯展示，不可点击更改（与常用隐患类型窗口一致）
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 0),
                    FontSize = fontSize
                });
                header.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = title,
                    FontSize = fontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                });
                // 版本日期（灰色、常规字重，跟在版本号后面；与 CHANGELOG.md 标题行日期一致）
                if (!string.IsNullOrEmpty(date))
                {
                    header.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = $"（{date}）",
                        FontSize = fontSize,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    });
                }
                ChangelogPanel.Children.Add(header);

                // 内容行（缩进与版本号文本对齐）
                foreach (string line in lines)
                {
                    ChangelogPanel.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = line,
                        FontSize = fontSize,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 26,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                        Margin = new Thickness(30, 0, 0, 0)
                    });
                }
            }
        }

        /// <summary>
        /// 构建「关于软件」内容；版本号动态读取程序集版本，避免与 csproj 版本不同步。
        /// </summary>
        private static string BuildSoftwareGuide()
        {
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
            return
            "【关于软件】\n\n" +
            "一、软件信息\n" +
            "  · 名称：斜月安全管理系统（个人版 Beta）\n" +
            "  · 特点：现代界面及交互方式；极具便携性的数据录入、导入、导出；智能匹配导入与导出模版；一次录入，多次使用；\n" +
            "  · 版本：V" + version + "\n" +
            "  · 作者：斜月Spirit（AXin）\n" +
            "  · 开源协议：GNU Affero General Public License v3.0（AGPL-3.0-or-later）\n" +
            "  · 运行环境：Windows 10 1809 及以上\n\n" +
            "二、技术栈\n" +
            "  · 框架：.NET 10 + WPF\n" +
            "  · UI：Fluent Design 风格\n" +
            "  · OCR：PaddleOCR本地模型（身份证识别、手机录入）\n" +
            "  · 局域网服务：Kestrel HTTP\n" +
            "  · 数据目录：默认位置“非系统盘的 MSpirit.SafetySys.Date 文件夹”\n" +
            "  · 整体移动软件程序目录、版本更新、替换不影响已有的数据\n\n" +
            "三、功能模块\n" +
            "  · 隐患管理：安全巡查隐患记录、图片附件、状态跟踪、月度统计\n" +
            "  · 人员管理：人员档案、证书管理、局域网批量录入\n" +
            "  · 图片查看器：缩放、旋转、翻转、幻灯片、全屏\n" +
            "  · 智能导入导出：Word/Excel 模板字段智能匹配\n" +
            "  · 统计分析：隐患类型分布、人员统计图表\n\n" +
            "四、相关文档\n" +
            "  · 发布包内附《使用说明》（PDF）；\n" +
            "  · 各模块详细操作说明见菜单「设置 → 使用提示」。\n\n" +
            "五、反馈与交流\n" +
            "  使用过程中遇到问题、想提交 Bug 或功能建议，\n" +
            "  欢迎通过左侧「作者信息」中的联系方式反馈。";
        }
    }
}
