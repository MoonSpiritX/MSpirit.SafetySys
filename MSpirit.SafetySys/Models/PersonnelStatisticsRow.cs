// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;

namespace MS_SafetyManagementSystem.Models
{
    /// <summary>
    /// 单位人员信息统计数据表行数据模型
    /// </summary>
    public class PersonnelStatisticsRow
    {
        /// <summary>记录 ID，双击跳转到记录卡时使用</summary>
        public string RecordId { get; set; } = "";
        /// <summary>分组 ID（部门/月份分组），双击跳转到树节点时使用</summary>
        public string GroupId { get; set; } = "";

        public int RowNumber { get; set; }
        public string Title { get; set; } = "";
        /// <summary>所属单位名称（根节点）</summary>
        public string Unit { get; set; } = "";
        /// <summary>所属部门名称</summary>
        public string Department { get; set; } = "";
        public string Gender { get; set; } = "";
        public string Position { get; set; } = "";
        private string _birthDate = "";
        public string BirthDate
        {
            get => _birthDate;
            set { _birthDate = value ?? ""; }
        }

        /// <summary>年龄（从出生年月自动计算）</summary>
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
            set { } // 兼容旧代码，值自动被忽略
        }
        public string EmployeeID { get; set; } = "";
        public string NativePlace { get; set; } = "";
        public string EducationLevel { get; set; } = "";
        public string PhoneNumber { get; set; } = "";

        public string EntryTime { get; set; } = "";
        public string EduTime { get; set; } = "";
        public string ExitTime { get; set; } = "";

        public string IDCardDocNumber { get; set; } = "";
        /// <summary>身份证有效期（格式：yyyy-MM-dd 至 yyyy-MM-dd）</summary>
        public string IDCardValidPeriod { get; set; } = "";

        public string CertName { get; set; } = "";
        public string CertNumber { get; set; } = "";
        /// <summary>证书有效期（格式：yyyy-MM-dd 至 yyyy-MM-dd / 长期）</summary>
        public string CertValidPeriod { get; set; } = "";

        /// <summary>三类人员（是/否）</summary>
        public string ThreeCategoryPersonnelDisplay { get; set; } = "";
        /// <summary>特种作业（是/否）</summary>
        public string SpecialOperationsPersonnelDisplay { get; set; } = "";

        /// <summary>备注</summary>
        public string Remark { get; set; } = "";

        // ===== 显示用计算属性（带打码） =====

        /// <summary>身份证号（显示用，中间2个*打码）</summary>
        public string IDCardDisplay => MaskString(IDCardDocNumber, 4, 4, "**");

        /// <summary>手机号（显示用，仅保留首位+末4位）</summary>
        public string PhoneDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(PhoneNumber)) return "";
                if (PhoneNumber.Length <= 5) return PhoneNumber;
                return PhoneNumber[..1] + "**" + PhoneNumber[^4..];
            }
        }

        private static string MaskString(string value, int prefixLen, int suffixLen, string mask = "****")
        {
            if (string.IsNullOrEmpty(value)) return "";
            int len = value.Length;
            if (len <= prefixLen + suffixLen + 1) return value;
            return value[..prefixLen] + mask + value[^suffixLen..];
        }
    }
}
