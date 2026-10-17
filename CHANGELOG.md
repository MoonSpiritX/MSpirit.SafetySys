# 更新日志（Changelog）

本项目所有重要变更记录于此。格式参照 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.2.3.0] - 2026-10-17

### 新增

- **程序图标全面更换**：采用新设计的品牌图标（深色圆角底 + 月牙与字母标识），替换沿用的旧月亮图标。正本收敛到单一来源 `MSpirit.SafetySys\Resources\斜月安全管理系统图标256.ico`（含 16/32/48/64/128/256 六档）与 `…256.png`（窗口标题栏用），文件名去掉了原有的空格（避免 pack URI 需 `%20` 转义、MSBuild 需引号两个长期坑）。覆盖范围：主程序 `ApplicationIcon`、**31 个窗口的标题栏与图像引用**、托盘图标、启动器与更新器的 `ApplicationIcon`、MSIX `Assets/` 三张（`Square44x44Logo` / `Square150x150Logo` / `StoreLogo`）。启动器与更新器原先各自持有一份 112 KB 的 ico 副本，现改为引用主工程同一份并删除副本 —— **此后换图标只需同名覆盖 `Resources` 里的 ico 与 png 两个文件后重新构建，31 处 XAML 与三个 exe 自动跟随**。

### 修复

- **导出界面「自定义时间范围」崩溃（`System.NullReferenceException`）**：`Controls/DropdownMenu.xaml.cs` 的 `Close()` 存在**重入缺陷**。根因：`Close()` 先调用 `ReleaseCaptureForAutoClose()` 释放鼠标捕获，`Mouse.Capture(null)` 会**同步把当前 MouseDown 重派发给真实命中元素**，触发调用方自身的 toggle 逻辑再次调用 `Close()`；内层执行完毕把 `_activePopup` 置 null，外层回来继续访问该字段即抛空引用 —— **判空挡不住，因为判空与使用之间字段被重入改写了**。修法：新增 `_isClosing` 重入守卫；`Close()` 改用局部变量持有弹层引用、释放捕获不再读字段；`ReleaseCaptureForAutoClose(Popup?)` 改为接收参数；以 `ReferenceEquals` 做引用比对，若释放捕获期间发生「关旧开新」则不清新弹层状态、不误触发 `Closed`。该控件被 9 处复用（导出界面 4 个菜单含 `ShowRightOf` 两级联动最易触发，另有日期选择器、记录卡片右键菜单等），一处修好全部生效。

### 优化

- **托盘图标按系统托盘尺寸取样**：原 `Icon.ExtractAssociatedIcon` 只能返回 32×32，在 150% 缩放（托盘实际 24×24）下由系统缩放导致边缘发虚；改为从 exe 图标资源按 `SystemInformation.SmallIconSize` 重建，并记录实际尺寸到日志。另说明：托盘图标此前并非「不显示」，而是深色底板上的暗色符号在浅色任务栏上缩到 24px 后几乎糊成一块，肉眼容易误判为缺失（已通过三态截屏差分与 `HKCU:\Control Panel\NotifyIconSettings` 注册表记录取证）。

### 其他

- **版本号 1.2.2.0 → 1.2.3.0**（当时为 csproj 三处 + 本文件 + `AboutWindow.xaml.cs`；同版本内随后收敛为单一来源，见下一条）。提升第 3 段的技术原因：Windows 对「同标识 + 同版本 + 内容不同」的包直接拒绝部署（`0x80073CFB`），而清单 `Version` 第四段按商店要求必须为 0，故覆盖安装只能靠提升第 3 段 —— 本机已安装 1.2.2.0，不提版本无法覆盖安装。
- **版本号收敛为单一来源**：主程序、绿色版启动器（`斜月安全管理系统.exe`）、`Updater.exe` 三个工程的 `Version` / `AssemblyVersion` / `FileVersion` 改由根 `Directory.Build.props` 的 `SafetySysVersion` 统一注入，各 csproj 不再单独定义。此前启动器与更新器长期停在 `1.0.0.0`、`Product` 还写死「（个人版）V1.0」，与主程序的 1.2.3.0 不一致 —— 而绿色版的唯一入口正是启动器，用户右键看到的版本号会直接误导排障。此后升版只改 `SafetySysVersion` 一处；仍需手工同步的是 MSIX 清单 `Identity/@Version`（静态 XML 读不到 MSBuild 属性）与 CHANGELOG / AboutWindow 文本。
- **作者署名统一为「斜月Spirit」**：移除程序内残留的英文名 `AXin`。范围包括 ① 关于窗口「笔名」行与「关于软件」复制文本（`AboutWindow.xaml` / `AboutWindow.xaml.cs`）；② 三个工程的 `<Authors>`（该属性写入程序集 `CompanyName`，即 exe 文件属性里的「公司/作者」栏）；③ **211 个源文件的版权头注释**；④ 根目录 `NOTICE`；⑤ 中英 `README`、`CONTRIBUTING`、隐私政策文档。英文语境署名改用 `MSpirit`。`CHANGELOG` 中 1.2.2.0 的历史记录保留原名（历史事实不改写）。另注：`ImageViewer` 里的 `SlideShowMaxInterval` 含 “axIn” 子串，属大小写不敏感检索的误报，未改动。

## [1.2.2.0] - 2026-10-16

### 其他

