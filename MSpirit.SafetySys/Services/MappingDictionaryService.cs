// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MS_SafetyManagementSystem.Services
{
    public class MappingDictionaryService
    {
        // 模块字段归属定义（用于 MatchField 的 module 过滤）
        private static readonly HashSet<string> HazardDataFields = new()
        {
            "Status", "RowNumber", "RecordDate",
            "ResponsibleUnit", "ResponsiblePerson", "Location",
            "HazardType", "HazardLevel",
            "Deadline", "CompletedDate", "Reviewer",
            "Description", "Measure",
            "HazardPhoto", "RemedyPhoto"
        };

        private static readonly HashSet<string> PersonnelDataFields = new()
        {
            "Title", "Gender", "Age",
            "Department", "Position", "Unit",
            "BirthDate", "EmployeeID", "NativePlace", "EducationLevel",
            "PhoneNumber", "EntryTime", "EduTime", "ExitTime",
            "IDCardDocNumber", "IDCardValidPeriod",
            "ThreeCategoryPersonnelDisplay", "SpecialOperationsPersonnelDisplay",
            "CertName", "CertNumber", "CertValidPeriod",
            "Remark"
        };

        private readonly string _filePath;
        private List<FieldMappingInfo> _fieldMappings = new();
        private Dictionary<string, List<string>> _commonSynonyms = new();
        private string _version = "1.0";

        public MappingDictionaryService(string rootPath)
        {
            // 2026-08-10 目录调整：MappingDictionary.json 上移至数据根目录（与其他 json 同层），Data 目录已废弃
            _filePath = Path.Combine(rootPath, "MappingDictionary.json");
            LoadOrCreate();
        }

        private void LoadOrCreate()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    var dict = JsonSerializer.Deserialize<DictionaryData>(json);
                    if (dict != null)
                    {
                        _version = dict.Version;
                        _fieldMappings = dict.FieldMappings ?? new List<FieldMappingInfo>();
                        _commonSynonyms = dict.CommonSynonyms ?? new Dictionary<string, List<string>>();

                        // 版本迁移：1.0 → 1.1（补充人员模块字段映射）
                        if (_version == "1.0")
                        {
                            AppendPersonnelMappings();
                            _version = "1.1";
                            Save();
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "加载 MappingDictionary.json 失败，将创建默认字典");
                }
            }
            CreateDefault();
            Save();
        }

        private void CreateDefault()
        {
            _fieldMappings = new List<FieldMappingInfo>
            {
                // ===== 隐患模块字段 =====
                new FieldMappingInfo { DataField = "Status", StandardName = "进度", Aliases = new List<string> { "状态", "进度", "复查情况", "整改进度", "整改状态", "治理情况", "治理状况",  } },
                new FieldMappingInfo { DataField = "RowNumber", StandardName = "序号", Aliases = new List<string> { "序号", "编号", "隐患编号", "问题编号" } },
                new FieldMappingInfo { DataField = "RecordDate", StandardName = "日期", Aliases = new List<string> { "日期", "检查日期", "记录日期", "问题日期", "隐患日期", "排查时间", "排查日期", "发现日期", "发现时间" } },
                new FieldMappingInfo { DataField = "ResponsibleUnit", StandardName = "责任单位", Aliases = new List<string> { "责任单位", "所属单位", "负责单位", "责任部门", "队伍名称", "分包单位", "分包队伍" } },
                new FieldMappingInfo { DataField = "ResponsiblePerson", StandardName = "责任人", Aliases = new List<string> { "责任人", "负责人", "责任人员", "整改责任人", "整改人", "整改人员" } },
                new FieldMappingInfo { DataField = "Location", StandardName = "隐患位置", Aliases = new List<string> { "隐患位置", "隐患区域", "隐患部位", "区域位置", "隐患存在区域", "隐患存在部位", "隐患存在的区域", "隐患存在的部位", "隐患存在的区域或部位" } },
                new FieldMappingInfo { DataField = "HazardType", StandardName = "隐患类型", Aliases = new List<string> { "隐患类型", "风险类型", "隐患类别", "类别" } },
                new FieldMappingInfo { DataField = "HazardLevel", StandardName = "级别", Aliases = new List<string> { "级别", "隐患等级", "风险等级", "隐患分级" } },
                new FieldMappingInfo { DataField = "Deadline", StandardName = "整改期限", Aliases = new List<string> { "整改期限", "治理期限", "整改时限", "完成期限" } },
                new FieldMappingInfo { DataField = "CompletedDate", StandardName = "完成日期", Aliases = new List<string> { "完成日期", "完成时间", "整改完成日期", "整改时间", "整改完成时间", "复查时间", "复查日期" } },
                new FieldMappingInfo { DataField = "Reviewer", StandardName = "复查人", Aliases = new List<string> { "复查人", "复查人员", "验收人", "验收人员", "复核人", "复核人员", "排查人", "排查人员", "监督人员", "监督整改人员", "监督责任人" } },
                new FieldMappingInfo { DataField = "Description", StandardName = "隐患描述", Aliases = new List<string> { "隐患描述", "问题描述", "隐患情况", "隐患内容", "存在问题", "问题情况", "隐患问题描述", "隐患状态描述", "隐患情况描述", "隐患名称", "隐患措施" } },
                new FieldMappingInfo { DataField = "Measure", StandardName = "整改措施", Aliases = new List<string> { "整改措施", "主要整改措施", "治理措施", "主要治理措施", "整改要求", "处理措施", "整改情况", "整改回复", "回复描述" } },
                new FieldMappingInfo { DataField = "HazardPhoto", StandardName = "隐患照片", Aliases = new List<string> { "隐患照片", "隐患图片", "问题照片", "问题图片", "整改前照片", "整改前图片" } },
                new FieldMappingInfo { DataField = "RemedyPhoto", StandardName = "整改照片", Aliases = new List<string> { "整改照片", "整改图片", "整改后照片", "整改后图片", "完成照片", "整改完成照片" } },

                // ===== 人员模块字段 =====
                new FieldMappingInfo { DataField = "Title", StandardName = "姓名", Aliases = new List<string> { "姓名", "名字", "人员姓名" } },
                new FieldMappingInfo { DataField = "Gender", StandardName = "性别", Aliases = new List<string> { "性别" } },
                new FieldMappingInfo { DataField = "Age", StandardName = "年龄", Aliases = new List<string> { "年龄" } },
                new FieldMappingInfo { DataField = "Department", StandardName = "部门", Aliases = new List<string> { "部门", "班组", "施工班组", "作业班组", "队组", "部门/班组" } },
                new FieldMappingInfo { DataField = "Position", StandardName = "岗位", Aliases = new List<string> { "岗位", "工种", "岗位工种", "岗位/工种", "岗位或工种", "职务", "职务/工种", "职务/岗位", "职务或岗位" } },
                new FieldMappingInfo { DataField = "Unit", StandardName = "单位", Aliases = new List<string> { "单位", "单位名称", "公司", "公司名称", "施工单位", "分包队伍", "分包单位", "协作队伍", "协作单位", "施工队", "施工队伍" } },
                new FieldMappingInfo { DataField = "BirthDate", StandardName = "出生年月", Aliases = new List<string> { "出生年月", "出生日期", "出生时间" } },
                new FieldMappingInfo { DataField = "EmployeeID", StandardName = "工号", Aliases = new List<string> { "工号", "员工编号", "员工号" } },
                new FieldMappingInfo { DataField = "NativePlace", StandardName = "籍贯", Aliases = new List<string> { "籍贯" } },
                new FieldMappingInfo { DataField = "EducationLevel", StandardName = "文化程度", Aliases = new List<string> { "文化程度", "学历", "教育程度" } },
                new FieldMappingInfo { DataField = "PhoneNumber", StandardName = "手机号", Aliases = new List<string> { "手机号", "手机号码", "联系电话", "联系方式", "电话" } },
                new FieldMappingInfo { DataField = "EntryTime", StandardName = "入场教育时间", Aliases = new List<string> { "入场教育时间", "入场时间", "进场时间", "教育时间", "入场教育", "三级教育时间" } },
                new FieldMappingInfo { DataField = "EduTime", StandardName = "教育时间", Aliases = new List<string> { "教育时间", "三级教育时间", "培训时间" } },
                new FieldMappingInfo { DataField = "ExitTime", StandardName = "退场时间", Aliases = new List<string> { "退场时间", "出场时间", "离场时间", "离场日期", "退场日期" } },
                new FieldMappingInfo { DataField = "IDCardDocNumber", StandardName = "身份证号", Aliases = new List<string> { "身份证号", "身份证号码", "证件号码", "身份证" } },
                new FieldMappingInfo { DataField = "IDCardValidPeriod", StandardName = "身份证有效期", Aliases = new List<string> { "身份证有效期", "证件有效期" } },
                new FieldMappingInfo { DataField = "ThreeCategoryPersonnelDisplay", StandardName = "三类人员", Aliases = new List<string> { "三类人员", "三类" } },
                new FieldMappingInfo { DataField = "SpecialOperationsPersonnelDisplay", StandardName = "特种作业", Aliases = new List<string> { "特种作业", "特种作业人员" } },
                new FieldMappingInfo { DataField = "CertName", StandardName = "证书名称", Aliases = new List<string> { "证书名称", "证书名" } },
                new FieldMappingInfo { DataField = "CertNumber", StandardName = "证书编号", Aliases = new List<string> { "证书编号", "证书号" } },
                new FieldMappingInfo { DataField = "CertValidPeriod", StandardName = "证书有效期", Aliases = new List<string> { "证书有效期", "证有效期" } },
                new FieldMappingInfo { DataField = "Remark", StandardName = "备注", Aliases = new List<string> { "备注", "说明", "备注说明" } },
            };
            _commonSynonyms = new Dictionary<string, List<string>>
            {
                ["隐患"] = new List<string> { "风险", "问题", "缺陷" },
                ["整改"] = new List<string> { "治理", "处理", "纠正" },
                ["入场"] = new List<string> { "进场", "入场" },
                ["岗位"] = new List<string> { "工种" }
            };
            _version = "1.1";
        }

        /// <summary>
        /// 向已加载的字典中追加人员模块字段映射（用于版本迁移）
        /// </summary>
        private void AppendPersonnelMappings()
        {
            var personnelFields = new List<FieldMappingInfo>
            {
                new FieldMappingInfo { DataField = "Title", StandardName = "姓名", Aliases = new List<string> { "姓名", "名字", "人员姓名" } },
                new FieldMappingInfo { DataField = "Gender", StandardName = "性别", Aliases = new List<string> { "性别" } },
                new FieldMappingInfo { DataField = "Age", StandardName = "年龄", Aliases = new List<string> { "年龄" } },
                new FieldMappingInfo { DataField = "Department", StandardName = "部门", Aliases = new List<string> { "部门", "班组", "施工班组", "作业班组", "队组" } },
                new FieldMappingInfo { DataField = "Position", StandardName = "岗位", Aliases = new List<string> { "岗位", "工种", "岗位工种", "岗位/工种", "岗位或工种" } },
                new FieldMappingInfo { DataField = "Unit", StandardName = "单位", Aliases = new List<string> { "单位", "单位名称", "公司", "公司名称", "施工单位", "分包队伍", "分包单位", "协作队伍", "协作单位", "施工队", "施工队伍" } },
                new FieldMappingInfo { DataField = "BirthDate", StandardName = "出生年月", Aliases = new List<string> { "出生年月", "出生日期", "出生时间" } },
                new FieldMappingInfo { DataField = "EmployeeID", StandardName = "工号", Aliases = new List<string> { "工号", "员工编号", "员工号" } },
                new FieldMappingInfo { DataField = "NativePlace", StandardName = "籍贯", Aliases = new List<string> { "籍贯" } },
                new FieldMappingInfo { DataField = "EducationLevel", StandardName = "文化程度", Aliases = new List<string> { "文化程度", "学历", "教育程度" } },
                new FieldMappingInfo { DataField = "PhoneNumber", StandardName = "手机号", Aliases = new List<string> { "手机号", "手机号码", "联系电话", "电话" } },
                new FieldMappingInfo { DataField = "EntryTime", StandardName = "入场教育时间", Aliases = new List<string> { "入场教育时间", "入场时间", "进场时间", "教育时间", "入场教育" } },
                new FieldMappingInfo { DataField = "EduTime", StandardName = "教育时间", Aliases = new List<string> { "教育时间", "三级教育时间", "培训时间" } },
                new FieldMappingInfo { DataField = "ExitTime", StandardName = "退场时间", Aliases = new List<string> { "退场时间", "出场时间", "离场时间" } },
                new FieldMappingInfo { DataField = "IDCardDocNumber", StandardName = "身份证号", Aliases = new List<string> { "身份证号", "身份证号码", "证件号码", "身份证" } },
                new FieldMappingInfo { DataField = "IDCardValidPeriod", StandardName = "身份证有效期", Aliases = new List<string> { "身份证有效期", "证件有效期" } },
                new FieldMappingInfo { DataField = "ThreeCategoryPersonnelDisplay", StandardName = "三类人员", Aliases = new List<string> { "三类人员", "三类" } },
                new FieldMappingInfo { DataField = "SpecialOperationsPersonnelDisplay", StandardName = "特种作业", Aliases = new List<string> { "特种作业", "特种作业人员" } },
                new FieldMappingInfo { DataField = "CertName", StandardName = "证书名称", Aliases = new List<string> { "证书名称", "证书名" } },
                new FieldMappingInfo { DataField = "CertNumber", StandardName = "证书编号", Aliases = new List<string> { "证书编号", "证书号" } },
                new FieldMappingInfo { DataField = "CertValidPeriod", StandardName = "证书有效期", Aliases = new List<string> { "证书有效期", "证有效期" } },
                new FieldMappingInfo { DataField = "Remark", StandardName = "备注", Aliases = new List<string> { "备注", "说明", "备注说明" } },
            };

            foreach (var pf in personnelFields)
            {
                // 只添加尚未存在的字段映射
                if (!_fieldMappings.Any(m => m.DataField == pf.DataField))
                    _fieldMappings.Add(pf);
            }

            // 补充常用同义词
            if (!_commonSynonyms.ContainsKey("入场"))
                _commonSynonyms["入场"] = new List<string> { "进场", "入场" };
            if (!_commonSynonyms.ContainsKey("岗位"))
                _commonSynonyms["岗位"] = new List<string> { "工种" };
        }

        private void Save()
        {
            try
            {
                var data = new DictionaryData
                {
                    Version = _version,
                    FieldMappings = _fieldMappings,
                    CommonSynonyms = _commonSynonyms
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存 MappingDictionary.json 失败");
            }
        }

        /// <summary>
        /// 根据表头文本获取匹配的数据字段名
        /// </summary>
        /// <param name="headerText">原始表头文本</param>
        /// <returns>数据字段名（如 "ResponsibleUnit"），未匹配返回 null</returns>
        public string? MatchField(string headerText)
        {
            if (string.IsNullOrWhiteSpace(headerText))
                return null;

            // 先塌缩各种空白字符（全角空格、不间断空格等），再归一化
            headerText = CollapseWhitespace(headerText);
            var normalized = NormalizeText(headerText);

            // 1. 直接匹配（现有逻辑）
            var result = TryMatchNormalized(normalized);
            if (result != null)
                return result;

            // 2. 分隔符回退匹配：对头文本按分隔符拆段，分别匹配
            //    只有恰好 1 个段匹配且其他段都不匹配时才接受，避免歧义
            var segments = SplitHeaderText(headerText);
            string? matchedField = null;
            foreach (var segment in segments)
            {
                var segNormalized = NormalizeText(segment);
                if (string.IsNullOrEmpty(segNormalized))
                    continue;
                // 跳过与整体归一化结果相同的段（避免重复匹配）
                if (segNormalized == normalized)
                    continue;

                var segResult = TryMatchNormalized(segNormalized);
                if (segResult != null)
                {
                    if (matchedField != null)
                    {
                        // 多个段匹配到不同字段 → 歧义，放弃
                        return null;
                    }
                    matchedField = segResult;
                }
            }

            return matchedField;
        }

        /// <summary>
        /// 匹配表头字段，并根据模块过滤结果。
        /// 隐患导出时只匹配隐患字段，人员导出时只匹配人员字段，避免跨模块误匹配。
        /// </summary>
        /// <param name="headerText">表头文本</param>
        /// <param name="module">模块名 "Hazard" / "Personnel"，null 时不过滤</param>
        public string? MatchField(string headerText, string? module)
        {
            var result = MatchField(headerText);
            if (result == null || module == null) return result;

            bool isPersonnelField = PersonnelDataFields.Contains(result);
            if (module == "Personnel" && !isPersonnelField) return null;
            if (module == "Hazard" && isPersonnelField) return null;
            return result;
        }

        /// <summary>
        /// 对归一化后的文本执行别名匹配，返回最佳匹配的数据字段名
        /// 排序优先级：精确匹配 &gt; 标准名匹配 &gt; 别名长度（越具体越优先）
        /// </summary>
        private string? TryMatchNormalized(string normalized)
        {
            var candidates = new List<(string dataField, int aliasLength, bool exactMatch, bool isStandardName)>();

            foreach (var mapping in _fieldMappings)
            {
                foreach (var alias in mapping.Aliases)
                {
                    var normalizedAlias = NormalizeText(alias);
                    if (string.IsNullOrEmpty(normalizedAlias))
                        continue;

                    if (normalized == normalizedAlias)
                    {
                        candidates.Add((mapping.DataField, normalizedAlias.Length, true, normalized == NormalizeText(mapping.StandardName)));
                    }
                    else if (normalized.Contains(normalizedAlias))
                    {
                        candidates.Add((mapping.DataField, normalizedAlias.Length, false, normalized.Contains(NormalizeText(mapping.StandardName))));
                    }
                }
            }

            if (candidates.Count == 0)
                return null;

            return candidates
                .OrderByDescending(c => c.exactMatch)
                .ThenByDescending(c => c.isStandardName)
                .ThenByDescending(c => c.aliasLength)
                .First().dataField;
        }

        /// <summary>
        /// 将表头文本按分隔符拆分为多个段（用于回退匹配）
        /// 分隔符包括：/ ／ 、 - — – · ＆ &amp; ＋ +
        /// </summary>
        private static List<string> SplitHeaderText(string text)
        {
            var separators = new[] { '/', '／', '、', '-', '—', '–', '·', '&', '＆', '+', '＋' };
            return text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToList();
        }

        /// <summary>
        /// 获取所有字段映射信息（用于管理界面）
        /// </summary>
        public List<FieldMappingInfo> GetFieldMappings() => _fieldMappings;

        /// <summary>
        /// 更新某个字段的别名列表
        /// </summary>
        public bool UpdateAliases(string dataField, List<string> newAliases)
        {
            var mapping = _fieldMappings.FirstOrDefault(m => m.DataField == dataField);
            if (mapping == null) return false;
            mapping.Aliases = newAliases;
            Save();
            return true;
        }

        /// <summary>
        /// 塌缩各种连续空白字符为单个空格，并 trim 两端。
        /// 覆盖半角空格、全角空格、不间断空格等所有 char.IsWhiteSpace 变体。
        /// </summary>
        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new System.Text.StringBuilder();
            bool lastWasSpace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace) sb.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || (c >= 0x4e00 && c <= 0x9fff))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private class DictionaryData
        {
            public string Version { get; set; } = "";
            public List<FieldMappingInfo> FieldMappings { get; set; } = new();
            public Dictionary<string, List<string>> CommonSynonyms { get; set; } = new();
        }
    }
}