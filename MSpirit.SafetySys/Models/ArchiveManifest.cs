// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MS_SafetyManagementSystem.Models
{
    /// <summary>
    /// .msarch 存档包顶层 manifest 元数据（包内 manifest.json）。
    /// 用于记录项目名称等"不在项目文件夹里"的信息，导入时据此注册 master.db。
    /// </summary>
    public class ArchiveManifest
    {
        /// <summary>格式版本号（升级兼容用，当前 1）</summary>
        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; set; } = 1;

        /// <summary>生成该包的软件版本</summary>
        [JsonPropertyName("appVersion")]
        public string AppVersion { get; set; } = "";

        /// <summary>导出时间（yyyy-MM-dd HH:mm:ss）</summary>
        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = "";

        /// <summary>包内项目列表</summary>
        [JsonPropertyName("projects")]
        public List<ArchiveProjectInfo> Projects { get; set; } = new();
    }

    /// <summary>manifest 中的单个项目信息</summary>
    public class ArchiveProjectInfo
    {
        /// <summary>项目文件夹名（如 Project_xxx），即解压后的目录名</summary>
        [JsonPropertyName("folder")]
        public string Folder { get; set; } = "";

        /// <summary>项目名称（存于 master.db 的 Projects 表，必须随包携带）</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>项目创建日期（yyyy-MM-dd）</summary>
        [JsonPropertyName("created")]
        public string Created { get; set; } = "";
    }
}