- **MSIX 包标识切换为 Partner Center 分配值（上架微软商店准备）**：清单 `Identity/@Name` 由测试期占位名 `MSpiritSafetySys` 改为 **`Spirit.31431668B8E26F`**、`Identity/@Publisher` 由 `CN=MSpirit` 改为 **`CN=6603A24B-BA4D-43EE-B004-2337E6B44574`**、`Properties/PublisherDisplayName` 由 `AXin` 改为 **`斜月 Spirit`**（三者均为 Partner Center「产品标识」页的原样值；官方口径：`@Name` 必须与预留名逐字符一致，`@Publisher` 必须与签名证书 Subject 逐字符一致）。`Properties/DisplayName` 与 `VisualElements/@DisplayName` 保持中文「斜月安全管理系统」不变（用户可见名）。打包脚本 `打包MSIX.ps1` 的 `$CertSubject` 同步改为新 Publisher（脚本检测到已有 `.pfx` 的 Subject 不一致时会**自动重新生成证书**，无需手工删除）。
- **包族名（PFN）随之变更，旧测试包需卸载重装**：`MSpiritSafetySys_4df3rbx12d1x0` → `Spirit.31431668B8E26F_n4fgvp176crcr`（2026-10-16 装包实测值），AUMID 变为 `Spirit.31431668B8E26F_n4fgvp176crcr!App`。原 `shell:AppsFolder\MSpiritSafetySys_4df3rbx12d1x0!App` 一键启动命令随之失效——**稳妥写法仍是用显示名反查**：`$aumid = (Get-StartApps | Where-Object { $_.Name -eq '斜月安全管理系统' }).AppID; Start-Process "shell:AppsFolder\$aumid"`。新签名证书（Subject = `CN=6603A24B-BA4D-43EE-B004-2337E6B44574`）必须先导入**本地计算机级** `TrustedPeople`（用户级无效，见 `MSpirit.SafetySys.Package/README.md` 证书专节）。
- **版本号 1.2.1.0 → 1.2.2.0**（csproj 三处 + 本文件 + `AboutWindow.xaml.cs` 三处同步）。除标记本轮发布工程变更外，另有一个工程目的：Windows 对「**同标识 + 同版本 + 内容不同**」的包直接拒绝部署（`0x80073CFB`），而清单 `Version` 第四段按商店要求必须为 0，故覆盖安装只能靠提升第 3 段 —— 提版本后即可直接覆盖安装，不必每次先卸载。更新日志条目另补记 `[1.2.1.0]` 遗漏的局域网离线判定提速（约 35 秒 → 约 12 秒，该变更实际随 1.2.1.0 发布，`AboutWindow` 此前未转译）。

## [1.2.1.0] - 2026-10-15

### 修复

- **数据目录与日志文件位置调整（微软商店版上架准备）**：
  - **数据目录（仅系统盘的单盘机器）**：无任何非系统盘时的兜底位置由 `%LOCALAPPDATA%\MSpirit.SafetySys.Date` 改为 `%USERPROFILE%\MSpirit.SafetySys.Date`。原因：MSIX（商店安装包）会把程序对 AppData 目录的新建写入重定向到包私有目录，**卸载软件时该目录被一并清空**——只有 C 盘的办公机会因此丢失全部隐患记录与人员档案。有 D/E 盘的机器数据本来就在非系统盘，不受影响。
  - **老用户无感迁移**：记录文件一并移到 `%USERPROFILE%\MSpirit.SafetySys\DataRoot.txt`，启动时自动读取旧位置记录并迁移（旧位置无记录才回落到新位置逻辑），**不会误弹「数据目录丢失」恢复窗口，数据原地承接**。
  - **日志文件**：由「程序安装目录旁的 `\logs\`」改为 `%USERPROFILE%\MSpirit.SafetySys\logs\`。MSIX 下安装目录只读，原位置日志会静默失效（出问题查不到日志）；新位置卸载后仍保留。日志目录不可写时程序降级为不落盘，**不会因日志问题导致无法启动**。
  - 新增 `Services/AppPaths.cs` 统一收敛上述路径；`App.xaml.cs` 中两处重复的日志初始化合并为一处。
  - 单元测试新增 15 例（旧记录回落、自动迁移、路径不落在 AppData 下的守护断言等）。
- **局域网防火墙放行检测：修复「盘符联接」导致的假阴性（MSIX 包内每次都弹引导窗）**：当系统盘不是唯一安装位置时（Windows「新应用安装位置」设到别的盘），系统会把 MSIX 包实体放在 `D:\WindowsApps\<包全名>\`，只在 `C:\Program Files\WindowsApps\<包全名>` 留一个**目录联接**。于是系统按清单 `windows.firewallRules` 自建的放行规则里 `Program` 记的是 `D:` 路径，而进程自报的 `Environment.ProcessPath` 是 `C:` 路径 —— 原来的纯字符串比对必然不等，包内**每次**点「启动服务」都弹手动指引窗（防火墙其实早已放行，手机能连上，只是程序判断不出来）。新增纯函数 `ProgramPathEquals`（`LanFirewallService.cs`）做三级判定：① 原样比对 → ② 路径规范化后比对（统一分隔符、去尾随分隔符）→ ③ **仅在包内运行时**：规则路径的父目录名等于当前包全名且文件名相同。第 ③ 级刻意门控在包内，确保**非打包形态（绿色版/开发实例）行为完全不变**，同时避免把绿色版的遗留放行规则误判成"包内已放行"。新增 `TryGetPackageFullName`（复用既有 `GetCurrentPackageFullName` P/Invoke，不引新依赖）。单元测试新增 15 例（含"盘符联接包内命中"与三个守护用例：非包内不命中、包内但父目录非本包全名不命中、不同目录同名 exe 不命中），全仓 **748 例通过**。
- **局域网「已连接设备」离线判定提速（原约 35 秒 → 约 12 秒）**：手机端心跳改为**仅在页面可见时上报**（判 `document.visibilityState`，并在 `visibilitychange` 回到前台时立即补发一次），切后台/锁屏即停止上报；心跳超时由 30 秒收紧到 **10 秒**、窗口巡检间隔由 5 秒收紧到 **2 秒**。超时值收敛为 `LanDeviceTracker.DefaultTimeout` 单点定义，三个 LAN 窗口改用无参构造（原先各自硬编码一份 `30s`/`5s`）。原实现在页面被浏览器后台节流续跑时设备数会**永不归零**（"假在线"）；关标签页仍由 `/bye` 即时移除，行为不变。取 10 秒的依据：手机端 3 秒一次心跳 → 容忍 3 次连续丢失，足够抗 Wi-Fi 抖动。单元测试新增 2 例（无参构造采用默认超时、超时值落在"抗抖动 vs 响应速度"区间）。

### 其他

- **开源准备（文档与合规）**：补全第三方组件许可清单（漏记的 `MetadataExtractor 2.8.1`；`Hardcodet.NotifyIcon.Wpf` 依据官方仓库更正为 MIT）；中英 README 补界面截图（10 张）与徽章、隐私政策链接；`LICENSE` 还原为官方 AGPL-3.0 原文（此前因额外抬头导致 GitHub 无法识别许可），署名声明移入新增的 `NOTICE`；README 新增「维护状态」声明（个人开发、更新不定期、欢迎 Issue、暂不接受外部 PR）；SECURITY / FAQ / CONTRIBUTING 同步开源口径。
- **文档订正**：大文件行数清单按 2026-10-15 实测刷新（`MainWindow.xaml.cs` 已于 2026-08 拆分为 10 个分部类、主文件 398 行，移出清单）。
- **清理 LAN 防火墙死代码**：删除 `LanFirewallService.TryAddRule()`（端口级放行 8000-8100 的旧实现，2026-08-16 改为「程序级放行」后全仓已无调用点）及仅供其使用的 `EndPort` 常量（端口区间语义统一由 `Common/LanPorts` 提供），同步精简对应单元测试。
- **MSIX 商店版防火墙适配（R3）**：新增运行形态识别 `LanFirewallService.IsRunningPackaged()`（`GetCurrentPackageFullName` P/Invoke），并以纯函数 `DecideAction` 分派放行动作。**包内不再走 UAC 提权路径**——商店不批 `allowElevation`，且该能力语义是"允许应用**启动时**提权"（需配合 exe manifest 的 `requireAdministrator`），与本服务需要的"**运行中按需提权**"不是一回事，原路径在包内表现为"弹窗 → 用户点按钮 → 毫无反应"（比直接说不支持更误导）；现改为手动指引窗（「打开防火墙设置」/「重新检测」/「复制命令」），文案**先让用户用手机试连**——清单规则可能已生效、只是 `Program` 字段逐字符比对未命中（假阴性）。**非打包形态（开发调试、未来商店外分发）行为完全不变**（原路径原样抽为 `EnsureFirewallAccessByElevation`）。三个 LAN 录入窗口在包内不再叠加"等 Windows 原生弹窗"的 Toast，避免与手动指引窗自相矛盾。**新增 12 例单元测试**（`DecideAction` 7 / `IsRunningPackaged` 1 / `TryForceManualPrompt` 4）；另加一个仅开发用的调试开关 `MS_SPIRIT_FORCE_MANUAL_PROMPT`（非空即生效，值 `block` 按阻止形态渲染）——本机已有放行规则时该窗口不会自然弹出，用它跳过检测直接弹窗以目视验证排版文案。
- **MSIX 打包工程落地并完成首个可安装包**：新增 `MSpirit.SafetySys.Package/`（含 `AppxManifest.xml`、`打包MSIX.ps1`、`打包MSIX.bat`、`Assets/` 图标与 `README.md`）。**不依赖 Visual Studio 或已安装的 Windows SDK**——`makeappx.exe` / `signtool.exe` 取自 NuGet 包 `Microsoft.Windows.SDK.BuildTools`。包标识由 `MoonSpiritX.MSpiritSafetySys` 缩短为 **`MSpiritSafetySys`**，签名者由 `CN=MoonSpiritX` 改为 **`CN=MSpirit`**；打包脚本新增两项守卫：已有证书 Subject 与当前签名者不一致时自动重新生成（防"改了签名者却沿用旧 .pfx"）、清单 `Identity/@Publisher` 与证书 Subject 逐字符一致性校验。`MSpirit.SafetySys.Package/README.md` 记录完整流程、证书信任要求（**必须装到「本地计算机」级的 `TrustedPeople`/`Root`，装到「当前用户」级无效**）、常见错误码与已知坑。
- **MSIX 签名补齐 RFC3161 时间戳**：此前 `signtool sign` 只带 `/fd SHA256`，签名里没有时间信息（包属性页显示「时间戳：不可用」）。后果是官方明确的硬行为——**证书过期后该包无法再安装**（已安装的实例仍可继续运行，不受影响）。现打包脚本默认加盖时间戳（`/td SHA256 /tr http://timestamp.digicert.com`），TSA 不可达时自动回退为不带时间戳重签并在结尾把"本包无时间戳"明确打出来（不静默降级）；新增 `-NoTimestamp`（离线应急）与 `-TimestampServer <url>`（换源）两个参数；签名后从签名里读回时间戳状态写入汇总行。上架微软商店不受此影响（商店用自己的证书重新签名），受益的是自签分发与将来可能走国内平台的分发。
  > 包名口径：2026-10-15 曾考虑把 `Identity/@Name` 再改成两段式占位名（`MSpirit.MSpiritSafetySys`）以贴近商店预留名的形态，**权衡后维持 `MSpiritSafetySys` 不变**——官方要求提交时的包名必须与 Partner Center **预留名逐字符一致**，而预留名只能预留后原样复制，任何自拟名都要在提交前替换；提前改只会白付一轮「卸载旧包 + 重导证书 + 重装」的验证成本。
