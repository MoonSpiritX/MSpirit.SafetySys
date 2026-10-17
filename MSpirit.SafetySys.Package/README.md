# MSpirit.SafetySys.Package

斜月安全管理系统的 **MSIX 打包工程**。

这里只有打包清单与脚本，**没有任何 C# 代码**，也**不在 `MSpirit.SafetySys.slnx` 里**（不参与 `dotnet build` / `dotnet test`）。它的唯一职责是把 `MSpirit.SafetySys` 的发布产物打成可安装、可提交微软商店的 `.msix`。

> ⚠️ **本目录随仓库公开（AGPL-3.0），因此有一条铁律：`打包MSIX.ps1` 的 `param()` 里 `$CertPassword` 的默认值必须永远保持为空。**
>
> 口令只能走三级来源：`-CertPassword` 参数 → 环境变量 `MSIX_CERT_PASSWORD` → `bin\MSIX.Cert\pfx.password`。
> 证书私钥（`.pfx`）与口令文件都落在仓库根的 `bin\` 下，已由 `.gitignore` 三重排除（`bin/`、`*.pfx`、`*.password`），**不会入库**。
> 一旦有人把明文口令写回脚本并提交，git 历史将永久留痕 —— 删掉文件也抹不掉，只能重写历史（若已被克隆则不可挽回）。

---

## 1. 为什么不走 Visual Studio

本工程刻意**不依赖 Visual Studio，也不依赖已安装的 Windows SDK**：`makeappx.exe` / `signtool.exe` 直接取自 NuGet 包 `Microsoft.Windows.SDK.BuildTools`（项目依赖链里本来就有，`dotnet restore` 之后就在缓存里）。

好处是打包只需一个 .NET SDK，任何一台开发机、任何一条 CI 都能跑；代价是要自己写清单与脚本——也就是本目录存在的原因。

## 2. 文件清单

| 文件 | 作用 |
|------|------|
| `AppxManifest.xml` | 包清单。**makeappx 直接消费这个文件名**（VS 打包工程的源文件名是 `Package.appxmanifest`，内容等价、文件名不同） |
| `打包MSIX.ps1` | 打包主脚本：发布 → 组装布局 → `makeappx pack` → `signtool sign` |
| `打包MSIX.bat` | 双击入口，只负责 `-ExecutionPolicy Bypass` 调用上面的 ps1 并保持窗口 |
| `Assets/` | 商店与开始菜单图标：`Square150x150Logo.png` / `Square44x44Logo.png` / `StoreLogo.png`（均由 `MSpirit.SafetySys\Resources\斜月安全管理系统图标256.png` 以 LANCZOS 缩放而来；**2026-10-17 二次重出 —— 新图标取消了四周透明边距，三档均占满画布，视觉上比旧版更大**） |
| `README.md` | 本文件 |

产物与中间文件都落在仓库根的 `bin/` 下，**已被 `.gitignore` 排除**，不会进仓库：

| 路径 | 内容 |
|------|------|
| `bin\Release.MSIX\<包名>_<版本>_<架构>.msix` | 最终包（如 `Spirit.31431668B8E26F_1.2.3.0_x64.msix`） |
| `bin\MSIX.Layout\` | 打包前的布局目录（`-SkipPublish` 复用的就是它） |
| `bin\MSIX.Cert\MSpiritTest.pfx` / `.cer` | 本地测试签名证书与公钥 |
| `bin\MSIX.Cert\pfx.password` | 证书口令（本地文件，与 `.pfx` 同目录） |

> `.pfx` 里含私钥，但它**不进 git**（`bin/` 被忽略）。口令同理，脚本按三级来源解析（优先级从高到低）：
>
> 1. 命令行参数 `-CertPassword '<口令>'`
> 2. 环境变量 `$env:MSIX_CERT_PASSWORD`
> 3. 本地口令文件 `bin\MSIX.Cert\pfx.password`（内容为纯口令一行）
>
> 三级都没有时：**没有现成证书 → 自动生成随机口令并写入该文件**（首次打包免手工配置）；**已有证书 → 直接报错并提示上面三种来源**（不会静默用错口令）。
>
> ⚠️ **当前 `param()` 里仍留着一个旧默认值，会短路以上三级** → 需按第 8 节「已知坑」第 10 条清空后才真正生效。

## 3. 前置条件

- **.NET SDK 10**（`dotnet publish` 与 NuGet 取 BuildTools 都靠它）
- 打包机任意 Windows 版本即可；**安装测试**需要 Windows 10 1809+ / Windows 11（RS5 起旁加载默认放行）
- **打包与签名不需要管理员权限**——只有「导入测试证书」和「安装包」需要

## 4. 打包

```powershell
# 方式 1：双击
打包MSIX.bat

