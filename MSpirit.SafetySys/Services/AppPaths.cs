// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// 应用级固定路径（2026-10-15 统一收敛，供绿色版与微软商店版共用）。
    /// <para>
    /// 背景：MSIX（微软商店）打包后，程序向 %LOCALAPPDATA% / %APPDATA% 的**新建**写入会被
    /// 系统重定向到包私有目录 %LOCALAPPDATA%\Packages\&lt;包族名&gt;\LocalCache\...，
    /// 而**卸载 MSIX 包时该目录会被一并清空**。若用户只有 C 盘（办公机很常见），
    /// 数据根恰好落在那里 → 卸载软件 = 全部安全数据消失。
    /// </para>
    /// <para>
    /// 因此所有"必须跨卸载保留"的程序状态统一放在用户目录下 %USERPROFILE%\MSpirit.SafetySys\，
    /// 该位置不在 MSIX 虚拟化（KnownFolder 重定向）覆盖范围内，绿色版与商店版行为一致。
    /// 数据根本体仍优先放在非系统盘（见 <see cref="DataPathService.DefaultRootPath"/>）。
    /// </para>
    /// </summary>
    public static class AppPaths
    {
        /// <summary>程序固定目录名（位于用户目录下）</summary>
        public const string AppFolderName = "MSpirit.SafetySys";

        /// <summary>日志子目录名</summary>
        public const string LogFolderName = "logs";

        /// <summary>日志文件名（Serilog 按天滚动，实际落盘为 debug20261015.log 这类）</summary>
        public const string LogFileName = "debug.log";

        /// <summary>数据根记录文件名</summary>
        public const string RecordFileName = "DataRoot.txt";

        /// <summary>
        /// 用户目录（%USERPROFILE%，如 C:\Users\xxx）。
        /// 不用 Environment.SpecialFolder.UserProfile 之外的 AppData 系列——那正是 MSIX 会虚拟化的位置。
        /// </summary>
        public static string UserProfileFolder =>
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>
        /// 程序固定目录：%USERPROFILE%\MSpirit.SafetySys。
        /// 存放数据根记录与日志；不随数据根漂移、不受 MSIX 虚拟化影响、卸载后保留（便于排障）。
        /// </summary>
        public static string AppFolder => Path.Combine(UserProfileFolder, AppFolderName);

        /// <summary>日志文件完整路径</summary>
        public static string LogFilePath => Path.Combine(AppFolder, LogFolderName, LogFileName);

        /// <summary>数据根记录文件完整路径（当前位置）</summary>
        public static string RecordFilePath => Path.Combine(AppFolder, RecordFileName);

        /// <summary>
        /// 旧版数据根记录位置：%LOCALAPPDATA%\MSpirit.SafetySys\DataRoot.txt。
        /// 仅用于**读取回落**（老用户升级后不误判"数据根丢失"），不再写入。
        /// </summary>
        public static string LegacyRecordFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName, RecordFileName);

        /// <summary>
        /// 数据根兜底目录：%USERPROFILE%\MSpirit.SafetySys.Date。
        /// 仅当无任何非系统固定盘可用时（单盘机器）使用；同样规避 MSIX 虚拟化导致的卸载丢数据。
        /// </summary>
        public static string FallbackDataRootPath =>
            Path.Combine(UserProfileFolder, DataPathService.DataRootFolderName);

        /// <summary>
        /// 旧版数据根位置：%LOCALAPPDATA%\MSpirit.SafetySys.Date。
        /// 不再作为兜底写入目标，但浅扫时仍要能找到它（老版本遗留数据）。
        /// </summary>
        public static string LegacyFallbackDataRootPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DataPathService.DataRootFolderName);
    }
}