- 源代码仓库已于 2026-10-15 转为公开：<https://github.com/MoonSpiritX/MSpirit.SafetySys>（AGPL-3.0）。

### 2026-10-16（MSIX 打包完善：桌面快捷方式 / 包体积裁剪 / 工程加固）

- **MSIX 包新增桌面快捷方式（Win11 生效，Win10 按设计跳过）**：清单新增 `desktop7:Extension / windows.shortcut`（声明在 **`<Application>` 级**，与必须在 `<Package>` 级的 `firewallRules` 相反），装包时由系统在**当前用户桌面**创建「斜月安全管理系统.lnk」。官方 schema 标注该元素**最低 OS Build 19645**，而 Windows 10 最高只到 19045——若据此提高 `TargetDeviceFamily/@MinVersion` 等于**把所有 Win10 用户排除在外**，故**刻意保持 MinVersion = `10.0.17763.0` 不动**，并把 `desktop7` 写进 `IgnorableNamespaces`：Win10 忽略该扩展，结果是「**装得上、但没有桌面快捷方式**」（2026-10-16 明确的取舍）。另实测「从开始菜单『所有应用』拖到桌面」在 Win11 **拖不动、不生成快捷方式**，故未采用该备选路。
- **裁剪卫星程序集，包体积小幅下降**：主工程新增 `<SatelliteResourceLanguages>zh-Hans;zh-Hant;en</SatelliteResourceLanguages>`，`cs` / `de` / `es` / `fr` / `it` / `ja` / `ko` / `pl` / `pt-BR` / `ru` / `tr` 共 11 种语言不再生成卫星程序集目录。MSIX 包 **261.5 MB → 256.4 MB**（−5.1 MB），包内语言目录 **14 个 → 2 个**（`zh-Hans` / `zh-Hant`；英文是中性资源，直接编进主程序集，没有独立目录）。作用域覆盖**全部**发布形态，绿色版同样受益。降幅有限的原因：卫星程序集本身很小，包体积大头是 .NET 运行时与 PaddleOCR / ONNX Runtime / OpenCV / SkiaSharp 的原生库。
- **打包工程加固（口令外置 / 忽略规则兜底 / 路径去本地化）**：`打包MSIX.ps1` 新增证书口令三级解析（`-CertPassword` 参数 > 环境变量 `MSIX_CERT_PASSWORD` > 本地口令文件 `bin\MSIX.Cert\pfx.password`），并在「当前无现成证书」时自动生成随机口令并落盘（首次打包免手工配置）；`.gitignore` 补 `*.pfx` / `*.pvk` / `*.snk` / `*.password` 兜底规则；`MSpirit.SafetySys.Updater` 的发布配置 `FolderProfile.pubxml` 把本机绝对路径改为基于文件位置的相对路径（公开仓库不再暴露本地目录结构）。`MSpirit.SafetySys.Package/README.md` 同步新增「桌面快捷方式」「包体积与语言资源裁剪」两节与两条已知坑。**注**：脚本 `param()` 里的旧明文默认值尚未清空（会短路上述三级解析），清空方式见该 README 已知坑第 10 条。

