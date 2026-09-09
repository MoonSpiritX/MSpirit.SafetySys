// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Services;

namespace MS_SafetyManagementSystem.Helpers
{
    public static class AppServiceProvider
    {
        public static CommonHazardTypeService? CommonHazardTypeService { get; set; }
        public static SettingsService? SettingsService { get; set; }

        /// <summary>
        /// 全局数据根服务：App 启动时经 DataRootLocator 决策后设置一次，全程序共享。
        /// 严禁各处重新 new DataPathService()——数据根可能被用户/恢复流程指定为非默认位置，
        /// 重新 new 会按默认算法漂移，导致读写不同数据根。
        /// </summary>
        public static DataPathService? DataPathService { get; set; }

        /// <summary>获取全局数据根服务；未初始化时兜底默认构造（正常流程不会走到）</summary>
        public static DataPathService GetDataPathService()
        {
            DataPathService ??= new DataPathService();
            return DataPathService;
        }
    }
}
