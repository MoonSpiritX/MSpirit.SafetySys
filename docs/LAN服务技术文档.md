# LAN 服务技术文档

> 整理日期：2026-08-15 ｜ 状态：初版（随代码演进持续更新）
> 适用范围：斜月安全管理系统（MS_SafetyManagementSystem，个人版 V1.2）
> 定位：说明程序内置的局域网（LAN）服务与更新 IPC 的实现架构、路由协议、数据流与安全设计，作为排查联调问题、扩展新 LAN 功能时的依据。数据定义以代码为准。
> 关联文档：`docs/数据架构.md`（附件路径/分组规则）、`docs/开发约定.md`（坑清单）、`docs/用户手册.md`（用户操作说明）、`临时文档/开发-手机图片传输.md`（手机图片传输设计备忘）

---

## 1. 服务全景

程序内置 **3 个局域网 HTTP 服务**（均基于 Kestrel，零 UAC 绑定）+ **1 个命名管道 IPC**：

| 服务 | 类（文件） | 用途 | 手机端能力 | 处理链路 |
|---|---|---|---|---|
| 人员批量录入 | `Services/LanService.cs` | 手机扫码拍照身份证，OCR 识别后批量录入人员卡片 | 拍照/相册选图（正反面）、部门切换、提交状态轮询 | 上传 → 临时 → OCR → 暂存 → 提交队列 → 落库 |
| 隐患手机录入 | `Services/HazardLanService.cs` | 手机填表 + 拍照录入隐患卡片（自动建年月/日期分组） | 表单（位置/级别/描述/整改措施）、隐患/整改照片、日期选择 | 上传 → 临时 → 提交 → 落库 |
| 手机图片传输 | `Tools/PhotoTransferService.cs` | 手机原图传到电脑选定文件夹，按拍摄日期归档 | 拍照/相册多选、逐张直传、完成提示 | 上传 → 读 EXIF → 按日期落盘 |
| 更新 IPC | `Services/UpdateIpcService.cs` | 供 `Updater.exe` 请求主程序"保存数据并退出"（绿色版更新时文件被占用） | —（命名管道，非 HTTP） | 管道命令 → UI 线程保存退出 |

三者共用同一套骨架：**Kestrel 托管 + token 鉴权 + 20MB 上传上限 + 端口 `LanPorts.Start`..`LanPorts.ProbeEnd` 自动探测 + 局域网 IP 获取 + 嵌入式 HTML 页面**。服务之间不互斥，可多窗口同时运行（端口自动递增，如 Start/Start+1/Start+2）。

> 差异速记：人员录入**建记录 + OCR**；隐患录入**建记录、不 OCR**；图片传输**两者都不做，纯图片落盘**。

---

## 2. 架构总览

### 2.1 技术选型：为什么用 Kestrel

- 框架引用：`MS_SafetyManagementSystem.csproj` 中 `<FrameworkReference Include="Microsoft.AspNetCore.App" />`，直接使用 ASP.NET Core 的 `WebApplication` 最小宿主。
- 绑定 `http://0.0.0.0:{port}`，**无需管理员权限（免 UAC）**即可接受所有网卡请求——这是相对 `HttpListener` 的核心优势（`HttpListener` 绑定非回环地址在部分环境下需要 URL ACL 授权）。
- 对比既有方案：曾用 HttpListener，2026-08-12 前后统一迁移到 Kestrel。

### 2.2 服务启动流程（三个 HTTP 服务一致）

```
Start(port)
 ├─ 生成访问令牌：RandomNumberGenerator.GetBytes(32) → 64 位 HEX 字符串
 ├─ WebApplication.CreateBuilder → UseUrls("http://0.0.0.0:{port}") → UseKestrel → Build
 ├─ 注册鉴权中间件（token 不匹配一律 401）
 ├─ 注册路由（MapGet / MapPost）
 ├─ 同步等待 Kestrel 启动：
 │    Task.Run(() => _app.StartAsync(_cts.Token)).GetAwaiter().GetResult()
 │    └─ 端口被占在此抛出 → 调用方端口探测捕获后尝试下一个端口
 │    └─ 失败时 DisposeAsync 清理半启动服务后再抛出
 └─ 标记 _running = true，写日志
```

> ⚠️ **启动必须同步等待**（开发约定 2026-08-12 踩坑）：`_ = _app.StartAsync(...)` fire-and-forget 会导致端口绑定失败（IOException）不感知、端口探测永远"成功"、多个服务窗口显示同一端口。必须 `Task.Run(...).GetAwaiter().GetResult()`，失败清理 `_app` 后重新抛出。

### 2.3 服务停止流程（三个 HTTP 服务一致）

```
Stop()
 ├─ _cts.Cancel() + Volatile.Write(_running, false)
 ├─ Interlocked.Exchange 取出 _app 置空
 ├─ fire-and-forget 调用 app.StopAsync（不阻塞 UI 线程）
 └─ 清理本实例未提交照片的临时目录（按 tempId 目录，不影响其他窗口的服务）
```

### 2.4 鉴权：token 模式

- 每次启动生成 64 位随机 HEX token（`RandomNumberGenerator.GetBytes(32)`，加密安全随机数）。
- token **拼入二维码 URL**（如 `http://192.168.1.5:{LanPorts.Start}/?groupId=xxx&token=<token>`）。
- 所有请求必须携带 `?token=` 查询参数，`IsAuthorized` 用 `CryptographicOperations.FixedTimeEquals` **恒定时间比较**（防时序攻击），不匹配返回 `401 {"ok":false,"error":"未授权访问，请重新扫描二维码"}`。
- token 随服务生命周期存在：服务停止后 token 失效，二维码隐藏，手机端旧页面全部请求 401。

### 2.5 端口分配

- 从 **`LanPorts.Start`**（`Common/Constants.cs`，唯一修改处）开始递增尝试（上限 `LanPorts.ProbeEnd` = Start+99），共用 `LanPortProbe.FindAvailable`（`Services/LanPortProbe.cs`，2026-08-16 自三窗口抽取）逐个 `Start(port)`，捕获 `IOException / InvalidOperationException / SocketException / HttpListenerException` 视为端口被占，继续下一个。
  - 捕获集含 **SocketException**（2026-08-16 修复点）：Windows 保留/排除端口段（Hyper-V/WSL/WinNAT 动态排除范围）bind 返回 WSAEACCES(10013)，Kestrel 直接抛原始 SocketException——漏掉它会导致探测起点落在保留端口上时整个启动流程中止（"启动服务失败"弹窗），而实际上一个端口之后即可正常绑定。