## [1.2.0.0] - 2026-08-17

> 本版合并 2026-08-14 ~ 08-17 的未发布内容（按日分段保留原分类）。

### 2026-08-17（马赛克预览修复）

#### 修复

- **马赛克标注预览真实像素化（不再显示半透明灰块）**：`CropOverlay.Annotations` 的马赛克预览占位由"半透明灰色块 + 深色边框"改为**真实马赛克效果**——从预览图抠出标注区域、线性降采样（≈块内取均值）再以 NearestNeighbor 放大回显示尺寸，块数规则与输出端 `AnnotationRenderer.DrawMosaic` 一致（宽高各约 1/12），预览所见即输出所得；区域保留 1.2px 深色描边便于看清范围，无预览图/像素化失败时回退原占位块。交互方式不变（仍为拖拽矩形区域）。

### 2026-08-16（标注与剪裁 + LAN 强化）

#### 新增

- **手机录入窗口改名 + 标题同步**：`LanEntryWindow` → `LanPersonnelEntryWindow`（「手机录入人员信息」）、`HazardLanEntryWindow` → `LanHazardEntryWindow`（「手机录入隐患数据」），文件名与标题一致，避免混淆。
- **已连接设备实时计数（三窗口）**：`Services/LanDeviceTracker.cs` 统一设备在线跟踪——手机页面每 3 秒心跳 `/ping`、`pagehide` 时 `sendBeacon('/bye?token=...')` 即时上报离开（窗口**立即**移除，不再等 20 秒超时），30 秒无心跳兜底移除（beacon 失败场景）；窗口显示「已连接设备：x台 │ 已录入：x人/条」两行计数（原仅「已录入」单行）。授权请求才计入（服务重启后旧 token 不计入），停止服务时归零。
- **防火墙放行引导升级（P0 修复，2026-08-15 方案 B 的完成形态）**：
  - **程序级规则放行优先**：存在程序级 Block 规则（防火墙弹窗点「取消」的落盘形态，优先于端口级 Allow）时先检测并引导删除（`LanFirewallPromptWindow(programBlocked:true)`）；无任何放行规则时不再预写端口规则（避免抑制 Windows 原生弹窗），引导用户首次连接时点【允许访问】或一键「立即放行」（UAC 写入程序级 Allow，规则名无关，改端口无需重设）；
  - **netsh 输出解析修复（块数=1 检测全盲）**：中文系统 GBK 代码页下 netsh verbose 输出为中文标签+GBK 字节，原硬编码英文 `Rule Name:` 拆分导致只拆出 1 块、检测全盲（每次启动弹窗、放行累积重复规则）。`DecodeNetshText` 改为多编码择优（UTF-8 严格/GBK/UTF-16LE/BE，取规则标记最多者）+ 中英双标签解析；兼容「入」单字方向值（U+5165，程序级规则常见形态）；
  - **`HasAllowRule` 校验网络类型**：判定放行时校验规则 `Profiles` 包含当前活动配置文件（每轮扫描查一次 `netsh advfirewall monitor show currentprofile`，避免逐块查询卡顿），域/专用规则不再误判为公用网络已放行。
- **Toast 服务改进**：LAN 相关提示统一 3 秒时长、支持多行（TextWrapping + 最大宽度），ToastService 改为 `PlacementMode.Center` 原生居中（修复多屏/DPI 下位置漂移）。
- **端口定稿 8000**：`LanPorts.Start = 8000`（避开手机浏览器黑名单端口，如 6000/X11——Chromium 内核浏览器直接拒绝连接，表现为"服务正常但手机打不开"）；`LanPortProbe` 探测时自动跳过黑名单端口。

#### 标注与剪裁（图片处理窗口，2026-08-16 强化）

- **改名「图片剪裁」→「标注与剪裁」**：导航按钮与 Toast/日志文案统一。
- **标注功能（阶段 5 首版）**：六种标注工具（矩形/圆形/直线/箭头/文字/马赛克），工具栏「标注」+ 6 个 Path 矢量图标按钮（左对齐），选区信息右对齐；**标注与裁剪一体**（Spirit 模式）：标注对象存原始像素坐标（矢量方案），缩放/平移仅显示层变换；选择/裁剪模式下点击标注可选中（1px 高亮边框 + 右上角 ✕ 删除）、拖动移动、Delete 键删除；文字标注就地输入框（微信同款）；马赛克为矩形区域像素化（方案 A：区域降采样再放大）。
- **「应用剪裁」→「应用」**：统一应用裁剪与标注——执行链路「解码全尺寸 → 重绘标注（AnnotationRenderer 六类型 + 马赛克）→ 裁剪选区 → 输出」。
- **新增组件**：`Annotation`（对象模型/命中测试/平移）、`AnnotationTool`（状态机：创建/选中/移动/删除/文字提交）、`AnnotationRenderer`（SKCanvas 六类型绘制 + 马赛克区域模糊）、`CropOverlay.Annotations`（标注视觉层 partial：渲染/选中框/✕/文字输入）；CropOverlay 事件统一分派（绘制模式 → 标注，默认 → 标注命中优先、未命中走裁剪）。
- **新增 22 个单元测试**（Annotation 命中/归一化/平移、AnnotationTool 六类型创建/选中/移动/删除/清空/文字提交、AnnotationRenderer 像素输出/箭头头/文字字形/马赛克块化/越界裁剪）。
- **裁剪比例扩展**：`自由 / 1：1 / 3：4 / 4：3 / 9：16 / 16：9` 六档。
- **修复：裁剪选区位置偏移（严重）**：`CropOverlay.GetPixelSelection` 原把"相对图片显示区域"的裁剪框坐标直接传给期望"控件坐标"的 `DisplayRectToPixelRect`，非满幅（Stretch=Uniform 留白）时像素选区整体向左上偏移 offset/scale——裁出的图与框选位置不一致。新增 `CropGeometry.RegionRectToPixelRect` 把"加 offset"收进函数内（坐标域由签名强制），补 2 个回归测试。
- **修复：固定比例下拖"边"无法向外扩大**：`CropRegion.ResizeTo` 原比例约束只有"收缩主导维"分支（向外拖时被拖维缩回原值），只能缩小不能扩大、只能靠角。改为"按被拖边决定主导维、另一维按比例联动"（锚定边固定、越界反向收缩），补 3 个回归测试。
- **修复：裁剪框内平移失效 + 悬停无 4 向箭头**：根因是 `OverlayCanvas` 未设 `Background`——WPF 中 Panel 背景为 null 时空白区不参与命中测试，选区内部点击穿透到下层 Image，鼠标事件不路由到裁剪层。设 `Background="Transparent"` 后选区内部可按住平移（4 向箭头）。
- **修复：遮罩错位（灰块盖留白/漏留白/右侧边框位置不对）**：`CropOverlay.Render` 遮罩四块外边缘未对齐图片显示区域边界（左/上盖住留白、右/下漏出留白）。统一以图片显示区域为界重算四块遮罩；并移除预览区米白底 `#F5F7F9`（改透明，留白显示窗口底色）。
- **修复：另存原目录/覆盖模式下「应用并下一张」不跳转 + 状态列不显示**：`RefreshGroup` 会重建列表项实例，原按引用查找/标记旧实例失效。改为按文件路径定位刷新后的新实例（状态标记 + 下一张跳转）。
- **新增：拖入自动选中**：拖入 1 张 → 自动选中并显示；多张 → 按**自然排序**（数字感知 `NaturalStringComparer`：a2 < a10）选第一张；组内排序统一改自然序。
- **新增：右上角位置指示**：`第 x/x 张`（当前选中/总数，跨分组列表顺序；未选中显示 `共 x 张`），替代原「总计：x 张」。
- 裁剪测试 45 → **59 例全绿**（含上述回归），全套 677 例无回归。