# 方式 2：命令行（绝对路径，避免目录不对）
powershell -NoProfile -ExecutionPolicy Bypass -File "<仓库根>\MSpirit.SafetySys.Package\打包MSIX.ps1"

# 只改了清单/脚本时复用上次布局目录，跳过 dotnet publish（快很多）
powershell -NoProfile -ExecutionPolicy Bypass -File "<仓库根>\MSpirit.SafetySys.Package\打包MSIX.ps1" -SkipPublish

# 离线环境（TSA 不可达时）：跳过时间戳出包（不推荐，仅应急）
powershell -NoProfile -ExecutionPolicy Bypass -File "<仓库根>\MSpirit.SafetySys.Package\打包MSIX.ps1" -NoTimestamp

# 换时间戳服务（默认 http://timestamp.digicert.com）
powershell -NoProfile -ExecutionPolicy Bypass -File "<仓库根>\MSpirit.SafetySys.Package\打包MSIX.ps1" -TimestampServer "http://timestamp.digicert.com.cn"
```

脚本七步：

| 步骤 | 内容 | 失败时说明 |
|------|------|-----------|
| 0/7 | 定位 `makeappx` / `signtool` | 找不到 → 先 `dotnet restore` |
| 1/7 | 前置检查：主工程 / 清单 / 图标齐备；**版本第四段必须为 0** | 商店硬性要求 |
| 2/7 | `dotnet publish` 到 `bin\MSIX.Layout`（自包含 win-x64） | `-SkipPublish` 时跳过 |
| 3/7 | 清理 pdb、放入清单与图标 | 图标用「建目录 + 拷 `*.png`」，避免 `Assets\Assets\` 双层 |
| 4/7 | `makeappx pack` | 正常时静默，失败才打印完整输出 |
| 5/7 | 准备签名证书：**核对已有证书 Subject 是否等于 `$CertSubject`**，不一致则重新生成；再守卫**清单 `Identity/@Publisher` 与证书 Subject 逐字符一致** | 这一步是防「改了签名者却沿用旧 .pfx」的坑 |
| 6/7 | `signtool sign`（**带 RFC3161 时间戳**）+ `verify`；连不上 TSA 自动回退为不带时间戳重签并在末尾告警 | verify 失败通常只是证书还没导入受信任存储 |
| 7/7 | 打印包标识、签名者指纹、**时间戳状态**与后续导入/卸载/安装命令 | —— |

### 关于时间戳（2026-10-15 补上）

签名时加 `/td SHA256 /tr <TSA地址>`，向 RFC3161 时间戳服务换取一份"签名发生在何时"的证明并封进签名。实测走 `http://timestamp.digicert.com` 成功，耗时约 20 秒，拿到的时间戳证书有效期到 **2037 年**。

**没有时间戳会怎样**（微软官方文档《Sign an app package》原文措辞）：

| 场景 | 无时间戳 | 有时间戳 |
|------|----------|----------|
| 证书有效期内 | 可安装 | 可安装 |
| 证书过期后 | **安装失败** | 仍可安装（按签名时刻判定证书有效性） |
| 装完之后的运行 | 不受影响 | 不受影响 |

也就是说：**已经装上的包，证书过期也不会失效；受影响的是"证书过期后再去装这个包"。** 本测试证书有效期 1 年（到 2027-10-15），无时间戳的话这包明年就装不上了。上架微软商店不受影响（商店用自己的证书重新签名），受影响的是自签分发 / 以后可能走国内平台的分发这条线。

### 桌面快捷方式（desktop7，2026-10-16 新增）

