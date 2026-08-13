// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Services;
using System.Collections.Generic;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 数据写入器接口：将通用解析结果写入具体模块的数据库记录
    /// 新模块只需实现此接口即可复用完整的导入（解析→校验→预览→确认→写入）流程
    /// </summary>
    public interface IDataWriter
    {
        /// <summary>
        /// 将解析后的数据行批量写入数据库
        /// </summary>
        /// <param name="result">导入解析结果</param>
        /// <param name="dataService">项目数据服务</param>
        /// <param name="targetYearMonth">目标年月（yyyy-MM）</param>
        /// <param name="targetDateGroupId">目标日期分组 ID</param>
        /// <param name="autoAssignByDate">是否按文件中的日期自动分配分组</param>
        /// <returns>(成功数, 失败数, 错误列表)</returns>
        (int success, int failed, List<string> errors) Import(
            ImportParseResult result,
            ProjectDataService dataService,
            string targetYearMonth,
            string targetDateGroupId,
            bool autoAssignByDate);
    }
}
