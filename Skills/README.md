# Skills — 正式 Skill 存放处

> 创建：2026-08-13 ｜ 定位：**只存放已形成的正式 Skill**（标准 SKILL.md 格式：frontmatter 含 name + 触发描述）
> 规则（2026-08-13 用户确认，正式出处见 `docs/开发约定.md` 文档边界）：
> - **正式 Skill** → 放本目录
> - **未成形的经验/计划文档** → 放「临时文档」文件夹
> - **正式项目文档** → 放 docs/（进 git）

## 已收录

| Skill | 触发场景 | 正本位置 |
|---|---|---|
| `dev-conventions.SKILL.md` | 本项目一切功能开发/数据层修改/Bug 修复/发布任务 | `.workbuddy/skills/dev-conventions/` |
| `versioning-policy.SKILL.md` | 定版、递增版本号、发版三处同步 | `.workbuddy/skills/versioning-policy/` |
| `beta-release-checklist.SKILL.md` | 内测/发布准备、更新包制作、冒烟与检查清单 | `.workbuddy/skills/beta-release-checklist/` |
| `wpf-partial-class-split.SKILL.md` | WPF 大文件（超 1000 行）拆分分部类 | `~/.workbuddy/skills/wpf-partial-class-split/` |

> 说明：本目录是正式 Skill 的**归档展示处**；WorkBuddy 自动加载技能时读取的是技能目录里的正本（项目级 `.workbuddy/skills/` 或用户级 `~/.workbuddy/skills/`）。新增正式 Skill 时需同时放入技能目录正本 + 本目录副本。

## 参考资料入口（不在本目录）

开发中按需参考（由助手主动读取）：
- 《开发约定》《数据架构》《版本号管理方案》等正式文档 → `docs/`
- 功能开发备忘、发布清单、问题分析等 → `临时文档/`