- 端口绑定结果以 Kestrel 实际启动成功为准（同步等待保证"成功即真成功"）。
- 多个服务窗口并存时自动分配不同端口（Start/Start+1/Start+2…）。
- **自动跳过手机浏览器不安全端口**（2026-08-16 定位）：Chromium 内核浏览器（Chrome/Edge/微信/QQ 浏览器等）对内置黑名单端口直接拒绝连接（`ERR_UNSAFE_PORT`），请求根本不发出——表现为"服务正常监听、防火墙已放行、但手机连不上且无任何请求日志/弹窗"。黑名单含 6000（X11，实测真凶）、5060/5061（SIP）、6665-6669/6697（IRC）等，`LanPortProbe` 探测时自动跳过。
- 探测范围与防火墙放行范围关系：防火墙放行 `Start..FirewallEnd`（Start+100）**覆盖**探测范围 `Start..ProbeEnd`（Start+99），保证探测到的端口必然被放行规则覆盖（`LanPortsTests` 守护该不变量）。

### 2.6 局域网 IP 获取（`LanService.GetLocalIPAddress`）

选 IP 策略（避免选中 VPN/隧道/虚拟网卡地址）：

1. **优先**：`Up` 状态、非回环/隧道、网卡描述不含 `VPN/Tunnel/Virtual/Tap/Loopback/WSL` 关键字、且配置了**默认网关**（即实际局域网出口）的网卡的第一个 IPv4（跳过 `169.254.` 链路本地地址）。
2. **兜底**：主机名 DNS 条目中的第一个 IPv4（同样跳过 169.254）。
3. 都没有则返回 `127.0.0.1`。

二维码 URL 中的 IP 即由此得出；窗口启动时显示 `{IP}:{LanPorts.Start}` 预览，启动后显示实际绑定端口。

### 2.7 请求体限量（`ReadBodyWithLimitAsync`）

- 双重校验：先查 `Content-Length` 预检，再分块读取累计计数（防伪造 Content-Length）。
- 图片上传上限 **20MB**（`MaxUploadBytes`），JSON 请求体上限 **1MB**（`MaxJsonBodyBytes`）。
- 超限返回 `413 Payload Too Large`。

### 2.8 嵌入式 HTML 页面

- 页面文件：`Resources/lan_page.html`（人员）、`Resources/hazard_lan_page.html`（隐患）、`Resources/photo_transfer_page.html`（图片传输），csproj 中 `CopyToOutputDirectory="PreserveNewest"` 随发布物拷贝。
- 服务启动时 `LoadEmbeddedHtml()` 从**输出目录**读取（调试时回退到源码目录相对路径；都找不到则返回"页面加载失败"最小页并记日志）。
- 响应头统一 `Cache-Control: no-store, no-cache, must-revalidate`（+ 页面 meta），**禁止手机浏览器启发式缓存**（否则升级后手机端仍加载旧页面）。
- 手机端无需安装任何 App，扫码后浏览器直开（`input[type=file][capture]` 唤起拍照/相册）。

### 2.9 二维码生成

- QRCoder：`QRCodeGenerator` + `PngByteQRCode`，ECC 级别 Q，窗口内 `Image` 显示。
- URL 组成：`http://{IP}:{port}/?业务参数&token={token}`（人员页带 `groupId/groupName/unitName`，隐患页带 `groupId/groupName/yearMonth`）。
- 服务停止 → 二维码隐藏，占位提示显示；重启服务重新生成（token/IP 可能变化）。

### 2.10 临时目录约定

