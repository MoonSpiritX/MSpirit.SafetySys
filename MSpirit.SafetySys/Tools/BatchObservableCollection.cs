// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 支持批量添加的 ObservableCollection：AddRange 一次完成多项添加、仅触发一次
    /// 集合变更通知（Reset），避免大批量导入时逐条 Add 造成 N 次通知与 DataGrid 重复处理。
    /// 其余行为与 ObservableCollection 一致（Remove/Clear 仍逐条通知）。
    /// </summary>
    public class BatchObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>批量添加多项，只触发一次 Reset 通知（经 Items 直接添加，不逐条通知）。</summary>
        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
                Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
