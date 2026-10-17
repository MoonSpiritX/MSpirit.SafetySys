// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MS_SafetyManagementSystem.Services
{
    public class DataPathService
    {
        /// <summary>数据根目录名（位于所选盘根目录下）</summary>
        public const string DataRootFolderName = "MSpirit.SafetySys.Date";

        public string RootPath { get; }

        public string GetViewStateFilePath() => Path.Combine(RootPath, "ViewState.json");

        /// <summary>数据根"请勿手动操作"提示文件名（无扩展名，文件名即提示语）</summary>
        public const string DataRootNoticeFileName = "此目录为斜月安全管理系统存档目录，请勿手动操作";

        /// <summary>
        /// 默认构造：自动选择数据根位置。
        /// 规则（2026-08-15 由"仅认 D 盘"改为通用算法）：
        ///  - 优先：第一个可用的非系统固定盘（按盘符字母序 D→E→F…，排除系统盘与可移动/网络盘）；
        ///  - 兜底：%USERPROFILE%\MSpirit.SafetySys.Date（单盘机器；2026-10-15 由 LOCALAPPDATA 改此，
        ///    规避 MSIX 虚拟化导致"卸载即清空数据"）。
        /// 数据根 = {所选盘根}\MSpirit.SafetySys.Date。
        /// </summary>
        public DataPathService() : this(DefaultRootPath())
        {
        }

        /// <summary>
        /// 显式指定数据根（测试注入 / 数据根恢复 / 未来用户自定义数据位置用）。
        /// 传入的必须是最终数据根完整路径（如 "D:\MSpirit.SafetySys.Date"）。
        /// </summary>
        public DataPathService(string rootPath)
        {
            RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
            Directory.CreateDirectory(Path.Combine(RootPath, "ProjectsDate"));
            Directory.CreateDirectory(Path.Combine(RootPath, "Temp"));
        }

        /// <summary>
        /// 计算默认数据根路径：先选非系统固定盘，无可选时兜底到用户目录（DataRootLocator 决策链复用）。
        /// 兜底位置 2026-10-15 由 %LOCALAPPDATA% 改为 %USERPROFILE%\MSpirit.SafetySys.Date：
        /// MSIX 会把对 AppData 的新建写入重定向到包私有目录，卸载即清空，单盘机器会因此丢全部数据。
        /// </summary>
        public static string DefaultRootPath()
        {
            string? driveRoot = PickDataDrive(GetFixedReadyDriveRoots());
            if (!string.IsNullOrEmpty(driveRoot))
                return Path.Combine(driveRoot, DataRootFolderName);

            return AppPaths.FallbackDataRootPath;
        }

        /// <summary>枚举所有就绪的固定盘根路径（如 "C:\"、"D:\"）</summary>
        private static IEnumerable<string> GetFixedReadyDriveRoots()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.Name);
        }

        /// <summary>
        /// 选盘算法（纯逻辑，可单测）：从候选盘根路径中挑选第一个非系统盘（按盘符字母序）。
        /// 盘符字母序（而非"剩余空间最大"）：行为确定、用户易理解，且几百 MB 级数据任意盘均足够。
        /// </summary>
        /// <param name="candidateRoots">候选盘根路径集合（如 "C:\"、"D:\"、"E:\"）</param>
        /// <param name="systemRoot">系统盘根路径（如 "C:\"）；null 时自动探测</param>
        /// <returns>选中的盘根路径（如 "D:\"）；无可选时返回 null</returns>
        public static string? PickDataDrive(IEnumerable<string> candidateRoots, string? systemRoot = null)
        {
            if (candidateRoots == null) return null;

            systemRoot ??= Path.GetPathRoot(Environment.SystemDirectory);
            var candidates = candidateRoots
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Where(r => string.IsNullOrEmpty(systemRoot)
                            || !r.Equals(systemRoot, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return candidates.Count > 0 ? candidates[0] : null;
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