| 服务 | 临时根目录 | 说明 |
|---|---|---|
| 人员录入 | `%TEMP%\LanPersonnel\{tempId}\` | 按 tempId 一个目录，含正反面照片 |
| 隐患录入 | `%TEMP%\HazardLan\{tempId}\` | 按 tempId 一个目录，含隐患/整改照片 |
| 图片传输 | `%TEMP%\PhotoTransfer\` | 单文件，处理后移动/删除 |

- 所有客户端传入标识符（`tempId`/`photoId`）必须通过 `IsValidTempId` 白名单校验（`^[A-Za-z0-9_-]{1,64}$`），**防路径穿越**。
- 删除目录统一走 `LanService.TryDeleteTempDirectory`：先 `Path.GetFullPath` 解析，确认仍位于系统临时目录内才删除。
- 孤儿清理：人员服务 `CleanupStalePending`（暂存 30 分钟 TTL）、隐患服务 `CleanupStaleTempDirectories`（孤儿目录最后写入超 30 分钟删除，启动时 + 每次上传时自愈）、人员提交状态 `CleanupStaleSubmissionStatuses`（30 分钟 TTL 防字典无界增长）。

### 2.11 设备在线检测（`LanDeviceTracker`，2026-08-16 三窗口统一）

HTTP 无长连接，设备"在线/离线"靠心跳判定，三窗口共用 `Services/LanDeviceTracker.cs`（纯逻辑、时间可注入、可单测；定时器与界面刷新由窗口负责）：

- **计入**：任何**授权**请求（携带有效 token，经鉴权中间件）到达时触发 `DeviceConnected(来源IP)` → 记录心跳（IP 去重）。服务重启后旧 token 失效，旧页面请求一律 401 不计入。
- **即时移除**：手机页面 `pagehide` 时 `sendBeacon('/bye?token=...')` 上报 → 服务端触发 `DeviceLeft(来源IP)` → **窗口立即移除该设备**（不再等 20 秒超时）。`/bye` 由基类注册，经鉴权中间件（旧 token 无效）。
- **兜底移除**：beacon 失败/页面强杀时，窗口定时器每 5 秒 `Sweep()` 一次，超过 **30 秒**无任何请求的设备移除（`LanDeviceTracker.Timeout = 30s`）。
- **停止服务**：停止心跳扫描、`Reset()` 清空设备集合（设备集合属于本次服务会话）。
- 窗口显示「已连接设备：x台 │ 已录入：x人/条」两行计数（2026-08-16 由单行「已录入」扩展）。

---

## 3. 人员批量录入服务（LanService）

### 3.1 入口与窗口

- 桌面入口：人员模块对**部门/单位分组右键 → 批量人员录入**，`MainWindowViewModel.ExecuteBatchPersonnelEntry` 打开 `Controls/LanPersonnelEntryWindow`（「手机录入人员信息」，2026-08-16 由 LanEntryWindow 改名）。
- 窗口职责：启动/停止服务、IP 与端口显示、二维码生成/隐藏、**已连接设备 + 已录入两行计数**（`LanDeviceTracker`，见 2.11）、IP 变化提示、请求禁用项目菜单（引用计数，防切换项目导致服务崩溃）。
- 窗口关闭（`Closed`）→ `StopService()` → 释放项目菜单禁用。

### 3.2 路由表

| 方法 | 路径 | 参数 | 说明 |
|---|---|---|---|
| GET | `/` | — | 返回 `lan_page.html`（禁止缓存） |
| GET | `/ping` | token | 心跳：返回 `{ok, groupName, unitName}`；手机端每 3 秒轮询，实时显示"终端服务已启用/关闭"，并同步最新部门信息 |
| GET | `/departments` | token, groupId | 返回同单位下部门列表 `{ok, departments:[{id,name}]}`（供手机端切换录入部门） |
| POST | `/upload` | token, side=`front\|back`, tempId, groupId | 上传一张身份证照片 → OCR → 暂存 → 返回识别字段 |
| POST | `/submit` | token, body=`{tempId}` | 提交暂存记录入后台队列（幂等） |
| GET | `/status` | token, tempId | 查询录入状态：`processing/completed/failed` + 姓名/性别/部门/错误信息 |
| POST | `/discard` | token, body=`{tempId}` | 丢弃暂存记录并删除临时文件 |
| POST | `/bye` | token | **基类注册**（见 2.11）：手机页 `pagehide` 上报设备离开 → 立即从已连接设备移除 |

> 注：`/bye` 由 `LanServiceBase` 统一注册（`MapRoutes` 之外），三个服务路由表相同，后文不再重复列出。

### 3.3 上传与 OCR 链路（`HandleUpload`）

```
校验 side / tempId / groupId（IsValidPersonnelGroupId：必须当前项目 Personnel 模块真实分组）
 → 读取图片（20MB 上限）
 → 存 %TEMP%\LanPersonnel\{tempId}\upload_{时间戳}.jpg
 → _ocrService.RecognizeAsync(filePath)      ← 引擎见 3.5
 → OcrService.ParseFields(blocks)             ← 字段提取（与引擎无关的静态解析）
 → OcrService.DetectIdCardSide(blocks)        ← 自动纠正正反面（正面/背面互拍错位时自动修正）
 → 写入 PendingRecord（FrontResult/BackResult + 图片路径）
 → 触发 PendingUpdated 事件 + 清理过期暂存
 → 返回 {ok, tempId, side, ocr:{name,gender,age,birthDate,idNumber,nativePlace,validFrom,validTo}}
```

### 3.4 提交与后台落库（`HandleSubmit` → `SubmissionConsumerLoop` → `ProcessSubmissionAsync`）

- **幂等入队**：`_submissionStatuses.TryAdd(tempId, {Status="processing"})` 首次提交才成功，重复提交直接拒绝（防止同一 tempId 二次入队导致状态被覆盖）。
- 消费者：`Channel<string>` 无界单读队列，`SubmissionConsumerLoop` 逐条 `ProcessSubmissionAsync`。
- 落库流程：

```
合并正反面 OCR 结果（PendingRecord.GetMergedResult：背面字段覆盖正面空字段）
 → 构建 PersonnelRecord：
    Title=姓名, Gender, Age, NativePlace, BirthDate, IDCardDocNumber,
    IDCardStart=ValidFrom, IDCardEnd=ValidTo, YearMonth=当前月,
    DisplayIndex=当前部门最大 DisplayIndex + 1
 → AddPersonnelRecord
 → MoveTempPhotosToAttachments：
    正面 → 附件槽位 1（idcard_1_{guid}.jpg）
    背面 → 附件槽位 2（idcard_2_{guid}.jpg）
    落位 Project_{id}\Personnel_{单位分组ID}\Card_{记录ID}\，thumb\ 生成 300px 缩略图，
    AttachmentType=IDCard，RelativePath/ThumbnailPath 按数据架构约定写库
 → 附件保存失败 → 回滚删除人员记录（防半成品记录）
 → 更新 _submissionStatuses[tempId] = completed + 姓名/性别/部门/RecordId
 → 触发 RecordConfirmed(groupId, recordId) 事件（桌面端刷新视图）
```

### 3.5 OCR 引擎（`IOcrService`）

接口 `Helpers/IOcrService.cs`，实现二选一，选择逻辑在 `PersonnelRecordCardView.GetOcrService()`（**进程级单例**，桌面端手动添加照片自动填表 `AutoOcrFillAsync` 与 LAN 录入共用同一实例）：

```
new PaddleOcrService() → EnsureInitializedAsync（懒加载本地 V5 中文模型）
  ├─ 成功 → 使用 PaddleOCR V5（Sdcb.PaddleOCR + OpenCvSharp 读图 + Mkldnn 设备，
  │         AllowRotateDetection=true，关 180° 分类提升速度；Run 线程不安全用信号量串行化）
  └─ 失败（初始化异常）→ 降级 WindowsOcrService（包装静态 OcrService，Windows.Media.Ocr 中文引擎，
        SkiaSharp 预处理：<500px 小图 2x 线性放大 + 3x3 锐化卷积核）
