// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MS_SafetyManagementSystem.Models
{
    public class Record : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _yearMonth;
        /// <summary>
        /// 年月分组，格式如 "2026-05"，用于物理文件夹组织
        /// </summary>
        public string? YearMonth
        {
            get => _yearMonth;
            set { _yearMonth = value; OnPropertyChanged(); }
        }

        public string Id { get; set; } = "";
        public string GroupId { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Content { get; set; }
        public string CreatedAt { get; set; } = "";
        public string? UpdatedAt { get; set; }
        public string? DueDate { get; set; }
        public string? Status { get; set; }
        public string? ExtraData { get; set; }
        public int DisplayIndex { get; set; }
        private string _responsibleUnit = "";
        public string ResponsibleUnit
        {
            get => _responsibleUnit;
            set
            {
                if (_responsibleUnit != value)
                {
                    _responsibleUnit = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _responsiblePerson = "";
        public string ResponsiblePerson
        {
            get => _responsiblePerson;
            set
            {
                if (_responsiblePerson != value)
                {
                    _responsiblePerson = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _location = "";
        public string Location
        {
            get => _location;
            set
            {
                if (_location != value)
                {
                    _location = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _hazardType = "";
        public string HazardType
        {
            get => _hazardType;
            set
            {
                if (_hazardType != value)
                {
                    _hazardType = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _hazardLevel = "";
        public string HazardLevel
        {
            get => _hazardLevel;
            set
            {
                if (_hazardLevel != value)
                {
                    _hazardLevel = value;
                    OnPropertyChanged();
                }
            }
        }

        private DateTime? _deadline;
        public DateTime? Deadline
        {
            get => _deadline;
            set
            {
                if (_deadline != value)
                {
                    _deadline = value;
                    OnPropertyChanged();
                }
            }
        }

        private DateTime? _completedDate;
        public DateTime? CompletedDate
        {
            get => _completedDate;
            set
            {
                if (_completedDate != value)
                {
                    _completedDate = value;
                    OnPropertyChanged();
                }
            }
        }
        private string _reviewer = "";
        public string Reviewer
        {
            get => _reviewer;
            set
            {
                if (_reviewer != value)
                {
                    _reviewer = value;
                    OnPropertyChanged();
                }
            }
        }
        public string Description { get; set; } = "";
        public string Measure { get; set; } = "";

        public Record Clone()
        {
            return new Record
            {
                Id = this.Id,
                GroupId = this.GroupId,
                Title = this.Title,
                Content = this.Content,
                CreatedAt = this.CreatedAt,
                UpdatedAt = this.UpdatedAt,
                DueDate = this.DueDate,
                Status = this.Status,
                ExtraData = this.ExtraData,
                DisplayIndex = this.DisplayIndex,
                ResponsibleUnit = this.ResponsibleUnit,
                ResponsiblePerson = this.ResponsiblePerson,
                Location = this.Location,
                HazardType = this.HazardType,
                HazardLevel = this.HazardLevel,
                Deadline = this.Deadline,
                CompletedDate = this.CompletedDate,
                Reviewer = this.Reviewer,
                Description = this.Description,
                Measure = this.Measure,
                YearMonth = this.YearMonth
            };
        }
    }
}