#### 其他

- **LAN 服务公共基类抽取**（`Services/LanServiceBase.cs`）：Kestrel 托管骨架 / token 鉴权中间件 / 请求入口日志 / 设备事件（DeviceConnected/DeviceLeft + /bye）/ 启动停止流程 / 共享工具（IP 获取、网络类别、请求体限量、临时目录清理、标识符校验）全部上收，三服务（`LanService`/`HazardLanService`/`PhotoTransferService`）只保留业务路由与钩子；差异点收敛为 `ApplicationName`/`LogPrefix`/`ServiceDisplayName` 三个抽象属性。原 `StartOnAvailablePort` 同步收敛为 `LanPortProbe.FindAvailable`。
- **LAN 业务流单元测试补齐（654 → 664 全绿）**：`LanServiceFlowTests`（4 例：上传→OCR→提交→落库人员记录+附件落盘、未上传即提交被拒、非法部门分组、重复提交幂等只落一条）、`HazardLanServiceFlowTests`（6 例：上传照片→提交→落库隐患记录+附件落盘、非法照片类型/日期/级别、缺位置描述、照片未上传附件跳过）；`LanDeviceTrackerTests`（11 例：心跳去重/离开即时移除/超时兜底/重置，时间注入）。

#### 性能与优化

- **LAN 日志瘦身**（`MinimumLevel.Debug()` 全量落盘 + 每日滚动保留 7 天，此前高频噪音占日志行数 80%+）：① `RunNetsh` 成功执行不再输出 netsh 原文（失败/超时/无规则标记自检告警保留，块数/命中/配置文件判定等结果日志仍在）；② 心跳 `/ping`（手机端每 3 秒一次）与 `/bye` 不记「请求完成」入口日志（业务路由照记，排障判读"请求到没到服务"不受影响）；③ 环境探测加缓存——`GetCurrentFirewallProfile` 5 秒 TTL（消除每次规则扫描 spawn netsh monitor 的重复进程与日志）、`LanServiceBase` 实例内缓存局域网 IP 与网络类别（端口探测重试不再重复枚举网卡，Stop 时清空）。排障所需信号（拒绝未授权、块数/命中、网络类别、启动/停止快照、业务处理日志）全部保留。
- **删除 `GetNetworkCategory` 注册表分支**（2026-08-16 核实）：`HKLM\...\NetworkList\Profiles` 在程序实际运行权限（非管理员）下必然抛 `SecurityException`（该键仅授予 SYSTEM/管理员读权限，实测 `reg.exe` 同样 Access denied），注册表方案是每次启动必失败的死代码，且其"最近连接的网络"语义不如 netsh 的"当前活动配置文件"准确；现 netsh monitor 为唯一方案（含 5s 缓存）。

### 2026-08-15（数据根保护 + 图片剪裁）

#### 新增

- **数据根定位与找回（拔盘/盘符变化防护）**：`DataRootLocator` 启动决策链——上次记录（`%LOCALAPPDATA%\MSpirit.SafetySys\DataRoot.txt`，独立于数据根不随数据丢失）→ 浅扫所有固定盘根+LOCALAPPDATA → 首启用默认路径；疑似丢失时弹 `DataRootRecoveryWindow` 引导（手动选择目录 / 全盘深扫异步可取消 / 使用新位置继续）。数据根改为**启动确定一次 + 全局共享**（`AppServiceProvider.GetDataPathService()`），15 处 `new DataPathService()` 调用统一收敛，杜绝读写漂移到不同数据根。
- **图片处理窗口「更多功能」页**：左侧导航移除「格式及压缩」「EXIF/OCR」「证件照生成」三个占位选项（保留「方向转换」「图片剪裁」），新增「更多功能」页介绍后续待开发功能（格式及压缩=规划中、EXIF/OCR=重新设计中、证件照生成=开发中），界面不再有孤立灰色占位。
- **图片剪裁功能（完整实现）**：`ImageProcessWindow`「图片剪裁」面板从占位变为可用——按文件夹分组的图片列表（单选、可折叠）+ 预览区裁剪交互（自绘裁剪框：8 手柄拖拽缩放、自由/1:1/4:3/16:9 比例、边界钳制、最小尺寸、遮罩挖孔、状态栏实时像素选区）。EXIF 摆正后预览/裁剪（手机照片所见即所得）、预览降采样执行全尺寸。输出三档下拉：**另存至原目录**（`原名_Edit.ext`，默认）/ **另存至其他位置**（选目录）/ **覆盖原图**（原图移回收站 + 输出 `_Edit` 文件，写成功后删原图不丢数据）。保存后**文件夹级刷新**（仅重扫变更目录分组）、「应用并下一张」连续裁剪。新增组件：`ImageProcessCropViewModel`（分组/预览/执行）、`CropRegion`（裁剪框状态机）、`CropGeometry`（坐标换算）、`CropImageProcessor`（解码摆正/裁剪）、`CropOverlay`（裁剪交互控件，已入拖拽白名单）。**新增 45 个单元测试**（分组构建/添加三来源/坐标换算/状态机/摆正裁剪/文件夹刷新，其中抓出 `using var + return` 提前释放 native 位图导致 AccessViolation 的致命模式并修复）。