```

> 注：README/文档中的"PaddleOCR"是首选引擎；实际运行若 Paddle 模型加载失败会自动降级到 Windows OCR，两种引擎产出统一交给静态 `OcrService.ParseFields` 解析字段。

### 3.6 字段解析（静态类 `OcrService`，与引擎无关）

- 判定证件类型：命中 18 位身份证号（含 X/×）或"公民身份号码/居民身份证"关键词 → 按**身份证**解析；否则按**证书**解析。
- 身份证解析要点：
  - 身份证号：严格 18 位正则 → 校验码校验（`IsValidIdNumber`）→ 不通过则**容错匹配** D/O/S/B 混淆字母 → 最后 17 位自动补齐校验码；识别置信度低时记 Warnings 提示人工核对。
  - 姓名：定位"姓名"标签，前后文 2~4 字中文块 + `IsPlausibleName` 过滤（排除"民族汉""性别男"等复合标签与明显非姓名文本）。
  - 出生/年龄：优先从**校验通过的身份证号**第 7~14 位提取（比 OCR 直识日期更准），年龄按当前年份推算。
  - 性别：块级"男/女"匹配 → 全文包含 → 兜底从身份证号第 17 位奇偶推断（100% 准确）。
  - 籍贯：住址文本正则提取到县区级 + 省名白名单逐字剥离校验。
  - 有效期：日期正则多格式提取（`- . / 年月日`、中文标点、YYYYMMDD 连写、2 位年份），"长期"关键词识别；`℃` 等 OCR 误识符号预处理。
- 证书解析：证件编号关键词标签（证件编号/证书编号/注册编号/NO 等）+ 多级正则兜底（建安/交安格式、T+身份证号等）；有效期排除"初次领证日期/发证日期"等无关日期标签。

### 3.7 手机端页面（`lan_page.html`）

- 顶栏：单位名 + 部门下拉（`/departments` 加载，可切换录入部门）+ 服务心跳指示灯。
- 列表区：每次提交新增一行，实时显示 序号/姓名/性别/部门/状态（处理中→录入成功/失败，`/status` 轮询，最多 30 次 × 2 秒 = 60 秒）。
- 拍照区：正面/背面两个拍照框（`capture` 唤起相机）+ "从相册选择（单次最多 2 张）"。
- 上传：Canvas 压缩（最长边 1200px、JPEG 0.7）后 `POST /upload`，正反面都传完再 `POST /submit`，随后轮询 `/status`。
- 提交后清空拍照框，可连续录下一人。
- 在线检测：每 3 秒 `GET /ping` 心跳；页面关闭（`pagehide`）时 `sendBeacon('/bye')` 上报离开（见 2.11）。

---

## 4. 隐患手机录入服务（HazardLanService）

### 4.1 入口与窗口

- 桌面入口：隐患模块对**年月分组右键 → 隐患手机录入**，`MainWindowViewModel.ExecuteBatchHazardEntry` 把 `2026年06月` 转换为 `2026-06` 后打开 `Controls/LanHazardEntryWindow`（「手机录入隐患数据」，2026-08-16 由 HazardLanEntryWindow 改名）。
- 服务构造绑定固定的 `yearMonth` 与 `groupName`（一个窗口只服务一个年月分组）。
- 不涉及 OCR（表单全部手动填写）。

### 4.2 路由表

| 方法 | 路径 | 参数 | 说明 |
|---|---|---|---|
| GET | `/` | — | 返回 `hazard_lan_page.html` |
| GET | `/ping` | token | 心跳：`{ok, groupName, yearMonth}`（手机端据 yearMonth 补拉日期列表） |
| GET | `/dates` | token, yearMonth | 当月 1~天数日期列表 + `defaultDay`（当月默认今天，非当月默认月末） |
| POST | `/upload-photo` | token, type=`hazard\|remedy`, tempId | 上传隐患/整改照片到临时目录，写入 `_photoCache["{tempId}_{type}"]` |
| POST | `/submit` | token, body 见下 | 提交记录（表单 + 照片标识）→ 落库 |

`/submit` 请求体：

```json
{
  "groupId": "...", "date": 15,
  "location": "配电箱未上锁", "level": "一般隐患",
  "description": "...", "measure": "...",
  "hazardPhotoId": "hzd_...", "remedyPhotoId": "hzd_...",
  "yearMonth": "2026-08"
}
```

### 4.3 提交校验与服务端落库（`HandleSubmit`）

```
校验：hazardPhotoId/remedyPhotoId 格式合法（IsValidTempId）
     location/description 非空
     date ∈ [1, 当月天数]（防越界）
     level ∈ {一般隐患, 较大隐患, 重大隐患}（IsValidHazardLevel，与手机端下拉一致）
     location ≤ 100 字 / description ≤ 500 字 / measure ≤ 500 字
 → fullDate = "{yearMonth}-{date:D2}"
 → GetOrCreateDateGroup(yearMonth, dateDay)：年月分组不存在自动建（"2026年08月"），
    日期分组不存在自动建（"08月15日"），返回分组 ID
 → 构建 Record：
    Title = "[{level}] {location}"
    Content = "隐患描述：...\n整改措施：..."
    Description/Measure/Location/HazardType/HazardLevel 分别落专用列
    Status = "进行"，YearMonth，DisplayIndex = 组内最大 + 1
    ExtraData JSON 存 {HazardLocation, HazardLevel, HazardDescription, Measure, FullDate}
 → AddRecord
 → 照片落库：Hazard 槽位 1（Hazard-1_{guid}.jpg）、Remedy 槽位 2（Remedy-2_{guid}.jpg）
    落位 Project_{id}\Hazard_{yearMonth}\Card_{记录ID}\，thumb\ 缩略图 300px，
    附件保存失败 → 回滚删除记录
 → 删除临时目录 → EntryCount++ → RecordConfirmed 事件 → 返回 {ok, recordId, title}
