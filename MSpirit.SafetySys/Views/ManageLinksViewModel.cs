// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.Views;

namespace MS_SafetyManagementSystem.Views
{
    public class ManageLinksViewModel : INotifyPropertyChanged
    {
        private readonly FavoriteLinksService _linksService;
        private readonly Func<Window> _getOwnerWindow;   // 用于获取所有者窗口
        private string _searchText = "";
        private LinkCategory? _selectedCategory;
        private LinkItem? _selectedLink;
        private ObservableCollection<LinkCategory> _categories = new();
        private ObservableCollection<LinkItem> _filteredLinks = new();
        private string _footerText = "";

        public ICommand EditLinkCommand { get; }

        public ManageLinksViewModel(FavoriteLinksService linksService, Func<Window> getOwnerWindow)
        {
            _linksService = linksService;
            _getOwnerWindow = getOwnerWindow;
            LoadCategories();
            OpenLinkCommand = new RelayCommand(ExecuteOpenLink, () => SelectedLink != null);
            NewLinkCommand = new RelayCommand(ExecuteNewLink);
            DeleteLinkCommand = new RelayCommand(ExecuteDeleteLink, () => SelectedLink != null);
            TopCommand = new RelayCommand(ExecuteTop, () => SelectedLink != null && SelectedCategory != null);
            UpCommand = new RelayCommand(ExecuteUp, () => SelectedLink != null && SelectedCategory != null);
            DownCommand = new RelayCommand(ExecuteDown, () => SelectedLink != null && SelectedCategory != null);
            BottomCommand = new RelayCommand(ExecuteBottom, () => SelectedLink != null && SelectedCategory != null);
            CloseCommand = new RelayCommand(ExecuteClose);
            EditLinkCommand = new RelayCommand(ExecuteEditLink, () => SelectedLink != null);
        }

        public ObservableCollection<LinkCategory> Categories
        {
            get => _categories;
            set { _categories = value; OnPropertyChanged(); }
        }

