---
name: beta-release-checklist
description: 斜月安全管理系统（MS_SafetyManagementSystem）内测发布准备流程与检查清单。当准备内测/发布新版本、制作更新包、检查发布产物、编写内测说明时使用。触发词：内测、发布、发版、打包、更新包、msupd、发布检查、发布清单。
agent_created: true
---

# 内测发布流程清单（斜月安全管理系统）

面向内测/正式发版的完整流程，按顺序执行。

## 一、发版链路（总览）

1. **定版**（按 `versioning-policy` 技能：改根 `Directory.Build.props` 的 `SafetySysVersion`，一处生效）
2. **构建发布**（dotnet publish）
3. **制作更新包**（全量/增量，双击「其他文件」打包脚本）
4. **编写内测说明**（放入发布目录）
5. **冒烟测试**（全新环境跑一遍）
6. **分发**（.msupd + Updater.exe 或压缩包）

## 二、构建发布

```bash
cd F:\【Program】\MSpirit.SafetySys
dotnet publish MS_SafetyManagementSystem.csproj -c Release -r win-x64 --self-contained true
```

- 产物：`bin\Release\publish\win-x64\`（自包含，ReadyToRun 编译约 5~15 分钟）。
- 验证：exe 日期更新 + 大小正常；`Resources\lan_page.html` / `hazard_lan_page.html` 存在（LAN 录入页随发布物拷贝）。

### 更新包（详见 docs/更新器使用说明.md）

- **依赖库未动** → 增量包（双击「其他文件」`打包增量更新包.bat`，默认 4 个主程序文件，约 10MB）。
- **依赖库变动/跨版本大改** → 全量包（`打包全量更新包.bat`）。**依赖库版本变化后必须发全量包**（增量包不更新依赖 dll，会造成版本混用）。
- 分发内容：`.msupd` + `Updater.exe`。

## 三、编写《内测说明》要点（面向同事，非开发者视角）

1. **启动方法**：双击 `MSpirit.SafetySys.exe`；若出现"Windows 已保护你的电脑"→ 点「更多信息 → 仍要运行」（未签名软件的正常提示）。
2. **数据在哪**：`D:\MSpirit.SafetySys.Date`（无其他盘则在 `%USERPROFILE%\MSpirit.SafetySys.Date`）——不要随意删除。
3. **数据备份建议**：程序已内置自动备份（启动/退出备份到 `ProjectsDate.Backup`，保留 30 天最多 100 份）；长期备份可在退出程序后整体复制数据目录。
4. **遇到问题怎么反馈**：把用户目录 `%USERPROFILE%\MSpirit.SafetySys\logs\debug*.log` 最新日志 + 问题截图发给作者。
5. **已知限制与隐私说明**：数据仅存本机、不联网上传（人员身份证照片等）。

## 四、提交前冒烟清单（全新机器/账号跑一遍）

首启 → 建项目 → 建记录 → 加图片 → 导出 → 重启数据还在。

## 五、发布前最终检查

- [ ] 版本号同步（根 `Directory.Build.props` 的 `SafetySysVersion` + `AppxManifest` 的 `Identity/@Version` + CHANGELOG 标题 + AboutWindow ChangelogData）
- [ ] Release 构建通过、产物齐全（exe 日期更新、LAN 页面存在）
- [ ] 说明文档已放入发布目录
- [ ] 过一遍 `docs/ISSUES.md`，确认无内测阻断项
- [ ] 明确内测范围（几人/哪些部门/用什么数据——建议让同事用**新建项目**试，不碰正式数据）