#### 修复

- **数据根选盘算法通用化**：由"仅认 D 盘"改为"第一个可用的非系统固定盘（字母序，排除系统盘与可移动盘）"，仅有 E/F 盘的电脑不再错误回退到 C 盘；`DataPathService` 新增显式路径构造（测试/恢复注入用）。
- **新建项目重名检查先比较后 Trim**（测试发现）：`ConfigManager.AddProject` 原在重名检查前未 Trim 名称，"同名项目 "（尾随空格）可绕过重名检查创建重名项目。已改为先 Trim 再判重。
- **删除项目文件夹清理不可靠**（测试发现）：`ConfigManager.DeleteProject` 原用默认连接池连接执行 checkpoint，`Close()` 后连接回池仍持有 data.db 文件句柄 → `Directory.Delete` 必然失败，项目文件夹只能等下次启动清理（残留数百 MB 附件）。已改为先 `ClearPool` 清残留池连接、再用 `Pooling=False` 连接执行 checkpoint（与 `ArchiveService.ReleaseDatabase` 一致），Close 后立即释放句柄，删除项目时文件夹即时清理。
- **人员统计在无 UI 环境静默丢失数据**（测试发现）：`PersonnelStatisticsViewModel.LoadDataCore` 用 `dispatcher?.Invoke(...)` 回填数据，`Application.Current` 为 null（无 UI 环境/后台）时 lambda 被跳过、统计结果不落 `Rows`。已改为 dispatcher 为 null 时直接执行，保证任何环境下数据完整。
- **空 catch / 无日志 catch 全面清理（ISSUES 第 2 条解决）**：全项目 22 处清理——5 处真空 catch（服务 Dispose/DETACH/停止等"尽力而为"清理路径）与 17 处无类型 catch（解析失败回退/错误响应/图片尺寸读取等）全部补充 `Log.Debug`（含上下文信息），错误可追溯；清理后全项目无空 catch、无"无日志 catch"。ISSUES 第 2 条移除并重编号。
- **Excel 导出模板旧数据删不干净（对称漏改）**：`ExcelSmartExporter.FindLastDataRow` 仍是 08-14 导入侧修复前的"底部关键词优先"旧逻辑——模板示例数据单元格以"日期/审核/注："开头时被误判为底部说明行，数据区边界提前结束，示例行不删除、导出文件残留模板占位行。已同步为"数据行优先"（映射列判定、排除序号列、非数据行有内容即边界），与 `ExcelSmartImporter` 完全一致。
- **Word 导入同一单元格多图损坏**：`WordSmartImporter.ExtractEmbeddedImages` 原将同一单元格多张图的字节 `Buffer.BlockCopy` 拼接成单个数组写出，产出损坏图片；已改为 `List<byte[]>` 每张图单独写文件。

#### 其他

- **A 层导入导出链路单元测试建立**：新增 `MappingDictionaryServiceTests`（别名匹配/归一化/模块过滤/版本迁移）、`ImportManagerTests`（Validate/预览文案/分派/委托）、`ExcelSmartImporterTests`（表头识别/列映射/**FindLastDataRow「数据行优先」回归**/嵌入图片/纯图片行）、`ExcelSmartExporterTests`（数据写入/**导出侧同步修复回归**）、`HazardDataWriterTests`（自动建组/DisplayIndex/标题生成/日期解析/图片附件落库）、`DataPathServiceTests`（选盘算法）、`DataRootLocatorTests`（决策链/记录/校验/深扫）。单元测试总数 **121 → 271 全绿**；测试文档全部程序化动态生成（ClosedXML/OpenXml），零二进制资产入库。
- **B 层数据安全测试建立**：`ConfigManagerTests`+`MasterDbContextTests`（项目 CRUD/排序/ValidateCurrentProject 文件夹丢失恢复/**Order→SortOrder 旧列迁移**）、`ArchiveServiceTests`（.msarch 导出/检查/四策略导入/**zip slip 防护**/**Groups.ProjectId 重写**）、`BackupServiceTests`（备份完整性/毫秒时间戳/**30 天 + 100 份轮换**）、`DatabaseHelperTests`（**BUSY 重试**/异常透传）、`StartupCleanupServiceTests`（待清理目录/孤儿目录/空卡片目录，含锁文件删除失败保留记录）。
- **C 层纯逻辑测试建立**：`ExtraConverterTests`（剩余 6 个转换器）、`SolarTermHelperTests`（节气季节键）、`PersonnelOrderingServiceTests`（收集/排序/过滤）、`PersonnelHelperTests`（FormatValidPeriod/行构建/互逆）、`ThumbnailMigrationTests`（缩略图迁移/幂等）、`ConfigServicesTests`（FavoriteLinks/CommonHazardType/SettingFile/Settings）、`ExportDataProviderTests`（隐患/人员数据提供者取值）。
- **D 层统计与分析测试建立**：`MonthlyStatisticsViewModelTests`（月度台账行：状态计算 完成/超期/进行/无效/空、照片计数、序号、日期排序、无期限回退+3天）、`ImageViewerHelperTests`（图片格式判断、模块图片索引构建/缩略图排除/排序）、`AnalysisViewModelTests`（当月统计聚合：统计卡片/类型分布/完成率/摘要文本/记录列表状态/月度趋势/图表构建/空数据归零）、`PersonnelStatisticsViewModelTests`（单位收集/在场优先排序/状态类别过滤/证书槽位/**dispatcher 修复回归**）。
- **A 层 Word 导入导出测试建立**（A 层最后一块拼图）：`TestInfra/WordDocBuilder`（OpenXml 程序化生成带表格/图片的 docx，零二进制资产）；`WordSmartImporterTests`（表头识别/**"标签+内容"排版表排除**/多 Run 表头/**跨表合并**/行数不一致警告/嵌入图片/**同单元格多图独立文件回归**/GridSpan/边界）；`WordSmartExporterTests`（基本导出旧数据删除/多表独立处理/**底部说明行保留**/NoImage 不嵌图/图片导出/缺失图片不失败/空模板行为守护）。
- 单元测试总数 **271 → 457 全绿**。