```

- 手机端 `submitEntry`：本地校验表单 → 生成统一会话 ID（`hzd_{时间戳}_{随机}`）→ 隐患/整改照片分别上传 → 汇总提交 → Toast 提示成功/失败 → 清空表单。

### 4.4 桌面端刷新

`RecordConfirmed` 触发后窗口内：

```
刷新分组树（新创建的日期分组需要显示）：GroupTree.LoadGroups()
重载当前选中分组的记录列表：Records.LoadRecords(当前分组)
刷新状态栏（"xx条记录"）
```

---

## 5. 手机图片传输服务（PhotoTransferService）

### 5.1 入口

- 菜单「办公工具 → 手机图片传输」或托盘「办公工具 → 手机图片传输」→ `Tools/PhotoTransferWindow`。
- 不绑定项目，无"切换项目保护"需求；目标目录由窗口左侧位置列表选择（默认"用户-图片"），支持自定义与拖入。
- 窗口显示「已连接设备：x台」与「已接收：x张」两行计数（`LanDeviceTracker`，见 2.11）。

### 5.2 路由表

| 方法 | 路径 | 参数 | 说明 |
|---|---|---|---|
| GET | `/` | — | 返回 `photo_transfer_page.html` |
| GET | `/ping` | token | 心跳 `{ok}` |
| POST | `/upload` | token, fileName, mtime | 上传一张原图 → 按日期归档落盘 |

### 5.3 上传归档链路（`HandleUpload`）

```
校验 fileName（Path.GetFileName 剥路径 + 扩展名白名单 jpg/jpeg/png/bmp/webp/tiff/tif/gif）
 → 读取原图字节（20MB 上限）
 → 写 %TEMP%\PhotoTransfer\ 临时文件
 → 拍摄时间三层兜底：
     1) EXIF DateTimeOriginal（MetadataExtractor，SubIFD 优先于 IFD0，"yyyy:MM:dd HH:mm:ss" 解析）
     2) 手机文件生成时间 mtime（file.lastModified 毫秒时间戳，ParseMtimeMillis，
        合理范围 1975-12-01（世界第一张数码照片）~ 当前+1 年）
     3) 都取不到 → 上传当天
 → 日期文件夹 "yyyy-MM-dd"（IsValidDateFolder 严格校验，防路径穿越/防未来改动引入原始输入）
 → ImageProcessViewModel.GetUniquePath 防重名（同名自动加序号）
 → File.Move 原图直写（不重新编码，保留 EXIF 等原始信息）
 → PhotoReceived 事件（窗口更新列表：序号/拍摄时间/文件名/大小）