装包时由系统创建桌面快捷方式，声明在 **`<Application>` 级**（与 firewallRules 相反）：

```xml
<desktop7:Extension Category="windows.shortcut">
  <desktop7:Shortcut
    File="$(Desktop)\斜月安全管理系统.lnk"
    Icon="MSpirit.SafetySys.exe"
    Description="面向实体行业企业安全管理员的本地桌面软件" />
</desktop7:Extension>
```

三个约束：

| 约束 | 说明 |
|------|------|
| `File` 只能落三个常量目录 | `$(Desktop)` / `$(Programs)` / `$(Common Programs)`。用 `$(Desktop)` = **当前用户桌面**（MSIX 按用户安装，各人装各人有一份）；**不能**写到 `C:\Users\Public\Desktop` |
| `Icon` **必填** | 指向包内文件。这里用主程序 exe 取内嵌图标（`.lnk` 的图标位不支持直接引用 png，故不能用 `Assets\*.png`） |
| 显示名 | 不单独声明（不引 `desktop10:DisplayName`，少一个命名空间），直接用文件名「斜月安全管理系统」 |

**Win10 兼容取舍（重要）**：官方 schema 标注本元素**最低 OS Build 19645**，而 Windows 10 最高只到 19045 —— 若照此把 `TargetDeviceFamily/@MinVersion` 提到 19645，等于**把所有 Win10 用户排除在外**。本清单刻意**不动 MinVersion（仍是 `10.0.17763.0`）**，而是把 `desktop7` 写进 `IgnorableNamespaces`：Win10 认不得该命名空间时会**忽略**它，结果是「**装得上、但没有桌面快捷方式**」（用户 2026-10-16 明确接受这个取舍）。Win11 正常创建。

> 备选路（本次评估后未采用）：① 让用户从开始菜单「**所有应用**」把图标拖到桌面 —— Win11 实测**拖不动、不生成快捷方式**；② 程序运行时自己写 `.lnk` 到桌面 —— 不受 OS 版本限制，但**卸载时不会被清理**，会留一个失效图标。

### 包体积与语言资源裁剪（2026-10-16）

`MSpirit.SafetySys.csproj` 里设了 `<SatelliteResourceLanguages>zh-Hans;zh-Hant;en</SatelliteResourceLanguages>`，把 .NET 运行时与各依赖库的**卫星程序集**裁到只剩中文（简/繁）与英文；`cs` / `de` / `es` / `fr` / `it` / `ja` / `ko` / `pl` / `pt-BR` / `ru` / `tr` 共 11 个语言目录不再生成。实测：

| 项 | 改前 | 改后 |
|----|------|------|
| msix 体积 | 261.5 MB | **256.4 MB**（−5.1 MB） |
| 布局目录 | —— | 664.1 MB |
| 语言目录 | 14 个 | **2 个**（`zh-Hans` / `zh-Hant`） |

英文不产生独立目录（它是中性资源，直接编进主程序集），所以「只剩中文与英文」在磁盘上表现为这两个目录而不是三个。

⚠️ **降幅只有 5 MB 量级，别期待"省几十 MB"** —— 卫星程序集本身都很小。包体积的大头是 .NET 运行时、`paddle_inference_c.dll` / `mkldnn.dll` / `mklml.dll`（PaddleOCR 推理引擎）、`onnxruntime.dll`、`libSkiaSharp.dll`、`opencv_videoio_ffmpeg4110_64.dll` 等原生库；要再瘦身得从"砍功能"入手，不是裁语言能解决的。

作用域是**所有**发布形态（MSIX 包与绿色版一起受益）。若将来某个依赖库只提供非中性资源、出现 `MissingManifestResourceException`，把对应语言加回列表即可。

## 5. 安装 / 卸载 / 更新

全部在**管理员 PowerShell** 里执行。

