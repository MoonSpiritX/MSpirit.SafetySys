// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Serilog;

namespace MS_SafetyManagementSystem.Peoplemanagement
{
    public partial class PersonnelRecordCardView : UserControl
    {
        #region 自动保存

        private void SetupAutoSave(PersonnelRecord record)
        {
            if (_autoSaveRecord != null)
            {
                _autoSaveRecord.PropertyChanged -= OnRecordPropertyChanged;
                _autoSaveRecord = null;
            }
            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Tick -= OnAutoSaveTimerTick;
                _autoSaveTimer.Stop();
                _autoSaveTimer = null;
            }

            _autoSaveRecord = record;
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AutoSaveDelayMilliseconds)
            };
            _autoSaveTimer.Tick += OnAutoSaveTimerTick;

            record.PropertyChanged += OnRecordPropertyChanged;
        }

        private void OnRecordPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_suppressAutoSave) return;

            if (e.PropertyName is nameof(PersonnelRecord.IsSelected)
                or nameof(PersonnelRecord.YearMonth)
                or nameof(PersonnelRecord.UpdatedAt)
                or nameof(PersonnelRecord.DisplayIndex)
                or nameof(PersonnelRecord.Id)
                or nameof(PersonnelRecord.GroupId)
                or nameof(PersonnelRecord.CreatedAt))
                return;

            _autoSaveTimer?.Stop();
            _autoSaveTimer?.Start();

            if (e.PropertyName is nameof(PersonnelRecord.ExitTime)
                or nameof(PersonnelRecord.EntryTime)
                or nameof(PersonnelRecord.EduTime)
                or nameof(PersonnelRecord.IDCardStart)
                or nameof(PersonnelRecord.IDCardEnd)
                or nameof(PersonnelRecord.CertStart)
                or nameof(PersonnelRecord.CertEnd))
                ScheduleDateFormat();
        }

        private void OnAutoSaveTimerTick(object? sender, EventArgs e)
        {
            _autoSaveTimer?.Stop();
            Log.Information("AutoSaveTimer Tick: 触发保存, Record={Rec}", Record?.Id);
            SaveRecord();
        }

        #endregion

        #region 日期格式更正

        private void ScheduleDateFormat()
        {
            if (_dateFormatTimer == null)
            {
                _dateFormatTimer = new DispatcherTimer();
                _dateFormatTimer.Interval = TimeSpan.FromSeconds(3);
                _dateFormatTimer.Tick += (s, e) =>
                {
                    _dateFormatTimer?.Stop();
                    FormatDateFields();
                };
            }
            _dateFormatTimer.Stop();
            _dateFormatTimer.Start();
        }

        private void FormatDateFields()
        {
            if (Record == null) return;

            _isLoadingCertSlot = true;
            _suppressAutoSave = true;
            try
            {
                FormatDateField(Record.ExitTime, formatted =>
                {
                    Record.ExitTime = formatted;
                    ExitTimeTextBox.Text = formatted;
                });

                FormatDateField(Record.EntryTime, formatted =>
                {
                    Record.EntryTime = formatted;
                    EntryTimeTextBox.Text = formatted;
                });

                FormatDateField(Record.EduTime, formatted =>
                {
                    Record.EduTime = formatted;
                    EduTimeTextBox.Text = formatted;
                });

                FormatValidityField(Record.IDCardStart, formatted =>
                {
                    Record.IDCardStart = formatted;
                    IDCardStartTextBox.Text = formatted;
                });

                FormatValidityField(Record.IDCardEnd, formatted =>
                {
                    Record.IDCardEnd = formatted;
                    IDCardEndTextBox.Text = formatted;
                });

                FormatValidityFieldTextBox(CertStartTextBox);
                FormatValidityFieldTextBox(CertEndTextBox);
                SaveCurrentCertSlotText();
            }
            finally
            {
                _suppressAutoSave = false;
                _isLoadingCertSlot = false;
            }
        }

        private void FormatDateField(string? currentValue, Action<string> setFormatted)
        {
            var (success, formatted, _) = DateParser.TryParse(currentValue);
            if (success && formatted != null && formatted != currentValue)
                setFormatted(formatted);
        }

        private void FormatValidityField(string? currentValue, Action<string> setFormatted)
        {
            var (success, formatted) = DateParser.TryParseValidityDate(currentValue);
            if (success && formatted != null && formatted != currentValue)
                setFormatted(formatted);
        }

        private void FormatValidityFieldTextBox(TextBox textBox)
        {
            var (success, formatted) = DateParser.TryParseValidityDate(textBox.Text);
            if (success && formatted != null && formatted != textBox.Text)
                textBox.Text = formatted;
        }

        #endregion
    }
}
