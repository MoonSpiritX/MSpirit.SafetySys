// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Threading;

namespace MS_SafetyManagementSystem.Helpers
{
    public static class SingleInstanceGuard
    {
        private static Mutex? _mutex;
        private static bool _ownsMutex = false;
        private const string AppId = "MSpirit.SafetySys_Unique";

        public static bool TryAcquire()
        {
            try
            {
                _mutex = new Mutex(true, AppId, out bool createdNew);
                _ownsMutex = createdNew;
                return createdNew;
            }
            catch (AbandonedMutexException)
            {
                // 互斥体被遗弃（原进程已崩溃或异常退出），但我们可以接管它
                // 此时互斥体已被当前线程获取，无需再创建新实例
                _ownsMutex = true;
                return true;
            }
            catch (Exception)
            {
                // 其他异常（如权限不足）视为获取失败
                return false;
            }
        }

        public static void Release()
        {
            if (_ownsMutex && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // 可能互斥体已被释放，忽略
                }
                finally
                {
                    _mutex.Dispose();
                }
            }
            _mutex = null;
            _ownsMutex = false;
        }
    }
}
