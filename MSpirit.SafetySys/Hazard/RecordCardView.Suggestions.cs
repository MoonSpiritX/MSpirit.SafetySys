// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Controls;
using MS_SafetyManagementSystem.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MS_SafetyManagementSystem.Hazard
{
    public partial class RecordCardView : UserControl
    {
        #region 责任单位/责任人 搜索建议

        private const int SuggestionDebounceMilliseconds = 150;
        private const int SuggestionMaxItems = 50;

        private DropdownMenu? _activeSuggestionMenu;
        private TextBox? _suggestionTriggerTextBox;
        private DispatcherTimer? _suggestionDebounceTimer;
        private ProjectDataService? _suggestionDataService;
        private List<string> _unitNames = new();
        private List<PersonnelSuggestion> _personnelSuggestions = new();
        private bool _suppressSuggestion;

        private sealed class PersonnelSuggestion
        {
            public string Name { get; }
            public string Unit { get; }

            public PersonnelSuggestion(string name, string unit)
            {
                Name = name;
                Unit = unit;
            }
        }

        private void AttachSuggestionTextChangedEvents()
        {
            var unitBox = this.FindName("ResponsibleUnitTextBox") as TextBox;
            var personBox = this.FindName("ResponsiblePersonTextBox") as TextBox;
            var reviewerBox = this.FindName("ReviewerTextBox") as TextBox;
            if (unitBox != null) unitBox.TextChanged += OnSuggestionTextChanged;
            if (personBox != null) personBox.TextChanged += OnSuggestionTextChanged;
            if (reviewerBox != null) reviewerBox.TextChanged += OnSuggestionTextChanged;
        }

        private void DetachSuggestionTextChangedEvents()
        {
            var unitBox = this.FindName("ResponsibleUnitTextBox") as TextBox;
            var personBox = this.FindName("ResponsiblePersonTextBox") as TextBox;
            var reviewerBox = this.FindName("ReviewerTextBox") as TextBox;
            if (unitBox != null) unitBox.TextChanged -= OnSuggestionTextChanged;
            if (personBox != null) personBox.TextChanged -= OnSuggestionTextChanged;
            if (reviewerBox != null) reviewerBox.TextChanged -= OnSuggestionTextChanged;
        }

        private void OnSuggestionTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSuggestion)
            {
                _suppressSuggestion = false;
                return;
            }

            if (sender is not TextBox textBox) return;
            if (textBox.Name != "ResponsibleUnitTextBox" &&
                textBox.Name != "ResponsiblePersonTextBox" &&
                textBox.Name != "ReviewerTextBox") return;
            if (!textBox.IsKeyboardFocused) return;   // 仅用户输入时弹出，绑定赋值不弹

            _suggestionTriggerTextBox = textBox;
            if (_suggestionDebounceTimer == null)
            {
                _suggestionDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SuggestionDebounceMilliseconds) };
                _suggestionDebounceTimer.Tick += OnSuggestionDebounceTick;
            }
            _suggestionDebounceTimer.Stop();
            _suggestionDebounceTimer.Start();
        }

        private void OnSuggestionDebounceTick(object? sender, EventArgs e)
        {
            _suggestionDebounceTimer?.Stop();
            UpdateSuggestionList();
        }

        private void UpdateSuggestionList()
        {
            var textBox = _suggestionTriggerTextBox;
            if (textBox == null || Record == null) return;

            string keyword = textBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(keyword))
            {
                CloseSuggestionMenu();
                return;
            }

            if (!EnsurePersonnelDataLoaded()) return;

            List<string> candidates = textBox.Name == "ResponsibleUnitTextBox"
                ? _unitNames
                    .Where(u => u.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : _personnelSuggestions
                    .Where(p => p.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .Select(p => string.IsNullOrEmpty(p.Unit) ? p.Name : $"{p.Name}（{p.Unit}）")
                    .ToList();

            if (candidates.Count == 0)
            {
                CloseSuggestionMenu();
                return;
            }

            var menu = _activeSuggestionMenu;
            if (menu == null)
            {
                menu = new DropdownMenu();
                menu.Closed += () => _activeSuggestionMenu = null;
            }
            else
            {
                menu.Close();
            }

            menu.ClearItems();
            foreach (string candidate in candidates.Take(SuggestionMaxItems))
            {
                string value = candidate;
                menu.AddSuggestionItem(value, () => ApplySuggestion(textBox, value));
            }

            _activeSuggestionMenu = menu;
            menu.ShowBelow(textBox);
        }

        private bool EnsurePersonnelDataLoaded()
        {
            var dataService = GetProjectDataService();
            if (dataService == null) return false;
            if (_suggestionDataService == dataService && (_unitNames.Count > 0 || _personnelSuggestions.Count > 0))
                return true;

            var allGroups = dataService.GetAllGroups(ModuleNames.Personnel);
            _unitNames = allGroups
                .Where(g => string.IsNullOrEmpty(g.ParentId))
                .OrderBy(g => g.Order)
                .Select(g => g.Name)
                .ToList();

            var groupById = allGroups.ToDictionary(g => g.Id, g => g);
            var unitNameByGroupId = new Dictionary<string, string>();
            foreach (var group in allGroups)
            {
                var current = group;
                var visited = new HashSet<string>();
                while (!string.IsNullOrEmpty(current.ParentId) && visited.Add(current.Id))
                {
                    if (!groupById.TryGetValue(current.ParentId, out var parent)) break;
                    current = parent;
                }
                unitNameByGroupId[group.Id] = current.Name;
            }

            _personnelSuggestions = dataService.GetAllPersonnelRecords()
                .Where(r => string.IsNullOrWhiteSpace(r.ExitTime))   // 仅在场人员
                .Select(r => new PersonnelSuggestion(
                    r.Title,
                    unitNameByGroupId.TryGetValue(r.GroupId, out var unit) ? unit : ""))
                .ToList();

            _suggestionDataService = dataService;
            return true;
        }

        private void ApplySuggestion(TextBox textBox, string displayValue)
        {
            if (Record == null) return;

            string value = displayValue;
            if (textBox.Name == "ResponsiblePersonTextBox" || textBox.Name == "ReviewerTextBox")
            {
                int left = displayValue.IndexOf('（');
                if (left > 0) value = displayValue.Substring(0, left).Trim();
            }

            _suppressSuggestion = true;
            CloseSuggestionMenu();
            textBox.Text = value;

            if (textBox.Name == "ResponsiblePersonTextBox")
            {
                var suggestion = _personnelSuggestions.FirstOrDefault(p => p.Name == value);
                if (suggestion != null && !string.IsNullOrEmpty(suggestion.Unit) &&
                    !string.Equals(Record.ResponsibleUnit, suggestion.Unit, StringComparison.Ordinal))
                {
                    // 联动：选择责任人后自动同步其所属单位
                    Record.ResponsibleUnit = suggestion.Unit;
                }
            }

            SaveRecord();
        }

        private void CloseSuggestionMenu()
        {
            _suggestionDebounceTimer?.Stop();
            if (_activeSuggestionMenu != null)
            {
                _activeSuggestionMenu.Close();
                _activeSuggestionMenu = null;
            }
            _suggestionTriggerTextBox = null;
        }

        private ProjectDataService? GetProjectDataService()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            return mainWindow?.ViewModel.GetProjectDataService();
        }

        #endregion
    }
}