### 2026-08-14（导入修复 + 测试体系）

#### 修复

- **Excel 智能导入底部行误判（数据静默丢失）**：`ExcelSmartImporter.FindLastDataRow` 原逻辑先判"底部说明行"（单元格以 填表/审核/日期/注：/编制/审批 开头或含"单位负责人"）后判数据行，导致数据单元格以这些词开头（如"日期牌脱落未更换"）时被误判为底部说明行、**其后的数据行全部截断**。已改为**数据行优先**：行内任一映射列（序号列除外）有文本或嵌入图片即为数据行，底部关键词不再生效；仅非数据行且行内有内容才视为结构边界停止读取。实测 3 行数据由"只导入 1 行"恢复为 3 行；20 份导入测试文档全量回归 0 无效行。
- **隐患/整改照片别名歧义**：`MappingDictionaryService` 中"整改前照片/整改前图片"同时是 `HazardPhoto` 与 `RemedyPhoto` 的别名，按列表顺序落到 隐患照片，表头意图无法表达——已从 `RemedyPhoto` 移除该两别名（仅保留于 `HazardPhoto`）；并补全 `Status` 缺失别名（整改状态、治理状况）。运行时字典 `MappingDictionary.json` 已同步（需重启程序生效；老用户字典升级仅影响新建，此处为直接同步）。
- **人员导入部分字段不落库（ISSUES 第 5 条）**：`MappingDictionaryService` 映射字段与 `PersonnelDataWriter` 落库字段名不一致，导致「身份证有效期」「证书编号」「证书有效期」「三类人员」「特种作业」5 个字段可识别但不落库。已改为写入端对齐映射字段：有效期区间（"2021-01-01 至 2041-01-01" 等）经 `PersonnelHelper.TryParseValidPeriod` 解析为起止日期（支持 至/~/连字符分隔、yyyy-MM-dd/yyyyMMdd/yyyy.MM.dd/中文日期 等格式，解析失败原样保存至起始字段并记日志），证书编号改读 `CertNumber`，三类人员/特种作业改读 Display 字段。**补充修复**：证书信息除单数字段外同步写入 `Cert1` 槽位（卡片 UI 证书栏读取 `Cert1Name/Cert1DocNumber/Cert1Start/Cert1End`，原写入的单数字段界面不显示）；证书附件创建处旧键名（`CertDocNumber`/`CertStart`/`CertEnd`）同步修正为映射字段与解析结果。新增 14 个单元测试全绿；用「人员导入功能测试文档 Excel01.xlsx」端到端验证 7 人 5 字段 + 证书槽位全部落库，并已由作者实机导入验证通过。
- **导入后主界面不即时刷新**：`GroupTreeViewModel.LoadGroups` 重建分组树后**不恢复选中节点**（`SelectedNode` 保持不变 → 不触发记录列表重载），且隐患模块导入完成后未调用任何刷新 → 导入完成回到模块界面时分组树/右侧卡片不显示新数据，需切换模块才更新。已修复：`LoadGroups` 树重建后按 Id 恢复原选中节点（仍在树中则触发 `SelectedNode` setter 自动重载记录列表；已删除则清空选中），一处修改使所有调用处（导入/LAN 录入/切项目等）行为一致；隐患导入完成后补调 `LoadGroups`（`ExportWindowViewModel.ExecuteStartImportAsync`），人员导入原有调用自动受益。

#### 其他

- 导入/导出测试文档体系建立：人员、隐患各 5 Word + 5 Excel（`数据导入功能测试文档`），责任人沿用《三体》花名、业务内容为实体行业场景；人员文档为适配"数据行优先"判定新增"序号"装饰列（不映射字段），签名行 merge 起始列统一落在序号列；落款统一"1379号监听员"。
- 文本措辞统一（用户可见提示不再使用"匹配字段"等技术词）：导入预览弹窗改「检测文档内容：识别到 X 个字段」，表头识别失败提示改「未检测到有效的表头行（文档内容至少需两个字段）」；使用提示「字段匹配说明」→「字段识别说明」，关于窗口/人员预览/OCR 弹窗中的"智能匹配/识别字段"统一为"智能识别/识别信息"。
- 清理半成品代码残留：`Controls/FluentPopup.cs`、`Controls/WindowMaterial.cs`、`MainWindow（备份版本）.xaml` 已被删除（作者手动清理），`MS_SafetyManagementSystem.csproj` 中对应的排除项（Compile/Page Remove）同步移除；ISSUES 第 3 条移除，README（中英）"已知事项"同步更新。

## [1.1.0.0] - 2026-08-12

### 新增

- **统一退出管理器** `AppExitManager`：所有退出路径（托盘/更新器/单实例/查看器子进程）统一入口，防重入、钩子异常隔离、任意线程可调度；新模块退出清理只需 `RegisterCleanup` 登记。
- **设置菜单「关闭程序」**：`ExitConfirmWindow` 提示窗口，提供「最小化到托盘 / 退出程序」双选择（复用退出管理器）。
- **托盘右键菜单多级化**：显示主窗口 / 办公工具（图片处理、手机图片传输）/ 信息官网 / 外部AI / 快捷网址，悬停展开二级菜单，链接与主菜单共用 `FavoriteLinksService` 数据。
- **手机图片传输**（办公工具新功能）：手机通过局域网把图片原图传输到电脑选定位置，**按拍摄日期自动归档**（`yyyy-MM-dd` 文件夹）；默认位置=用户-图片、支持自定义位置列表与文件夹拖入；新增 `MetadataExtractor 2.8.1` 依赖读取 EXIF 拍摄日期（与斜月图片查看器独立版同版本，为未来整合铺路）；日期三层兜底 EXIF → 手机创建时间（`mtime`）→ 上传当天；手机页含终端服务心跳指示灯、上传完成 Toast、批量上传。
- **窗口内容区拖拽平移**（程序特色）：`WindowDragHelper` 附加属性统一实现，接入 7 个窗口（手机图片传输/图片处理/使用提示/关于软件/项目及存档管理/常用隐患类型管理/常用链接管理）；交互控件白名单自动豁免。
- 方向转换面板空列表水印提示、执行前状态复位（第二次执行不再残留上次结果）。

### 修复

