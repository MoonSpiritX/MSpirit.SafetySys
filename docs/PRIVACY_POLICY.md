# Privacy Policy

**斜月安全管理系统（MS_SafetyManagementSystem）**

*Effective Date: 2026-08-04 | Version 1.0*

## 1. Overview

This software is a local desktop application for safety inspection and personnel management in physical-industry enterprises (construction, manufacturing, production, etc.). It follows a "local-first" principle: the personal and project data you enter is stored on your own computer and is never uploaded to any cloud server.

## 2. Information We Collect

- **Information you provide directly**: personnel records (name, gender, age, place of origin, ID card number, phone number, certificate/document information and photos); hazard records (title, description, photos, responsible unit, responsible person, location, rectification deadline and status); project and group information.
- **Information submitted via LAN entry**: when you enable the LAN entry feature, photos and field information are submitted from a phone browser on the same local network to your computer.
- **Local logs**: runtime information and exception stack traces, stored locally for up to 7 days.
- **We do NOT collect**: precise device location, contacts, device identifiers, usage analytics, or automatic crash reports. No advertising or tracking SDKs are used.

## 3. How We Use Information

All information is used solely for the local features of the software: personnel/profile management, hazard records, statistics, and report export. We do not use your information for advertising, marketing, or profiling.

## 4. Where Information Is Stored

- All data is stored locally on your computer under `D:\MSpirit.SafetySys.Date` (when a D: drive exists) or `%LOCALAPPDATA%\MSpirit.SafetySys.Date`.
- Data is stored in local SQLite databases and attachment files (photos, documents).
- **No built-in encryption**: data is stored in plaintext. If your data is highly sensitive, we recommend enabling OS-level disk encryption (e.g., BitLocker) on your computer.

## 5. How Information Is Transmitted (LAN Entry)

- When the LAN entry feature is started, the software runs a local HTTP server bound to `0.0.0.0` on a user-selected port.
- Data is transmitted between your phone and your computer over the local network in **plaintext HTTP, without authentication**.
- Data is never transmitted to the Internet.
- Recommendations: only enable this feature on a trusted local network; close it when not in use; avoid public or untrusted networks.

## 6. Third-Party Services

- The software does not rely on any cloud service. The OCR (text recognition) feature runs entirely locally using PaddleOCR and ONNX Runtime.
- The software is built on open-source components; see `THIRD-PARTY-NOTICES` in the project root for the license list.

## 7. Sharing and Disclosure

- We do not sell, rent, or share your information with any third party.
- We may disclose information only if required by applicable law.

## 8. Data Retention and Deletion

- You can delete records, personnel, or entire projects at any time; the software removes the corresponding database rows and attachment files.
- **Uninstalling the software does NOT delete your data directory.** To fully remove your data, manually delete the data directory (see Section 4).

## 9. Children's Privacy

- This software is intended for professional safety management personnel and is not designed for, or directed to, children.

## 10. Changes to This Policy

- We may update this policy from time to time. The latest version will always be available in this document with an updated effective date.

## 11. Contact

- If you have any questions about this policy, please contact the author: AXin (斜月Spirit), via email (**374532719@qq.com**), GitHub (**https://github.com/MoonSpiritX**), or the official Douyin user group (**群号 659306761979**).

---

# 隐私政策

**斜月安全管理系统（MS_SafetyManagementSystem）**

*生效日期：2026年8月4日 | 版本 1.0*

## 1. 概述

本软件是一款面向实体行业（建筑、制造、生产等）企业安全巡查与人员管理的本地桌面应用，遵循"数据本地优先"原则：您录入的人员与项目数据仅存储于您自己的电脑，不会上传至任何云端服务器。

## 2. 我们收集的信息

- **您主动录入的信息**：人员档案（姓名、性别、年龄、籍贯、身份证号、手机号、证件信息与照片）；隐患记录（标题、描述、照片、责任单位、责任人、地点、整改期限与状态）；项目与分组信息。
- **局域网录入提交的信息**：当您开启"局域网录入"功能时，同一局域网内手机浏览器提交的照片与字段信息会传输至您的电脑。
- **本地日志**：运行信息与异常堆栈，仅本地保存，最长保留 7 天。
- **我们不收集**：设备精确定位、通讯录、设备标识、使用行为统计、自动崩溃上报；不使用任何广告或跟踪 SDK。

## 3. 信息的使用

所有信息仅用于软件本地功能：人员/档案管理、隐患记录、统计分析、报告导出。我们不会将您的信息用于广告、营销或用户画像。

## 4. 信息的存储

- 所有数据存储在您电脑本地：`D:\MSpirit.SafetySys.Date`（存在 D 盘时）或 `%LOCALAPPDATA%\MSpirit.SafetySys.Date`。
- 数据以本地 SQLite 数据库与附件文件（照片、文档）形式存储。
- **无内置加密**：数据以明文存储。如数据较为敏感，建议在电脑上启用系统级磁盘加密（如 BitLocker）。

## 5. 信息的传输（局域网录入）

- 开启局域网录入时，软件会在您选择的端口上启动绑定 `0.0.0.0` 的本地 HTTP 服务。
- 数据通过局域网在手机与电脑之间以**明文 HTTP、无鉴权**方式传输。
- 数据不会传输到互联网。
- 建议：仅在可信局域网内开启此功能；使用完毕后立即关闭；避免在公共或不可信网络中使用。

## 6. 第三方服务

- 本软件不依赖任何云端服务。文字识别（OCR）功能完全本地运行（PaddleOCR + ONNX Runtime）。
- 本软件基于开源组件构建，第三方组件许可清单见项目根目录 `THIRD-PARTY-NOTICES`。

## 7. 共享与披露

- 我们不会向任何第三方出售、出租或共享您的信息。
- 仅在适用法律要求时，我们才可能披露相关信息。

## 8. 数据保留与删除

- 您可以随时删除记录、人员或整个项目，软件会同步删除对应的数据库记录与附件文件。
- **卸载软件不会删除您的数据目录**。如需彻底清除数据，请手动删除数据目录（见第 4 节）。

## 9. 未成年人隐私

- 本软件面向专业安全管理从业人员，不面向儿童设计，也不针对儿童提供。

## 10. 政策的变更

- 我们可能不时更新本政策，最新版本将始终以本文件为准并标注更新日期。

## 11. 联系我们

- 如对本政策有任何疑问，可通过以下方式联系作者：AXin（斜月Spirit）——邮箱（**374532719@qq.com**）、GitHub（**https://github.com/MoonSpiritX**）或抖音交流群（**群号 659306761979**）。

---

*版本说明：本文件提供英文与中文两种文本，内容一致；中英文如有出入，以中文版本为准。*