```

### 5.4 手机端页面（`photo_transfer_page.html`）

- 逐张**原图直传**（浏览器不压缩、不转码），服务端不重新编码。
- **每批独立计数**（2026-08-12 修复：原 total 全局累加 vs uploaded 局部归零导致多次选择后完成判断失效）。
- 批量完成 Toast："上传完成：N 张" / "完成：N 成功，M 失败"。
- HEIC 不支持（白名单无 heic），iPhone 需在相机设置选"兼容性最佳"（输出 JPG）。

---

## 6. 更新 IPC（UpdateIpcService）

- 定位：绿色版更新时主程序 exe/dll 被进程占用无法覆盖 → 更新器通过**命名管道**请求主程序"保存数据并退出" → 主程序保存后正常退出 → 更新器继续替换文件。
- 协议：
  - 管道名：`MSpirit.SafetySys.Update`（固定）
  - 命令：一行文本 `save-exit`（不区分大小写）
- 实现：后台线程 `NamedPipeServerStream` 循环监听（`WaitForConnection` → 读命令），收到 `save-exit` 后 `Dispatcher.Invoke` 到 UI 线程执行 `MainWindow.SaveAndExitForUpdate()`（清焦点保存全部记录 + 保存视图状态），主窗口不可用时直接 `Shutdown`。
- 在 `App.OnStartup` 中 `SingleInstanceGuard` 成功、主窗口创建后启动；异常后 `Thread.Sleep(300)` 继续等待下一连接。

---

## 7. 安全设计清单

| 项 | 实现 |
|---|---|
| 访问控制 | 每次启动生成 64 位加密随机 token，拼入二维码；所有请求校验，`FixedTimeEquals` 恒定时间比较 |
| 路径穿越防护 | `IsValidTempId` 白名单（`^[A-Za-z0-9_-]{1,64}$`）；`TryDeleteTempDirectory` 校验解析路径仍在系统临时目录内；日期文件夹 `IsValidDateFolder` 严格校验；文件名 `Path.GetFileName` 剥路径 + 扩展名白名单 |
| 分组越权写入 | `IsValidPersonnelGroupId`：仅允许写入当前项目 Personnel 模块下真实存在的分组 |
| 请求体上限 | Content-Length 预检 + 分块累计读取（图片 20MB / JSON 1MB） |
| 枚举校验 | 隐患级别白名单（一般/较大/重大）、照片类型（hazard/remedy）、年月格式（yyyy-MM 01~12） |
| 文本长度 | 隐患位置 100 / 描述 500 / 措施 500 字上限 |
| 缓存策略 | HTML 响应 `Cache-Control: no-store`，防旧页面缓存 |
| 幂等性 | `/submit` 以 tempId 去重，防重复入队/重复落库 |
| 数据一致性 | 附件保存失败回滚已建记录；提交状态跟踪供手机端查询 |

---

## 8. 生命周期与桌面端集成

- **入口汇总**：人员/隐患分组右键菜单 → 对应录入窗口；「办公工具 → 手机图片传输」/托盘 → 图片传输窗口。
- **项目切换保护**：录入窗口打开时 `RequestProjectMenuDisable()`（引用计数，与导出/导入共用），防止服务运行中切换项目导致数据服务被释放、服务崩溃；窗口关闭时 `ReleaseProjectMenuDisable()`。
- **程序退出**：`AppExitManager.RegisterCleanup(CloseAllLanEntryWindows)` —— 退出时统一关闭所有 LAN 录入窗口（窗口 `Closed` 事件内各自 `StopService()`，避免服务残留占用端口/数据服务）；打开项目管理窗口前也会先关闭所有 LAN 窗口。
- **IP 变化提示**：录入窗口 `Activated` 时检测当前局域网 IP 与绑定 IP 是否一致，不一致则提示"本机 IP 已变化，二维码失效，请停止后重新启动"（状态灯变橙）。
- **多窗口并存**：多个录入窗口可同时运行（人员 + 隐患 + 图片传输互不干扰），端口自动递增分配。

### 8.1 首次使用防火墙放行引导（2026-08-15 新增，方案 B）

> 背景：经实测验证（见 `临时文档/LAN问题根因验证结论-20260815.md`），**LAN 服务可达性由 Windows 防火墙放行规则（Allow/Block）决定**——公用网络默认阻止入站，首次使用 LAN 的用户（无「斜月安全管理系统-LAN」规则）手机必然连不上；而用户群体不具备命令行操作能力，须程序内一键放行。

- **组件**：`Services/LanFirewallService.cs`（检测 + UAC 放行 + 引导流程）、`Views/LanFirewallPromptWindow.xaml(.cs)`（引导窗口）。
- **触发时机**：三个 LAN 窗口（人员/隐患/传图）点「启动服务」时、创建服务前，调用 `LanFirewallService.EnsureFirewallAccess(this)`——**仅在用户主动使用 LAN 时触发**，不用 LAN 的用户零打扰。
- **流程（2026-08-16 重构为「程序级放行 + 原生弹窗」方案）**：
  0. **程序级阻止规则检测**：`HasProgramBlockRule()` 全量扫描中解析 `Program + Direction=In + Action=Block` 且 Program 路径忽略大小写匹配本程序 exe 的规则。根因：Windows 防火墙弹窗（程序首次监听入站时弹出）点「取消」即生成**程序级 Block 规则，且优先于端口级 Allow 规则**（2026-08-15 实测，本机即遗留两条针对 `mspirit.safetysys.exe` 的 TCP/UDP Block 规则）——存在时即使端口已放行手机也连不上，**且存在匹配规则时 Windows 不再弹「是否允许访问」的原生窗口**。命中则弹窗（`LanFirewallPromptWindow(programBlocked:true)`）→ 确认后 `TryRemoveProgramBlockRules()` UAC 执行 `netsh advfirewall firewall delete rule name=all dir=in program="<规则原文路径>"`（逐条删除规则里记录的原始路径，保证精确命中；端口级规则无 Program 字段不受影响）；
  1. **已有放行判定**：`HasAllowRule()`（TCP+Allow+LocalPort 覆盖 `LanPorts.Start` 的端口规则，不限定规则名，按当前 Start 校验——改端口后旧端口规则不误判）**或** `HasProgramAllowRule()`（程序级 Allow 规则，覆盖所有端口、与端口值无关，即原生弹窗点【允许访问】的落盘形态）→ 直接可用；
  2. **无任何放行规则 → 原生弹窗引导**：不预写端口规则（一旦写入规则，Windows 原生弹窗就不会出现）。默认路径：服务启动后**手机首次连接时 Windows 自动弹出『是否允许此应用访问网络？』**，用户勾选网络类型点【允许访问】即生成程序级 Allow 规则（一劳永逸，改 `LanPorts.Start` 也无需重设）；引导窗口同时提供「立即放行」按钮，一键 UAC 写入程序级 Allow 规则（`TryAddProgramAllowRule()`，原生弹窗的确定性等价物，`netsh advfirewall firewall add rule name="斜月安全管理系统-LAN" dir=in action=allow program="<exe>"`）。
  3. 用户选「暂不」/ UAC 取消 → 返回 false，**服务照常启动**，仅 Toast 提示"首次手机连接时在 Windows 弹出的询问窗口点【允许访问】"（不阻断用户）。
- **设计要点**：
  - 规则解析逻辑（`PortListCovers`/`RuleTextCoversStartPort`/`TryGetProgramRulePath`）为纯函数，已配单元测试 `LanFirewallServiceTests`；
  - 程序级规则（Allow 与 Block 均按 Program 路径解析）与端口值解耦：**改 LanPorts.Start 不再需要动防火墙规则**（历史端口规则 5000-5100/6000-6100/9100-9200 等为旧方案残留，无害，程序不主动清理）；
  - **规则名无关（2026-08-16 实测补充）**：检测/删除**均不依赖规则名**，一律按「Program 路径 + Direction + Action / LocalPort 覆盖」判定。原因：Windows 原生弹窗点【允许访问】生成的程序级规则名是**程序描述名「斜月安全管理系统」**（不带 `-LAN` 后缀），而「立即放行」按钮写的是「斜月安全管理系统-LAN」——两种来源都必须识别；同理，清理规则时按 DisplayName 会漏掉原生弹窗那批，须按 Program 路径删（`TryRemoveProgramBlockRules` 已如此）。
- **netsh 输出解析（2026-08-16 修复 P0）**：netsh verbose 输出**随控制台代码页本地化**——中文系统默认 GBK 代码页下标签与值为中文（`规则名称:`/`方向:`/`本地端口:`/`程序:`/`操作:`，值 `入站`/`任何`/`允许`/`阻止`），且文件为 GBK 字节、严格 UTF-8 解码常"碰巧成功"产生乱码（0 标记）。`DecodeNetshText` 现按 BOM 优先 + 候选编码（UTF-8/GBK/UTF-16LE/BE）逐个尝试、**取规则标记（`Rule Name:`/`规则名称:`）最多者**（标记数即解码正确性判据，天然兼容中英文）；解析器全部中英双标签（`SplitRuleBlocks` + 字段正则 + `NormalizeActionValue`/`NormalizeDirectionValue`/`任何=Any`）。另注意 **netsh 中文输出对"仅指定程序、未显式指定 dir"的规则把方向写成单个「入」（U+5165）而非「入站」**（`NormalizeDirectionValue` 需接受单字形态，漏认会导致程序级规则识别失败）。历史故障：硬编码英文 `Rule Name:` 拆分 → 中文输出下全量扫描只拆出 1 块 → 检测全盲（每次启动弹窗、每次放行累积重复规则），已修复并配 GBK/UTF-8/UTF-16 + 中文标签回归测试。
- **局限**：受 GPO 管理（`LocalFirewallRules: N/A (GPO-store only)`）的机器本地规则可能不生效，边缘情况提示用户联系管理员；原生弹窗依赖 `InboundUserNotification` 开启（默认开启），若被 GPO 关闭则弹窗不出现，须用「立即放行」按钮走程序级规则。

---

## 9. 诊断日志（2026-08-15 添加）

> 背景：LAN 功能在作者本机正常、发给他人后不可用，为定位"网络层 vs 应用层"问题添加详细执行日志。日志写入程序目录 `logs\debug*.log`（Serilog，Debug 级全量）。

### 9.1 日志分层与定位方法

| 日志内容 | 前缀 | 定位作用 |
|---|---|---|
| 启动快照：token/端口/绑定 IP/网络类别 | `[Lan]` / `[HazardLan]` / `[PhotoTransfer]` | 服务是否起来、二维码里的 IP 是否正确 |
| 网卡枚举详单：每张网卡 名称/描述/状态/类型/网关数/IPv4/跳过原因 | `[Lan] 网卡枚举` / `[PhotoTransfer] 网卡枚举` | IP 是否选错（虚拟网卡/VPN/WSL） |
| **请求入口日志**：每个到达服务的请求 方法/路径/来源 IP/状态码/耗时 | `[Lan] 请求完成` 等 | **关键**：请求到没到服务（**仅业务路由记；心跳 `/ping`、`/bye` 每 3 秒一次属高频噪音，2026-08-16 起不记**） |
| 401 明细：来源 IP + token 缺失/不匹配 | `拒绝未授权请求` | 鉴权层问题 |
| 各路由处理：上传大小/OCR 耗时/提交入队/落库结果/EXIF 判定 | `[Lan]`/`[HazardLan]`/`[PhotoTransfer]` 内各 Debug/Information | 业务层问题 |
| 停止快照：清理的暂存/缓存数量、端口 | `stopped` | 生命周期确认 |

> **日志瘦身（2026-08-16）**：`MinimumLevel.Debug()` 全量落盘（`App.xaml.cs`，每日滚动保留 7 天）。此前三处高频噪音占据大量行数：① `netsh 输出（文件法）` 成功执行不再记日志（失败/超时/无规则标记自检告警保留）；② 心跳 `/ping`、`/bye` 不记「请求完成」（手机页面常开时是最大长期增量）；③ 环境探测加缓存——`GetCurrentFirewallProfile` 5 秒 TTL（静态缓存，消除每次扫描 spawn netsh monitor 的重复进程与日志）、`LanServiceBase` 实例内缓存局域网 IP 与网络类别（端口探测重试不再重复枚举 42 张网卡，Stop 时清空）；另删除 `GetNetworkCategory` 的注册表分支（非管理员权限下必然失败的死代码，见 10 节）。排障判读所需信号（块数/命中/配置文件判定/网络类别/拒绝未授权）全部保留。

### 9.2 定位判读（测试时对照日志）

1. **启动日志缺失或端口/网络类别异常** → 服务层问题（端口全被占、IP 选择异常）。
2. **有启动日志、但手机扫码后没有任何"请求完成/拒绝"日志** → 请求根本没到服务 → **网络层/防火墙问题**（Windows 防火墙拦截入站、公用网络、AP 隔离、不在同一网段、IP 不对）——排查 `当前网络类别` 是否为公用网络；在受影响的电脑上可先删程序级 Block 规则、再放行本程序（程序级、覆盖所有端口）：`netsh advfirewall firewall delete rule name=all dir=in program="<本程序exe路径>"`、`netsh advfirewall firewall add rule name="斜月LAN" dir=in action=allow program="<本程序exe路径>"`。
2.5. **服务与防火墙均正常（本机能 401 访问）、但手机浏览器直接打不开页面** → **手机浏览器不安全端口黑名单**（Chromium 内核浏览器拒绝连接 6000/X11、5060/5061/SIP、6665-6669/6697/IRC 等，报 `ERR_UNSAFE_PORT`）——请求根本没发出。`LanPortProbe` 已自动跳过黑名单端口（2026-08-16 定位并修复，实测 6100 通、6000 不通）；若手填 URL 测试，避开黑名单端口即可。
3. **有"拒绝未授权请求"** → token 问题（二维码过期/服务重启后旧页面）。
4. **有"请求完成"但无业务日志/业务失败** → 应用层处理问题（按各路由日志继续定位）。

> 说明：`token` 值仅在启动快照与二维码 URL 日志中记录（本地诊断用）；请求入口日志中 token 打码为 `***`。网络类别经系统 COM `NetworkListManager` 读取（尽力而为，读取失败不影响服务）。

---

## 10. 已知问题与演进方向

- **三个 HTTP 服务代码高度同构**（Kestrel 托管 / token / 上传校验 / IP / 端口探测重复实现）：`临时文档/开发-手机图片传输.md`（2026-08-12）约定的公共基类抽取**已完成**（2026-08-16，`Services/LanServiceBase.cs`）：Kestrel 托管骨架 / token 鉴权中间件 / 请求入口日志 / 设备事件（DeviceConnected/DeviceLeft + /bye）/ 启动停止流程 / 共享工具全部上收，三个服务（`LanService` / `HazardLanService` / `PhotoTransferService`）只保留业务路由与钩子（`MapRoutes`/`OnStarted`/`OnStopped`），差异点收敛为 `ApplicationName`/`LogPrefix`/`ServiceDisplayName` 三个抽象属性；同批新增 `Services/LanDeviceTracker.cs`（设备在线跟踪：OnConnected/OnLeft/Sweep/Reset，纯逻辑可单测），三窗口设备计数统一走它。
- **测试覆盖**：`LanService` / `HazardLanService` 已补测试（2026-08-16，`LanServiceTests`：真实 Kestrel 绑定/被占端口异常形态/token 鉴权/Stop 释放端口/纯函数校验；`LanPortProbeTests`：探测范围与捕获集含 SocketException 回归 + **浏览器黑名单端口跳过**；`LanPortsTests`：常量一致性；`LanFirewallServiceTests`：端口/程序级规则解析、中英双标签与 GBK/UTF-16 解码回归；**`LanServiceFlowTests` / `HazardLanServiceFlowTests`（2026-08-16 第 3 步补齐）**：真实 Kestrel + 真实临时 SQLite 的完整业务流——`/upload`（OCR 解析）→ `/submit` 入队 → 后台消费者落库 PersonnelRecord/Record + 附件落盘 + 临时目录清理，以及幂等拒绝（二次提交）、非法部门分组、非法照片类型/日期/级别、缺位置描述、照片未上传附件跳过等错误路径）；`PhotoTransferService` 已有测试（`PhotoTransferTests.cs`：文件名白名单、日期文件夹校验、EXIF 读取、mtime 解析边界、位置服务）。
- **LAN 服务剩余优化点（2026-08-16 梳理）**：
  - **~~`HasAllowRule` 不校验 `Profiles`（网络类型）~~**：已修复（2026-08-16）：判定时校验规则 Profiles 包含**当前活动配置文件**（每轮扫描查一次 `netsh advfirewall monitor show currentprofile`，避免逐块查询的分钟级卡顿），并优先以程序级规则为准（全端口，天然与网络类型无关）。
  - **探测只查绑定、不查网络可达性**：绑定成功 ≠ 入站可达（浏览器黑名单端口已处理；第三方安全软件/公司准入驱动/网络路径仍可能静默拦截）。建议：服务启动后若 N 秒内无任何请求到达（无 `请求完成/拒绝未授权` 日志），Toast/日志提示"手机请求未到达，可能被 Windows 防火墙或第三方安全软件拦截"——把静默失败变成可诊断。
  - **~~`GetNetworkCategory` 读取失败~~**：已定论（2026-08-16 核实）：原注册表 `NetworkList\Profiles` 方案在程序实际运行权限（非管理员，零 UAC 设计）下**必然抛 SecurityException**（该键默认只授予 SYSTEM/管理员读权限，实测 `reg.exe` 同样 Access denied），且语义为"最近连接的网络"而非"当前活动网络"。注册表分支已删除，`netsh advfirewall monitor show currentprofile` 为唯一方案（普通权限可执行、语义准确、带 5s 缓存）。
  - **多窗口并发 netsh 全量扫描**：三个 LAN 窗口各自触发全量扫描（约 0.5s/次，含多次扫描），并发时浪费且可能互相干扰规则操作；可考虑进程内缓存 + 短 TTL 或串行化。
  - **`LanPorts.Start` 建议避开浏览器黑名单端口**：已完成（2026-08-16 定稿为 8000，非黑名单端口，探测无需跳转）。
  - **netsh 文本解析的版本兼容**：`DecodeNetshText`/字段解析依赖 netsh verbose 输出格式与本地化文案，Windows 版本/语言变化可能不匹配——`RunNetsh` 已带"无规则标记"自检告警，回归测试覆盖 GBK/UTF-8/UTF-16 与中英标签。
- 手机端页面为原生 HTML/JS（无框架），与 WPF 桌面端无共享代码；页面改动需同步发布（`Resources/*.html` 随发布物拷贝，**依赖库/资源文件变动须发全量更新包**）。
- OCR 引擎选择（Paddle vs Windows）由首次初始化结果决定并缓存为单例；Paddle 模型加载失败自动降级，用户无感知但识别质量可能差异（Windows OCR 需系统中文语言包）。
- 图片上传上限 20MB、压缩参数（最长边 1200px / JPEG 0.7）为写死常量；如需调整注意手机端与服务端两侧配合。

---

## 11. 关键代码索引

| 职责 | 类/方法 |
|---|---|
| 人员录入服务 | `Services/LanService.cs`（`HandleUpload`/`HandleSubmit`/`ProcessSubmissionAsync`/`MoveTempPhotosToAttachments`） |
| 隐患录入服务 | `Services/HazardLanService.cs`（`HandleUploadPhoto`/`HandleSubmit`/`SavePhotoFromTemp`） |
| 图片传输服务 | `Tools/PhotoTransferService.cs`（`HandleUpload`/`ReadExifShootTime`/`ParseMtimeMillis`） |
| LAN 服务公共基类 | `Services/LanServiceBase.cs`（Kestrel 托管/鉴权/设备事件/共享工具，三服务共用，2026-08-16 抽取） |
| 设备在线跟踪 | `Services/LanDeviceTracker.cs`（`OnConnected`/`OnLeft`/`Sweep`/`Reset`，三窗口计数共用） |
| 更新 IPC | `Services/UpdateIpcService.cs`（`Start`/`ListenLoop`/`RequestSaveAndExit`） |
| 防火墙放行引导 | `Services/LanFirewallService.cs`（`EnsureFirewallAccess`/`HasAllowRule`/`HasProgramAllowRule`/`HasProgramBlockRule`/`TryAddProgramAllowRule`/`TryRemoveProgramBlockRules`/`FindProgramRulePaths`/`PortListCovers`/`DecodeNetshText`）、`Views/LanFirewallPromptWindow.xaml(.cs)` |
| 端口探测 | `Services/LanPortProbe.cs`（`FindAvailable`/`IsBindFailure`/`IsBrowserRestrictedPort`） |
| 录入窗口 | `Controls/LanPersonnelEntryWindow.xaml(.cs)`（原 LanEntryWindow，2026-08-16 改名）、`Controls/LanHazardEntryWindow.xaml(.cs)`（原 HazardLanEntryWindow）、`Tools/PhotoTransferWindow.xaml(.cs)` |
| OCR 接口与实现 | `Helpers/IOcrService.cs`、`Helpers/PaddleOcrService.cs`、`Helpers/WindowsOcrService.cs` |
| 字段解析/正反面检测 | `Helpers/OcrService.cs`（`ParseFields`/`ParseIdCard`/`ParseCertificate`/`DetectIdCardSide`/`IsValidIdNumber`） |
| 引擎选择单例 | `Peoplemanagement/PersonnelRecordCardView.Interactions.cs`（`GetOcrService`） |
| 公用工具 | `LanService.GetLocalIPAddress` / `IsValidTempId` / `ReadBodyWithLimitAsync` / `TryDeleteTempDirectory` / `IsValidYearMonth` / `IsValidHazardLevel` |
| 分组/附件落库支撑 | `Services/ProjectDataService.cs`（`GetOrCreateDateGroup`/`GetPersonnelUnitGroupId`/`GetProjectsDateRoot`/`GetAllGroups`） |
| 手机端页面 | `Resources/lan_page.html`、`Resources/hazard_lan_page.html`、`Resources/photo_transfer_page.html` |
| 入口接线 | `MainWindowViewModel.ExecuteBatchPersonnelEntry` / `ExecuteBatchHazardEntry`、`MainWindow.MenuActions.CloseAllLanEntryWindows` |