```powershell
# ① 导入测试证书到「本地计算机 → 受信任人员」（仅首次、或换了签名者之后）
Import-Certificate -FilePath "<仓库根>\bin\MSIX.Cert\MSpiritTest.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# ② 安装（包名 = Partner Center 预留名生成的标识，见第 7 节「当前标识」）
Add-AppxPackage -Path "<仓库根>\bin\Release.MSIX\Spirit.31431668B8E26F_1.2.3.0_x64.msix"

# ③ 启动（AUMID = <包族名>!App；包族名 = 包名 + 发布者哈希，改名或换发布者后哈希会变）
#   推荐从开始菜单反查，省得记哈希：
$aumid = (Get-StartApps | Where-Object { $_.Name -eq '斜月安全管理系统' }).AppID
Start-Process "shell:AppsFolder\$aumid"

# ④ 卸载（当前标识；旧测试标识 MSpiritSafetySys 若还在，也顺手卸掉）
Get-AppxPackage Spirit.31431668B8E26F | Remove-AppxPackage
Get-AppxPackage MSpiritSafetySys | Remove-AppxPackage

# ⑤ 装完核对桌面快捷方式（Win11 应存在；Win10 按设计没有，见第 4 节）
Test-Path "$env:USERPROFILE\Desktop\斜月安全管理系统.lnk"
```

**装完应当看到**：开始菜单出现「斜月安全管理系统」，且 **Win11 的桌面出现「斜月安全管理系统」快捷方式**。

> 包族名的哈希段（如 `_n4fgvp176crcr`）由 `Identity/@Name` + `Publisher` 派生。**改了这两项中任意一项，旧的那条 `shell:AppsFolder\...` 就会报「系统找不到指定的文件」**——这不是打包出错，是拿旧钥匙开新锁。用上面的反查写法或去开始菜单点，都能正常启动。

**⚠️ 前置：先关掉绿色版。** `SingleInstanceGuard` 的互斥体名是固定串 `MSpirit.SafetySys_Unique`（**不带包标识**）→ 绿色版与商店版**互相排斥**，绿色版开着时商店版会静默退出（表现为"点了没反应"）。两版共用同一数据根与端口 8000。

**注意：改了包名或发布者就不是"升级"了。** 包族名（PFN）= `Identity/@Name` + 发布者哈希，任一变化都会生成一个**全新的应用标识**，系统会当成另一个应用：旧版必须手动卸载，用户数据虽共享（数据根在 `%USERPROFILE%\MSpirit.SafetySys.Date`，不在包内）但设置与开始菜单项会各留一份。

## 6. 证书与信任（最容易踩的一节）

自签名包的信任判定只认**本地计算机**级存储：

| 存储区 | 是否有效 |
|--------|----------|
| `LocalMachine\TrustedPeople`（本地计算机 → 受信任人员） | ✅ **推荐**，只影响"包/应用签名"信任，影响面最小 |
| `LocalMachine\Root`（本地计算机 → 受信任的根） | ✅ 可用（本证书自签名 = 自身链的根，语义合法） |
| `CurrentUser\TrustedPeople` / `CurrentUser\Root` | ❌ **无效** |

关键是最后一行：微软官方文档原文是「**不要将包签名证书导入到用户证书存储中。验证包标识时，应用安装程序不会搜索用户证书。**」

由此有一个**极具迷惑性的陷阱**：把证书装到 `CurrentUser\Root` 后，`Get-AuthenticodeSignature <包>` 会返回 `Valid` /「已验证签名」、`SignerCertificate.Verify()` 也返回 `True`——**看起来一切正常，但安装照样失败**（`0x800B0109`）。这两个 API 只跑在当前用户上下文里，只用用户存储区。

**所以判断"这个包能不能装"，不要看 `Get-AuthenticodeSignature`，要看 `LocalMachine\TrustedPeople` / `LocalMachine\Root` 里有没有那张证书。**

## 7. 上架微软商店：包标识与证书（2026-10-16 已切换为正式值）

### 当前标识（已 = Partner Center 分配值，不再是测试占位符）

