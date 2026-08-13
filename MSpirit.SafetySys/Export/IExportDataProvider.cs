// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 导出数据提供器接口。
    /// 不同模块（隐患/人员）实现此接口，供多种导出引擎（智能模板/一键导出）调用，
    /// 实现导出引擎与模块数据的解耦。
    /// </summary>
    public interface IExportDataProvider
    {
        /// <summary>数据行总数</summary>
        int RowCount { get; }

        /// <summary>
        /// 获取指定行的指定字段的文本值
        /// </summary>
        string GetTextValue(int rowIndex, string dataField);

        /// <summary>
        /// 获取指定行的指定字段的图片文件完整路径列表
        /// </summary>
        List<string> GetPhotoPaths(int rowIndex, string dataField, string attachmentsRootPath);

        // ===== 一键导出元数据 =====

        /// <summary>标题后缀，如 "2026年06月隐患统计"、"XX单位人员统计"</summary>
        string OneClickTitleSuffix { get; }

        /// <summary>默认文件名后缀，如 "2026年06月隐患统计"、"XX单位人员统计"</summary>
        string OneClickFileSuffix { get; }

        /// <summary>表头文本数组</summary>
        string[] OneClickHeaders { get; }

        /// <summary>每个表头对应的字段名，用于 GetTextValue 查询</summary>
        string[] OneClickFieldNames { get; }

        /// <summary>列宽数组（字符数）</summary>
        int[] OneClickColumnWidths { get; }
    }
}
