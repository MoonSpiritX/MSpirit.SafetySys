// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;

namespace MS_SafetyManagementSystem.Tools
{
    /// <summary>
    /// 标注交互状态机（纯逻辑、无 UI 依赖，可单元测试；2026-08-16 新增）。
    /// 坐标域：**像素坐标**（与 <see cref="Annotation"/> 一致，显示层变换由 Overlay 负责）。
    /// 职责：当前工具（绘制/选择）、绘制中对象（Draft）、选中对象（Selected）、移动拖拽状态；
    /// 把鼠标 按下/移动/抬起 翻译为 创建标注 / 选中 / 平移 操作。
    /// Overlay 负责：鼠标事件坐标换算（显示→像素）、渲染、选中框/✕ 的 UI。
    /// </summary>
    public sealed class AnnotationTool
    {
        /// <summary>全部标注（创建/移动/删除后同步更新，Overlay 与 ViewModel 共用此列表）</summary>
        public List<Annotation> Items { get; } = new();

        /// <summary>当前绘制工具；null = 选择/移动模式</summary>
        public AnnotationKind? ActiveKind { get; set; }

        /// <summary>当前标注颜色（ARGB，新创建的标注使用；2026-08-16 由"标注颜色"下拉同步）</summary>
        public uint CurrentColorArgb { get; set; } = 0xFFE53935;

        /// <summary>当前文字字号（像素，新创建的文字标注使用；2026-08-16 由"字号"下拉同步）</summary>
        public float CurrentFontSize { get; set; } = 12f;

        /// <summary>当前线宽（像素，新创建的矩形/圆形/直线/箭头使用；2026-08-16 由"线宽"下拉同步）</summary>
        public float CurrentStrokeWidth { get; set; } = 2f;

        /// <summary>绘制中的对象（按下→抬起期间非 null）</summary>
        public Annotation? Draft { get; private set; }

        /// <summary>当前选中的对象（绘制中为 null）</summary>
        public Annotation? Selected { get; private set; }

        /// <summary>正在拖拽移动选中的对象</summary>
        public bool IsMoving { get; private set; }

        /// <summary>命中容差（像素）</summary>
        public float HitTolerance { get; set; } = 6f;

        /// <summary>是否处于绘制模式（ActiveKind 非空）</summary>
        public bool IsDrawing => ActiveKind.HasValue;

        /// <summary>命中容差选择：按当前工具分派。开始绘制（按下）</summary>
        public void OnMouseDown(float px, float py)
        {
            if (ActiveKind.HasValue)
            {
                // 绘制模式：创建新对象（按下即固定起点），套用当前颜色/字号/线宽
                Draft = CreateForKind(ActiveKind.Value);
                Draft.ColorArgb = CurrentColorArgb;
                Draft.FontSize = CurrentFontSize;
                Draft.StrokeWidth = CurrentStrokeWidth;
                Draft.X1 = px; Draft.Y1 = py;
                Draft.X2 = px; Draft.Y2 = py;
                if (ActiveKind == AnnotationKind.Text)
                {
                    // 文字：起点即落点，抬起不扩矩形；由 UI 在抬起后弹输入框
                    Draft.X2 = px; Draft.Y2 = py;
                }
                Selected = null;
                return;
            }

            // 选择/移动模式：命中测试
            var hit = Items.LastOrDefault(a => a.HitTest(px, py, HitTolerance));
            Selected = hit;
            if (hit != null) IsMoving = true;
        }

        /// <summary>拖动更新：绘制中扩 Draft；选择模式由 Overlay 计算增量后调用 MoveSelectedBy</summary>
        public void OnMouseMove(float px, float py)
        {
            if (Draft != null)
            {
                Draft.X2 = px; Draft.Y2 = py;
            }
        }

        /// <summary>按像素增量移动选中对象（Overlay 在 move 事件中计算增量后调用）</summary>
        public void MoveSelectedBy(float dx, float dy)
        {
            if (Selected != null && IsMoving) Selected.Move(dx, dy);
        }

        /// <summary>结束交互（抬起）：Draft 定型入列表并清 Draft；返回新创建的对象（文字模式返回后由 UI 填文本）</summary>
        public Annotation? OnMouseUp()
        {
            var draft = Draft;
            if (draft != null)
            {
                if (draft.Kind == AnnotationKind.Text)
                {
                    // 文字对象先不入列表（等 UI 填完文本 CommitText）
                    Draft = null;
                    return draft;
                }
                NormalizeDraft(draft);
                Items.Add(draft);
                Draft = null;
                Selected = draft; // 创建完成自动选中（可立即拖动）
                return draft;
            }
            IsMoving = false;
            return null;
        }

        /// <summary>文字模式：提交文本，把临时 Draft 转为正式对象加入列表</summary>
        public void CommitText(Annotation textAnnotation, string text)
        {
            if (textAnnotation == null || string.IsNullOrWhiteSpace(text)) return;
            textAnnotation.Text = text;
            Items.Add(textAnnotation);
            Selected = textAnnotation;
        }

        /// <summary>取消文字输入（不加入列表）</summary>
        public void CancelText(Annotation textAnnotation)
        {
            // 对象由调用方丢弃；此方法仅为语义占位
            Selected = null;
        }

        /// <summary>删除选中对象；返回是否删除</summary>
        public bool DeleteSelected()
        {
            if (Selected == null) return false;
            bool removed = Items.Remove(Selected);
            Selected = null;
            return removed;
        }

        /// <summary>删除指定对象</summary>
        public bool Delete(Annotation annotation)
        {
            if (annotation == null) return false;
            if (ReferenceEquals(annotation, Selected)) Selected = null;
            return Items.Remove(annotation);
        }

        /// <summary>清空全部标注与选中</summary>
        public void Clear()
        {
            Items.Clear();
            Selected = null;
            Draft = null;
            IsMoving = false;
        }

        /// <summary>重置绘制/选择状态（切换图片时调用，不清空 Items——Items 归属 ViewModel）</summary>
        public void ResetInteraction()
        {
            Draft = null;
            Selected = null;
            IsMoving = false;
        }

        /// <summary>把 Draft 归一化：矩形/圆形/马赛克两点任意方向 → 左上右下；线/箭头/文字保持原序</summary>
        private static void NormalizeDraft(Annotation a)
        {
            if (a.Kind is AnnotationKind.Rectangle or AnnotationKind.Oval or AnnotationKind.Mosaic)
            {
                float l = Math.Min(a.X1, a.X2), t = Math.Min(a.Y1, a.Y2);
                float r = Math.Max(a.X1, a.X2), b = Math.Max(a.Y1, a.Y2);
                a.X1 = l; a.Y1 = t; a.X2 = r; a.Y2 = b;
            }
        }

        private static Annotation CreateForKind(AnnotationKind kind) => kind switch
        {
            AnnotationKind.Rectangle => new Annotation(AnnotationKind.Rectangle),
            AnnotationKind.Oval => new Annotation(AnnotationKind.Oval),
            AnnotationKind.Line => new Annotation(AnnotationKind.Line),
            AnnotationKind.Arrow => new Annotation(AnnotationKind.Arrow),
            AnnotationKind.Text => new Annotation(AnnotationKind.Text),
            AnnotationKind.Mosaic => new Annotation(AnnotationKind.Mosaic),
            _ => new Annotation(AnnotationKind.Rectangle)
        };
    }
}