| 项 | 当前值 | 说明 |
|----|--------|------|
| `Identity/@Name` | **`Spirit.31431668B8E26F`** | PC「产品标识」页 `Package/Identity/Name` 的**原样值**（= 你保留的应用名 + PC 自动拼的发布者前缀）。官方要求与预留名**逐字符一致**；**发布后永久固定**——再改等于换一个新应用、新 listing，老用户没有升级路径 |
| `Identity/@Publisher` | **`CN=6603A24B-BA4D-43EE-B004-2337E6B44574`** | PC `Package/Identity/Publisher`。**必须与签名证书 Subject 逐字符一致** ⇒ `打包MSIX.ps1` 的 `$CertSubject` 已同步；脚本检测到已有 `.pfx` 的 Subject 不一致时会**自动重新生成证书**，无需手工删除 |
| `Properties/PublisherDisplayName` | **`斜月 Spirit`** | 须与 PC `Package/Properties/PublisherDisplayName` 一致 |
| `Properties/DisplayName`、`VisualElements/@DisplayName` | `斜月安全管理系统` | **用户可见名**，中文，保持不动（`Identity/@Name` 只允许 ASCII，中文放不进那里） |
| `Identity/@Version` | `1.2.3.0` | 第四段按商店硬性要求必须为 0 |

**包族名（PFN）随之变更**：`MSpiritSafetySys_4df3rbx12d1x0` → **`Spirit.31431668B8E26F_n4fgvp176crcr`**（AUMID = `Spirit.31431668B8E26F_n4fgvp176crcr!App`）。⇒ 与旧测试包**不是升级关系**：**旧包必须先卸载**、**新证书必须重新导入** `LocalMachine\TrustedPeople`，然后才能装新包。

### 上架时仍需注意

| 项 | 说明 |
|----|------|
| 签名证书 | 本地仍是**自签名**（商店不接受自签）——但**提交后由微软用自己的证书重新签名**，所以**不需要购买代码签名证书**，本地自签只为旁加载验证 |
| 隐私政策 / 支持 URL | 在 PC 提交表单的「属性」页填写（`docs/PRIVACY_POLICY.md` 已就绪且公网可达） |
| 受限功能 `runFullTrust` | 在「提交选项」页填理由（草稿见 `临时文档/微软商店上架数据清单.md`） |

> `Identity/@Publisher` 是**逐字符**校验的（含 `CN=` 前缀、大小写、空格），也正因如此：改 Publisher 必须**同时**改 `$CertSubject` 并让脚本重新生成证书——脚本第 5 步已内置这个自愈，不用手工删 `.pfx`。

> 关于包名：2026-10-15 曾定「不提前改成两段式占位名」（真实预留名只能从 PC 复制，自拟名同样要在提交前替换掉，提前改只白付一轮卸载重装成本）。该判断在 2026-10-16 得到印证——用户 10 月初注册时**已保留名称**，直接复制正式值即完成切换，中间那一轮占位改名完全没有必要。

## 8. 已知坑（都实测撞过）

1. **两个扩展的声明层级是相反的，别放反**：`desktop2:FirewallRules` 必须在 **`<Package>` 级**（放进 `<Application>` 会报 `80080204: The "windows.firewallRules" extension should be declared under <Package> extensions.`）；`desktop7:Shortcut` 则必须在 **`<Application>` 级**（它描述的是"哪个应用"的快捷方式）。
2. **`Copy-Item <源目录> <目标目录>` 在目标已存在时会把源目录塞进去** → 包里出现 `Assets\Assets\*.png` 双层。脚本改为「建目录 + 拷 `*.png`」。
3. **受管/沙盒环境可能接管 `Remove-Item`**（安全删除策略，直接拒绝并抛错）→ 脚本内的删除一律改用 .NET API（`File.Delete` / `Directory.Delete`）。
4. **编码规矩：`.ps1` 要 UTF-8 **with BOM**，`.bat` 要 **GBK(936) + CRLF** —— 两者相反，别搞混**。
   - **`.ps1`**：Windows PowerShell 5.1 靠 BOM 判断编码，UTF-8 无 BOM 时中文按 ANSI 读成乱码。改动后用 .NET API 重写补回 BOM（`WriteAllText(..., UTF8Encoding($true))`）。
   - **`.bat`**：`cmd.exe` **始终按 ANSI 代码页（简中 = 936/GBK）** 解析批处理，写 `chcp 65001` 也救不了已进入缓冲的内容，反而会让 PowerShell 的中文输出花屏。
   - **2026-10-17 实测故障**：原 `打包MSIX.bat` 是 **UTF-8 无 BOM + LF 行尾**，双击后报
     `'琛岋級' 不是内部或外部命令` / `'…銆?powershell' 不是内部或外部命令`。
     根因：**全角句号「。」的 UTF-8 尾字节 `0x82` 与行尾 `0x0A` 被 GBK 拼成一个双字节字符，换行被吃掉** → 注释行与下一行 `powershell` 合并成一条命令执行。
     实证（同内容两版对比）：A＝UTF-8+LF → 报错且下一行命令被整条吞掉；B＝GBK+CRLF → 全部正常。
     修法见当前文件：GBK + CRLF、去掉 `chcp 65001`、注释改纯 ASCII（**唯一中文字面量是 ps1 文件名**，GBK 下可正确表示）。故障版备份在同目录 `打包MSIX.bat.bak-utf8-20261017`，确认无误后可删。
   - 顺带：`.bat` 里注释用 `rem` 而非 `::`，避免在 `if`/`for` 块内出现解析歧义。