        public ObservableCollection<LinkItem> FilteredLinks
        {
            get => _filteredLinks;
            set { _filteredLinks = value; OnPropertyChanged(); }
        }

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); FilterLinks(); }
        }

        public LinkCategory? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    FilterLinks();
                    UpdateFooterText();
                }
            }
        }

        public LinkItem? SelectedLink
        {
            get => _selectedLink;
            set
            {
                if (SetProperty(ref _selectedLink, value))
                {
                    ((RelayCommand)OpenLinkCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)DeleteLinkCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)TopCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)UpCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)DownCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)BottomCommand).NotifyCanExecuteChanged();
                    ((RelayCommand)EditLinkCommand).NotifyCanExecuteChanged();
                }
            }
        }

        public string FooterText
        {
            get => _footerText;
            set => SetProperty(ref _footerText, value);
        }

        public ICommand OpenLinkCommand { get; }
        public ICommand NewLinkCommand { get; }
        public ICommand DeleteLinkCommand { get; }
        public ICommand TopCommand { get; }
        public ICommand UpCommand { get; }
        public ICommand DownCommand { get; }
        public ICommand BottomCommand { get; }
        public ICommand CloseCommand { get; }

        private void LoadCategories()
        {
            Categories.Clear();
            var cats = _linksService.GetAllCategories();
            foreach (var cat in cats)
                Categories.Add(cat);
            if (Categories.Count > 0)
                SelectedCategory = Categories[0];
            else
                UpdateFooterText();
        }

        private void FilterLinks()
        {
            if (SelectedCategory == null)
            {
                FilteredLinks.Clear();
                return;
            }
            var links = _linksService.GetLinksByCategoryId(SelectedCategory.Id);
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                FilteredLinks = new ObservableCollection<LinkItem>(links);
            }
            else
            {
                var filtered = links.Where(l => l.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                                                l.Url.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                                    .ToList();
                FilteredLinks = new ObservableCollection<LinkItem>(filtered);
            }
        }

        private void UpdateFooterText()
        {
            if (SelectedCategory == null)
            {
                FooterText = "";
                return;
            }

            switch (SelectedCategory.Name)
            {
                case "政府信息官网":
                    FooterText = "本页面仅提供公开的政府官方网站链接，方便您快速访问。请遵守相关法律法规，合理使用信息。";
                    break;
                case "外部AI官网":
                    FooterText = "本页面链接为第三方AI服务平台，本程序仅作网址导航，不提供任何内置AI服务。使用前请阅读各平台的服务协议及隐私政策。";
                    break;
                case "自定义网址":
                    FooterText = "可自行添加常用网站。添加的网址仅保存在本地，请自行核实安全性。";
                    break;
                default:
                    FooterText = "";
                    break;
            }
        }

        private void ExecuteOpenLink()
        {
            if (SelectedLink == null) return;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(SelectedLink.Url) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"无法打开网址：{ex.Message}", "错误");
            }
        }

        private void ExecuteNewLink()
        {
            if (SelectedCategory == null) return;
            var dialog = new EditLinkDialog("新建网址");
            dialog.Owner = _getOwnerWindow();   // 使用当前窗口作为所有者
            if (dialog.ShowDialog() == true)
            {
                _linksService.AddLink(SelectedCategory.Id, dialog.ResultName, dialog.ResultUrl);
                FilterLinks();
            }
        }

        private void ExecuteDeleteLink()
        {
            if (SelectedLink == null || SelectedCategory == null) return;
            var confirmDialog = new ConfirmDialog($"确定要删除网址“{SelectedLink.Name}”吗？", "确认删除");
            confirmDialog.Owner = _getOwnerWindow();   // 也可改为当前窗口，保持一致性
            if (confirmDialog.ShowDialog() == true)
            {
                _linksService.DeleteLink(SelectedLink.Id);
                FilterLinks();
            }
        }

        private void ExecuteTop()
        {
            if (SelectedLink == null || SelectedCategory == null) return;
            _linksService.MoveToTop(SelectedCategory.Id, SelectedLink.Id);
            FilterLinks();
            SelectedLink = FilteredLinks.FirstOrDefault(l => l.Id == SelectedLink.Id);
        }

        private void ExecuteUp()
        {
            if (SelectedLink == null || SelectedCategory == null) return;
            _linksService.MoveLinkUp(SelectedCategory.Id, SelectedLink.Id);
            FilterLinks();
            SelectedLink = FilteredLinks.FirstOrDefault(l => l.Id == SelectedLink.Id);
        }

        private void ExecuteDown()
        {
            if (SelectedLink == null || SelectedCategory == null) return;
            _linksService.MoveLinkDown(SelectedCategory.Id, SelectedLink.Id);
            FilterLinks();
            SelectedLink = FilteredLinks.FirstOrDefault(l => l.Id == SelectedLink.Id);
        }

        private void ExecuteBottom()
        {
            if (SelectedLink == null || SelectedCategory == null) return;
            _linksService.MoveToBottom(SelectedCategory.Id, SelectedLink.Id);
            FilterLinks();
            SelectedLink = FilteredLinks.FirstOrDefault(l => l.Id == SelectedLink.Id);
        }

        private void ExecuteClose()
        {
            var window = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
            window?.Close();
        }

        public void SaveEditedLink(LinkItem link)
        {
            _linksService.UpdateLink(link);
            FilterLinks();
        }

        private void ExecuteEditLink()
        {
            if (SelectedLink == null || SelectedCategory == null) return;
            var dialog = new EditLinkDialog("编辑网址", SelectedLink.Name, SelectedLink.Url);
            dialog.Owner = _getOwnerWindow();
            if (dialog.ShowDialog() == true)
            {
                SelectedLink.Name = dialog.ResultName;
                SelectedLink.Url = dialog.ResultUrl;
                _linksService.UpdateLink(SelectedLink);
                FilterLinks();
                SelectedLink = FilteredLinks.FirstOrDefault(l => l.Id == SelectedLink.Id);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}