- 托盘退出路径缺"清焦点 + 保存全部记录"，最后编辑可能丢失（由退出管理器统一钩子修复）。
- `CloseAllLanEntryWindows` 遗漏隐患录入窗口（已补，退出时 LAN 服务残留风险消除）。
- **多 LAN 服务端口都显示 5000**：`StartAsync` fire-and-forget 导致端口绑定失败不感知，探测机制失效——改为同步等待启动（失败即抛），多服务端口自动递增 5000/5001/5002。
- **手机端页面不更新**（启发式缓存旧版 HTML）：三个 LAN 服务 `ServePage` 统一加 `Cache-Control: no-store` + 页面 meta 双保险。
- **手机端 Toast 不显示**：多次选择后完成计数失效（total 全局累加 vs uploaded 局部归零），改每批独立计数。
- 方向转换水印 XAML 运行期崩溃（`Fill="None"` 不被 BrushConverter 识别，须用 `Transparent`）。
- 手机图片传输窗口启动按钮不可点击（双按钮重叠且停止按钮挡点击，改 Visibility 切换）。
- 常用隐患类型窗口 30 个类型项点击被拖拽吞掉（`Border+MouseLeftButtonDown` 模拟按钮改为真 `Button`）。
- 窗口拖拽空白区失效（根 Grid 缺 `Transparent` 背景；ListBox/ScrollViewer 吞冒泡事件，改隧道事件 + 交互控件白名单）。

### 性能与优化

- 图片方向转换批量导入：去重 O(n²)→O(n)（HashSet）、目录枚举后台化、批量添加一次通知（`BatchObservableCollection`）、进度节流与 Dispatcher 合并、内存峰值 4→3 份位图。
- 托盘打开工具窗口独立弹出（主窗口保持隐藏，owned 窗口跟随隐藏问题规避）。

### 依赖

- 新增 `MetadataExtractor 2.8.1`（EXIF 读取，与斜月图片查看器独立版 MSpirit.Core 同版本）。

## [1.0.1.0] - 2026-08-11

### 修复

- **新库建表 SQL 缺列**：`ProjectDbContext.CreateTablesSql`（新建库路径）与代码实际写入列不同步——`Groups` 缺 `Tag`、`Records` 缺 12 个扩展列、`Attachments` 缺 `CertName`、`PersonnelRecords` 缺 `Cert1-5`。老库走逐列 `ALTER` 补列未暴露，**全新环境首次建库**添加分组/记录/附件即报 `no column named ...`。由单元测试发现并修复，建表 SQL 已补全。
- **人员模块图片查看器设置无效**：照片双击未读取查看器设置、直接调系统查看器；已接入设置分支（内置查看器可翻页/系统默认）。

### 新增

- **单元测试项目** `MSpirit.SafetySys.Tests`（xUnit，89 个用例全绿，挂入解决方案）：覆盖分组树/记录/人员 CRUD、附件复制跨年月路径重建、视图状态、日期解析、转换器；`dotnet test` 一键回归。扩充方向（未覆盖）：`ConfigManager`、`OneClickExporter`/导入解析、`ImageViewerHelper` 几何计算、OCR 录入链路。2026-08-12 新增图片处理 `ImageProcessViewModel.AddDroppedFiles` 拖放添加 6 例（文件过滤/文件夹递归/去重/格式枚举），当前 95 个用例全绿；2026-08-14 新增人员导入问题5修复用例 14 个，当前 109 个用例全绿。
- **更新包流水线**：`Updater.exe` 配套的全量/增量 `.msupd` 打包脚本（`make_full_msupd.py` / `make_incremental_msupd.py`）与双击打包入口（`打包全量更新包.bat` / `打包增量更新包.bat`），版本号自动读取、全程零输入；增量包仅 4 个主程序文件（约 10MB），依赖库变动时用全量包。
- **使用提示窗口**：左侧 7 个选项（隐患/人员/智能导入导出/手机LAN录入/项目管理/图片查看器/创建标准表格），内容经代码核实补全。
- **关于软件窗口**：关于软件/作者信息（邮箱、GitHub、抖音群 + 二维码），版本号动态读取程序集；作者信息支持一键复制与超链接。
- **GitHub 开源线文档**：CHANGELOG / SECURITY / CONTRIBUTING（维护模式）/ FAQ / Issue 模板。
- **docs/开发约定.md** 初版（技术栈、数据层约定、坑清单、发布约定）。

### 其他

- 软件定位放宽为**实体行业**（建筑、制造、生产等），功能场景词保留。
- 备份轮换策略文档纠错（"最多 10 份" → "保留最近 30 天、最多 100 份"，与代码常量一致）。
- 发布文档体系入档 `docs/`；非正式文档移入「临时文档」。

## [1.0.0.0] - 2026-08-11（首个内测版）

### 新增

- **隐患管理**：分组树（年月 → 日期，支持 Tag）、记录卡（自动保存、移动/复制/清空/删除/生成图片）、状态流转（完成/超期/进行/无效）、图片附件（隐患照片 3 + 整改照片 3）。
- **人员管理**：单位 → 部门分组树、人员卡片（身份证/证书照片附件）、三类人员/特种作业标记、**局域网扫码 + PaddleOCR 批量录入**（手机拍照身份证自动识别）。
- **图片查看器**：内置看图器，全屏、缩放、旋转/翻转、缩略图、幻灯片、拖入文件夹。
- **统计分析**：隐患月度统计图表、人员多维统计（学历/证件到期/三类人员/籍贯等）。
- **智能导入导出**：基于 Word/Excel 模板的字段智能匹配导入导出。
- **项目管理**：多项目切换、项目存档导出/导入（`.msarch`，含数据与附件）。
- **自动备份**：启动/退出自动备份到数据根 `ProjectsDate.Backup`（保留最近 30 天、最多 100 份）。
- **其他**：四季主题 + 节气日历、系统托盘常驻、单实例保护、常用隐患类型/常用链接管理、图片处理工具、绿色版启动器 + 更新器。

### 修复

（内测发布前累积修复，摘要）

- 人员模块图片查看器设置无效（未读取设置，直接调系统查看器）。
- 项目存档导入遗留问题（Merge 重写分组归属、Overwrite 按名称匹配、显式列名合并；2026-08-10 修复，经临时 console 工程全链路验证通过）。
- 图片查看器全屏切换闪烁、退出全屏位置偏移、旋转/翻转/平移坐标问题。
- 退出程序时弹出线程异常窗口（`Application.Current` 在 Shutdown 期间置 null，成对代码漏修一侧导致复发）。
- 新库建表与老库迁移路径不一致问题（详见 1.0.0.1 修复项）。

---

> 说明：1.0.0.0 为内测首发版，此前迭代记录未完整保留；后续版本将按本文件格式持续记录。
