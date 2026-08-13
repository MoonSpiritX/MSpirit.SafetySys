// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Linq;

namespace MS_SafetyManagementSystem.Services
{
    public class DataPathService
    {
        public string RootPath { get; }

        public string GetViewStateFilePath() => Path.Combine(RootPath, "ViewState.json");

        /// <summary>数据根"请勿手动操作"提示文件名（无扩展名，文件名即提示语）</summary>
        public const string DataRootNoticeFileName = "此目录为斜月安全管理系统存档目录，请勿手动操作";

        public DataPathService()
        {
            if (DriveInfo.GetDrives().Any(d => d.Name.StartsWith("D:") && d.IsReady))
            {
                RootPath = @"D:\MSpirit.SafetySys.Date";
            }
            else
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                RootPath = Path.Combine(localAppData, "MSpirit.SafetySys.Date");
            }

            // 创建必要子目录（master.db 与各项目库同置于 ProjectsDate；Data 目录已于 2026-08-10 调整移除）
            Directory.CreateDirectory(Path.Combine(RootPath, "ProjectsDate"));
            Directory.CreateDirectory(Path.Combine(RootPath, "Temp"));
        }

        /// <summary>
        /// 幂等检查数据根提示文件：存在则跳过（绝不覆盖用户改动），缺失则创建空文件。
        /// 失败仅记日志，不影响程序使用（提示文件属锦上添花）。
        /// </summary>
        public void EnsureDataRootNoticeFile()
        {
            try
            {
                string noticePath = Path.Combine(RootPath, DataRootNoticeFileName);
                if (!File.Exists(noticePath))
                {
                    File.WriteAllText(noticePath, "");
                    Serilog.Log.Information("[DataPath] 已创建数据根提示文件: {Path}", noticePath);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[DataPath] 创建数据根提示文件失败（忽略）");
            }
        }
    }
}