5. **签名者改名必须重生成证书**，不能沿用旧 `.pfx`——脚本第 5 步已内置核对，不一致会自动重生成并提示清理旧证书。
6. **包名/发布者变更 ⇒ 包族名变更 ⇒ 必须卸载重装**（不是升级）。
7. **防火墙规则里记的可能是另一个盘符**：当系统盘不是唯一安装位置时（「新应用安装位置」设到了别的盘），Windows 把包实体放在 `D:\WindowsApps\<包全名>\`，只在 `C:\Program Files\WindowsApps\<包全名>` 留一个**目录联接**。此时系统按清单自建的防火墙规则里 `Program` 记的是 `D:` 路径，而进程自报的是 `C:` 路径。程序侧的程序级规则检测曾因此**假阴性**（包内每次点「启动服务」都弹手动指引窗），已由 `LanFirewallService.ProgramPathEquals` 的第 ③ 级判定修掉。详见 `临时文档/R3-LAN防火墙MSIX适配方案.md` §11.9。
8. **别漏掉时间戳**：`signtool sign` 不带 `/tr` 时签名里就没有时间信息，属性页会显示「时间戳：**不可用**」。后果只有一个但很硬——**证书一过期，这个包就再也装不上了**（已装的可继续运行）。2026-10-15 之前的首个包就是这个状态，现已由脚本默认加上（TSA 默认 DigiCert，可用 `-NoTimestamp` 显式关掉、`-TimestampServer` 换源）。
9. **`Get-AuthenticodeSignature` 说"Valid"不等于能装**：它只查当前用户存储区。信任判定必须看 `LocalMachine\TrustedPeople` / `LocalMachine\Root`，详见第 6 节。
10. **打包脚本口令已外置（2026-10-17 收口，此项已不再是待办）**：脚本按「`-CertPassword` 参数 > `$env:MSIX_CERT_PASSWORD` > `bin\MSIX.Cert\pfx.password`」三级解析，`param()` 里原先的**明文默认值已清空为 `''`**，三级解析现已真正生效。口令现存放于 `bin\MSIX.Cert\pfx.password`（一行纯口令、不带换行；`bin/` 与 `*.password` 双重忽略，不进 git）。
    - ⚠️ **清空默认值的前提是口令文件先就位**。若「已有 `.pfx` 但三级来源全空」，脚本会**直接报错并提示三种来源**——这是刻意设计：不会静默用错口令，也不会悄悄重建证书（重建会换掉证书指纹，等同于换签名者）。
    - 新机器首次打包时该文件不存在也无所谓：**没有现成证书 → 脚本自动生成随机口令并落盘**，新的 `.pfx` 就用这个口令创建。
11. **桌面快捷方式图标带一把黄色小锁 —— 是"非系统包卷"的官方加密，不是打包缺陷**（2026-10-16 逐环实测定位）：
    - 现象：`desktop7:Shortcut` 生效、桌面出现快捷方式，但图标右上角压一把金色挂锁；**开始菜单里的图标正常**。
    - 根因链：本机默认应用安装卷 = `D:\WindowsApps`（`Get-AppxDefaultVolume`，`IsSystemVolume=False`）→ 微软文档《Package storage volumes》明确「**Windows 会加密非系统包卷上的包文件**」（防止把存储移到别的电脑绕过 MSIX 完整性保护）→ 于是包内 `MSpirit.SafetySys.exe` 带 `FILE_ATTRIBUTE_ENCRYPTED`（`cipher /c` 会显示该目录"**将加密新加到此目录的文件**"）→ Shell 渲染这个 exe 的图标时把"已加密"挂锁**合成进图标位图本身**（用 `SHGetFileInfo` 把图标抽出来即见挂锁，同一套方法抽绿色版 lnk 则干净）→ 桌面快捷方式显示的是 exe 图标 ⇒ 带锁；开始菜单用的是清单里的 Logo 资源 ⇒ 干净。
    - **已排除**（别再往这些方向查）：NTFS 权限/所有者/组（与无锁快捷方式 SDDL 逐字符相同，且另外 8 个所有者同为 `BUILTIN\Administrators` 的快捷方式都无锁）、已注册的图标叠加处理器（`SHGetFileInfo` 返回的叠加索引与其它快捷方式一致）、桌面上 `desktop.ini` 的 `[LocalizedFileNames]` 本地化映射（无害，见第 12 条）。
    - 一次确认：`Get-AppxDefaultVolume`（是否非系统卷）+ `cipher /c "D:\WindowsApps\<包全名>\MSpirit.SafetySys.exe"`（是否 E）。
    - 影响：**纯外观，功能零影响；商店上架不受影响**（其他用户默认装在系统卷 `C:\Program Files\WindowsApps`，不会出现）。交叉验证：同卷另外 8 个包（OpenAI.Codex / Steam++ 加速器 / LegionZone / WinAppRuntime 等）的 exe 同样带 `E` ⇒ 与我们的打包无关。
    - 想要消除：把「新的应用将保存到」改回 C:（设置 → 系统 → 存储 → 高级存储设置 → 新内容的保存位置）→ 卸载 → 重装；或试 `Move-AppxPackage -Package <包全名> -Volume <系统卷对象>`（是否支持视包类型而定，失败无害）。
12. **MSIX 部署会改写用户的「桌面\desktop.ini」**：装包时新增一行 `[LocalizedFileNames]` / `斜月安全管理系统.lnk=@,0`（Windows 的"本地化显示名"机制）。**无害**，但属包对外部文件的写入；卸载后该行会残留（不影响系统）。

## 9. 常见错误码

| 错误码 | 含义与处置 |
|--------|-----------|
| `0x80070005` `E_ACCESSDENIED` | 权限不足 → **用管理员 PowerShell** 重跑。本机实测：**双击 .msix（App Installer）走的就是非提权这条路，同样失败**（日志 `465/403 无法获取暂存会话`）；把包拷到 `Downloads` 再非提权装也失败 ⇒ 与包文件位置/ACL 无关 |
| `0x80073CFB` | **同标识 + 同版本 + 内容不同** → 必被拒（日志会写"请提高版本号，或删除系统上每个用户的旧程序包"）。因清单 `Version` 第 4 段必须为 0，**覆盖安装只能提第 3 段**（如 `1.2.1.0 → 1.2.3.0`）；不提版本就只能先卸载再装 |
| `0x800B0109` | 根证书不受信任 → 证书装到了用户级，改导 `LocalMachine\TrustedPeople` |
| `0x8BAD0042` `CertNotTrusted` | 同上，设备不信任签名证书 |
| `0x8007000B` | 清单 `Publisher` 与证书 Subject 不一致（含大小写/空格） |
| `80080204` | 清单扩展放错层级，见「已知坑」第 1 条 |
| `0x8007000D` | 包损坏、清单格式错误，或 Publisher 与证书不匹配 |

## 10. 相关文档

- `临时文档/R3-LAN防火墙MSIX适配方案.md` —— 防火墙在 MSIX 下的形态分流（R3）、打包落地记录（§11）、安装与实测记录（§11.9）
- `临时文档/MSIX打包与上架验证清单.md` —— 上架前的整体验证清单与风险表
- `docs/版本号管理方案.md` —— 版本号规则（清单版本与 csproj / CHANGELOG / AboutWindow 三处同步）
- `docs/LAN服务技术文档.md` —— 防火墙与设备在线判定的实现说明
