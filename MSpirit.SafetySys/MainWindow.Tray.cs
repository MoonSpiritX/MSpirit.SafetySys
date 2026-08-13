// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Views;
using System;
using System.Diagnostics;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace MS_SafetyManagementSystem
{
    /// <summary>
    /// MainWindow 分部类：托盘图标事件与托盘右键菜单构建（显示主窗口 / 办公工具 / 信息官网 / 外部AI / 快捷网址 / 退出程序）。
    /// 2026-08-13 从 MainWindow.xaml.cs 拆出，纯搬移、逻辑不变。
    /// </summary>
    public partial class MainWindow
    {
        private void OnNotifyIconMouseClick(object? sender, WinForms.MouseEventArgs e)
        {
            if (e.Button == WinForms.MouseButtons.Right)
            {
                // 每次打开时重建分组（与主菜单链接数据保持同步）
                _trayMenuService.ShowMenu(this, _viewModel.ShowWindowCommand, _viewModel.ExitCommand, BuildTrayMenuGroups());
            }
        }

        /// <summary>
        /// 构建托盘右键菜单分组。办公工具为固定入口；三个链接分组与主菜单共用
        /// FavoriteLinksService 数据（政府信息官网 / 外部AI官网 / 自定义网址分类）。
        /// </summary>
        private List<TrayMenuGroup> BuildTrayMenuGroups()
        {
            var groups = new List<TrayMenuGroup>
            {
                new("办公工具", new List<TrayMenuAction>
                {
                    new("图片处理", OpenImageProcessFromTray),
                    new("手机图片传输", () => OpenPhotoTransferWindow(owned: false))
                })
            };

            var categories = _linksService?.GetAllCategories() ?? new List<LinkCategory>();
            groups.Add(BuildTrayLinkGroup(categories, "政府信息官网", "信息官网", "(暂无内容)"));
            groups.Add(BuildTrayLinkGroup(categories, "外部AI官网", "外部AI", "(暂无内容)"));
            groups.Add(BuildTrayLinkGroup(categories, "自定义网址", "快捷网址", "暂无自定义网址内容"));
            return groups;
        }

        /// <summary>从链接分类构建托盘二级菜单（空分类显示禁用占位项）</summary>
        private static TrayMenuGroup BuildTrayLinkGroup(
            List<LinkCategory> categories, string categoryName, string header, string emptyText)
        {
            var cat = categories.FirstOrDefault(c => c.Name == categoryName);
            var items = new List<TrayMenuAction>();
            if (cat == null || cat.Links.Count == 0)
            {
                items.Add(new TrayMenuAction(emptyText, null, false));
            }
            else
            {
                foreach (var link in cat.Links.OrderBy(l => l.Order))
                {
                    var url = link.Url;
                    items.Add(new TrayMenuAction(link.Name, () => OpenUrlInBrowser(url)));
                }
            }
            return new TrayMenuGroup(header, items);
        }

        /// <summary>托盘打开图片处理：工具窗口独立弹出（不设 Owner），主窗口保持隐藏状态</summary>
        private void OpenImageProcessFromTray()
        {
            OpenImageProcessWindow(owned: false);
        }

        /// <summary>用默认浏览器打开网址（托盘链接菜单用，与主菜单打开逻辑一致）</summary>
        private static void OpenUrlInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // 全限定：MainWindow 同时 using Wpf.Ui.Controls 与 WinForms，MessageBox 有歧义
                System.Windows.MessageBox.Show($"无法打开网址：{ex.Message}", "错误");
            }
        }
    }
}
