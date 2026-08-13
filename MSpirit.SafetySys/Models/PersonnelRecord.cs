// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MS_SafetyManagementSystem.Models
{
    /// <summary>
    /// 人员管理模块专用实体类（自包含，不依赖 Record 表）
    /// 与 Records 表无关联，直接通过 GroupId 关联分组树
    /// </summary>
    public class PersonnelRecord : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ====== Record 通用字段（自包含） ======
        private string _id = "";
        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        private string _groupId = "";
        public string GroupId
        {
            get => _groupId;
            set { _groupId = value; OnPropertyChanged(); }
        }

        private string _title = "";
        /// <summary>姓名</summary>
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private string _createdAt = "";
        public string CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        private string? _updatedAt;
        public string? UpdatedAt
        {
            get => _updatedAt;
            set { _updatedAt = value; OnPropertyChanged(); }
        }

        private int _displayIndex;
        public int DisplayIndex
        {
            get => _displayIndex;
            set { _displayIndex = value; OnPropertyChanged(); }
        }

        private string? _yearMonth;
        /// <summary>年月，如 "2026-06"，用于附件存储目录</summary>
        public string? YearMonth
        {
            get => _yearMonth;
            set { _yearMonth = value; OnPropertyChanged(); }
        }

        private bool _isSelected;
        /// <summary>多选（仅 UI 使用，不入库）</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // ====== 人员专有字段 ======
        private string _gender = "";
        public string Gender
        {
            get => _gender;
            set { _gender = value; OnPropertyChanged(); }
        }

        /// <summary>年龄（从出生年月自动计算，设值被忽略）</summary>
        public string Age
        {
            get
            {
                if (string.IsNullOrEmpty(_birthDate)) return "";
                if (DateTime.TryParseExact(_birthDate, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var bd))
                {
                    var today = DateTime.Today;
                    int age = today.Year - bd.Year;
                    if (bd.Date > today.AddYears(-age)) age--;
                    return age >= 0 ? age.ToString() : "";
                }
                return "";
            }
            set => OnPropertyChanged(); // 兼容 Dapper 和旧代码，值自动被忽略
        }

        private string _employeeID = "";
        public string EmployeeID
        {
            get => _employeeID;
            set { _employeeID = value; OnPropertyChanged(); }
        }

        private string _position = "";
        public string Position
        {
            get => _position;
            set { _position = value; OnPropertyChanged(); }
        }

        private string _phoneNumber = "";
        public string PhoneNumber
        {
            get => _phoneNumber;
            set { _phoneNumber = value; OnPropertyChanged(); }
        }

        private string _educationLevel = "";
        /// <summary>文化程度</summary>
        public string EducationLevel
        {
            get => _educationLevel;
            set { _educationLevel = value; OnPropertyChanged(); }
        }

        private string _nativePlace = "";
        /// <summary>籍贯</summary>
        public string NativePlace
        {
            get => _nativePlace;
            set { _nativePlace = value; OnPropertyChanged(); }
        }

        private string _homeAddress = "";
        /// <summary>家庭住址</summary>
        public string HomeAddress
        {
            get => _homeAddress;
            set { _homeAddress = value; OnPropertyChanged(); }
        }

        private string _birthDate = "";
        /// <summary>出生年月</summary>
        public string BirthDate
        {
            get => _birthDate;
            set
            {
                if (_birthDate != value)
                {
                    _birthDate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Age)); // 出生日期变化联动更新年龄
                }
            }
        }

        private string _entryTime = "";
        public string EntryTime
        {
            get => _entryTime;
            set { _entryTime = value; OnPropertyChanged(); }
        }

        private string _eduTime = "";
        public string EduTime
        {
            get => _eduTime;
            set { _eduTime = value; OnPropertyChanged(); }
        }

        private string _exitTime = "";
        public string ExitTime
        {
            get => _exitTime;
            set { _exitTime = value; OnPropertyChanged(); }
        }

        private bool _threeCategoryPersonnel;
        public bool ThreeCategoryPersonnel
        {
            get => _threeCategoryPersonnel;
            set { _threeCategoryPersonnel = value; OnPropertyChanged(); }
        }

        private bool _specialOperationsPersonnel;
        public bool SpecialOperationsPersonnel
        {
            get => _specialOperationsPersonnel;
            set { _specialOperationsPersonnel = value; OnPropertyChanged(); }
        }

        private string _remark = "";
        public string Remark
        {
            get => _remark;
            set { _remark = value; OnPropertyChanged(); }
        }

        // 证件编号
        private string _idCardDocNumber = "";
        public string IDCardDocNumber
        {
            get => _idCardDocNumber;
            set { _idCardDocNumber = value; OnPropertyChanged(); }
        }

        private string _certDocNumber = "";
        public string CertDocNumber
        {
            get => _certDocNumber;
            set { _certDocNumber = value; OnPropertyChanged(); }
        }

        // 有效期
        private string _idCardStart = "";
        public string IDCardStart
        {
            get => _idCardStart;
            set { _idCardStart = value; OnPropertyChanged(); }
        }

        private string _idCardEnd = "";
        public string IDCardEnd
        {
            get => _idCardEnd;
            set { _idCardEnd = value; OnPropertyChanged(); }
        }

        private string _certStart = "";
        public string CertStart
        {
            get => _certStart;
            set { _certStart = value; OnPropertyChanged(); }
        }

        private string _certEnd = "";
        public string CertEnd
        {
            get => _certEnd;
            set { _certEnd = value; OnPropertyChanged(); }
        }

        private string _certName = "";
        /// <summary>证书名称（显示在证件编号后面，如：（建安证书））</summary>
        public string CertName
        {
            get => _certName;
            set { _certName = value; OnPropertyChanged(); }
        }

        // ========== 5 个证书槽位的独立文本字段 ==========
        private string _cert1Name = "";
        public string Cert1Name { get => _cert1Name; set { _cert1Name = value; OnPropertyChanged(); } }
        private string _cert1DocNumber = "";
        public string Cert1DocNumber { get => _cert1DocNumber; set { _cert1DocNumber = value; OnPropertyChanged(); } }
        private string _cert1Start = "";
        public string Cert1Start { get => _cert1Start; set { _cert1Start = value; OnPropertyChanged(); } }
        private string _cert1End = "";
        public string Cert1End { get => _cert1End; set { _cert1End = value; OnPropertyChanged(); } }

        private string _cert2Name = "";
        public string Cert2Name { get => _cert2Name; set { _cert2Name = value; OnPropertyChanged(); } }
        private string _cert2DocNumber = "";
        public string Cert2DocNumber { get => _cert2DocNumber; set { _cert2DocNumber = value; OnPropertyChanged(); } }
        private string _cert2Start = "";
        public string Cert2Start { get => _cert2Start; set { _cert2Start = value; OnPropertyChanged(); } }
        private string _cert2End = "";
        public string Cert2End { get => _cert2End; set { _cert2End = value; OnPropertyChanged(); } }

        private string _cert3Name = "";
        public string Cert3Name { get => _cert3Name; set { _cert3Name = value; OnPropertyChanged(); } }
        private string _cert3DocNumber = "";
        public string Cert3DocNumber { get => _cert3DocNumber; set { _cert3DocNumber = value; OnPropertyChanged(); } }
        private string _cert3Start = "";
        public string Cert3Start { get => _cert3Start; set { _cert3Start = value; OnPropertyChanged(); } }
        private string _cert3End = "";
        public string Cert3End { get => _cert3End; set { _cert3End = value; OnPropertyChanged(); } }

        private string _cert4Name = "";
        public string Cert4Name { get => _cert4Name; set { _cert4Name = value; OnPropertyChanged(); } }
        private string _cert4DocNumber = "";
        public string Cert4DocNumber { get => _cert4DocNumber; set { _cert4DocNumber = value; OnPropertyChanged(); } }
        private string _cert4Start = "";
        public string Cert4Start { get => _cert4Start; set { _cert4Start = value; OnPropertyChanged(); } }
        private string _cert4End = "";
        public string Cert4End { get => _cert4End; set { _cert4End = value; OnPropertyChanged(); } }

        private string _cert5Name = "";
        public string Cert5Name { get => _cert5Name; set { _cert5Name = value; OnPropertyChanged(); } }
        private string _cert5DocNumber = "";
        public string Cert5DocNumber { get => _cert5DocNumber; set { _cert5DocNumber = value; OnPropertyChanged(); } }
        private string _cert5Start = "";
        public string Cert5Start { get => _cert5Start; set { _cert5Start = value; OnPropertyChanged(); } }
        private string _cert5End = "";
        public string Cert5End { get => _cert5End; set { _cert5End = value; OnPropertyChanged(); } }
    }
}
