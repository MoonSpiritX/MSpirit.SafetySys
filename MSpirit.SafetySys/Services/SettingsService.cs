// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Common;

namespace MS_SafetyManagementSystem.Services
{
    public class SettingsService
    {
        private readonly MasterDbContext _masterDb;

        public AppSettings AppSettings { get; private set; } = new AppSettings();

        public SettingsService(DataPathService dataPathService)
        {
            _masterDb = new MasterDbContext(dataPathService.RootPath);
            Load();
        }

        private void Load()
        {
            string viewerType = _masterDb.GetSetting("ImageViewerType") ?? ImageViewerType.Internal;
            string startMode = _masterDb.GetSetting("ImageViewerStartMode") ?? ImageViewerStartMode.FullScreen;
            string currentModule = _masterDb.GetSetting("CurrentModule") ?? ModuleNames.Hazard;
            string selectorMode = _masterDb.GetSetting("HazardTypeSelectorMode") ?? "FullWindow";
            AppSettings = new AppSettings
            {
                ImageViewerType = viewerType,
                ImageViewerStartMode = startMode,
                CurrentModule = currentModule,
                HazardTypeSelectorMode = selectorMode
            };
        }

        public void Save()
        {
            _masterDb.SetSetting("ImageViewerType", AppSettings.ImageViewerType);
            _masterDb.SetSetting("ImageViewerStartMode", AppSettings.ImageViewerStartMode);
            _masterDb.SetSetting("CurrentModule", AppSettings.CurrentModule);
            _masterDb.SetSetting("HazardTypeSelectorMode", AppSettings.HazardTypeSelectorMode);
        }

        public string GetHazardTypeSelectorMode() => AppSettings.HazardTypeSelectorMode;

        public void SetHazardTypeSelectorMode(string mode)
        {
            if (mode == "FullWindow" || mode == "DropdownMenu")
            {
                AppSettings.HazardTypeSelectorMode = mode;
                Save();
            }
        }

        public void SetImageViewerType(string type)
        {
            if (type == ImageViewerType.Internal || type == ImageViewerType.System)
            {
                AppSettings.ImageViewerType = type;
                Save();
            }
        }

        public string GetCurrentModule()
        {
            return AppSettings.CurrentModule;
        }

        public string GetImageViewerType() => AppSettings.ImageViewerType;

        public string GetImageViewerStartMode() => AppSettings.ImageViewerStartMode;

        public void SetImageViewerStartMode(string mode)
        {
            if (mode == ImageViewerStartMode.FullScreen || mode == ImageViewerStartMode.Normal)
            {
                AppSettings.ImageViewerStartMode = mode;
                Save();
            }
        }

        public void SetCurrentModule(string module)
        {
            if (module == ModuleNames.Hazard || module == ModuleNames.Personnel)
            {
                AppSettings.CurrentModule = module;
                Save();
            }
        }

        // ====== 窗口规格 ======
        public (double left, double top, double width, double height, string windowState)? GetWindowBounds()
        {
            string? data = _masterDb.GetSetting("WindowBounds");
            if (string.IsNullOrEmpty(data)) return null;
            var parts = data.Split('|');
            if (parts.Length != 5) return null;
            if (double.TryParse(parts[0], out double l) &&
                double.TryParse(parts[1], out double t) &&
                double.TryParse(parts[2], out double w) &&
                double.TryParse(parts[3], out double h))
            {
                return (l, t, w, h, parts[4]);
            }
            return null;
        }

        public void SetWindowBounds(double left, double top, double width, double height, string windowState)
        {
            string data = $"{left}|{top}|{width}|{height}|{windowState}";
            _masterDb.SetSetting("WindowBounds", data);
        }
    }
}