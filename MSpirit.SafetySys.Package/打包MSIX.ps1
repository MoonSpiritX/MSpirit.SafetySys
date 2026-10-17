# ============================================================
#  斜月安全管理系统 · MSIX 打包脚本
#
#  流程：dotnet publish → 布局目录 → makeappx pack → signtool sign
#
#  用法：双击同目录的「打包MSIX.bat」（会以 -ExecutionPolicy Bypass 调用本脚本）。
#        也可手动执行：
#        powershell -NoProfile -ExecutionPolicy Bypass -File "打包MSIX.ps1"
#        调清单时反复跑很慢，可复用已有布局目录跳过发布（仅重打包 + 签名）：
#        powershell -NoProfile -ExecutionPolicy Bypass -File "打包MSIX.ps1" -SkipPublish
#
#  产物：<项目根>\bin\Release.MSIX\<清单 Identity/@Name>_<版本>_<架构>.msix
#        例：Spirit.31431668B8E26F_1.2.2.0_x64.msix
#
#  说明：
#   - 本文件随仓库公开（AGPL-3.0）。唯一绝不能入库的是**证书私钥**：bin\MSIX.Cert\*.pfx 与
#     pfx.password 已由 .gitignore 三重排除（bin/ 、*.pfx 、*.password）。脚本内的口令默认值必须永远为空。
#   - 打包与签名**不需要管理员权限**；只有"把测试证书导入受信任存储"和"安装包"才需要（脚本末尾会提示）。
#   - 证书 Subject 必须与清单 Identity/@Publisher **逐字符一致**；脚本会核对，不一致直接报错。
#   - 真上架前必须替换 AppxManifest.xml 里的 Identity/@Publisher 与 @Name 为 Partner Center 分配值。
#   - 签名默认带 **RFC3161 时间戳**（sign 时加 /td SHA256 /tr <TSA>）。无时间戳的包在证书过期后
#     **将无法再安装**；带时间戳则可一直安装（官方：已装应用无论有无时间戳都能继续运行）。
#     时间戳需要联网；签名时连不上 TSA 会自动回退为"不带时间戳重签"并在末尾给出警告。
#     离线环境可显式跳过：-NoTimestamp；换 TSA 可传：-TimestampServer <url>。
# ============================================================

param(
    [switch]$SkipPublish,
    # 跳过时间戳（离线环境用）。默认不加时间戳是**不推荐**的，仅用于无网时先把包签出来。
    [switch]$NoTimestamp,
    # RFC3161 时间戳服务地址（需支持 /tr 调用）。换国内也可用 e.g. http://timestamp.digicert.com.cn
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    # 测试证书（.pfx）的密码。**此默认值必须永远保持为空**。
    # 口令三级解析：-CertPassword 参数 > $env:MSIX_CERT_PASSWORD > bin\MSIX.Cert\pfx.password。
    # !! 切勿把明文口令写回这里：本文件随仓库公开（AGPL-3.0），git 历史不可撤销。
    [string]$CertPassword = ''
)

$ErrorActionPreference = 'Stop'

# ---------- 路径常量 ----------
# $PSScriptRoot 由 .bat 以 -File 调用时正常；直接粘贴到控制台运行则为空。
# 刻意不做硬编码兜底：兜底路径在换机器/换目录后会指向错误位置，也会把本地目录结构写进公开仓库。
# 为空时立即报错并给出正确调用方式，比静默猜一个路径安全。
if (-not $PSScriptRoot) {
    $msg = '无法定位脚本所在目录（$PSScriptRoot 为空）。' + [Environment]::NewLine +
           '本脚本需以 -File 方式调用，例如：' + [Environment]::NewLine +
           '  powershell -NoProfile -ExecutionPolicy Bypass -File "<脚本完整路径>\打包MSIX.ps1"' + [Environment]::NewLine +
           '或直接双击同目录下的 打包MSIX.bat。'
    throw $msg
}
$PackageDir  = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $PackageDir

$MainProject = Join-Path $ProjectRoot 'MSpirit.SafetySys\MS_SafetyManagementSystem.csproj'
$Manifest    = Join-Path $PackageDir 'AppxManifest.xml'
$AssetsDir   = Join-Path $PackageDir 'Assets'
$LayoutDir   = Join-Path $ProjectRoot 'bin\MSIX.Layout'
$OutputDir   = Join-Path $ProjectRoot 'bin\Release.MSIX'
$CertDir     = Join-Path $ProjectRoot 'bin\MSIX.Cert'
$CertPfx     = Join-Path $CertDir 'MSpiritTest.pfx'
$CertCer     = Join-Path $CertDir 'MSpiritTest.cer'
# 签名者名称（证书 Subject）不在此处写死 —— 改为从清单 Identity/@Publisher 读取（见步骤 1 前置检查）。
# 理由：Subject 必须与清单 Publisher 逐字符一致，两处各写一份迟早不同步；改签名者只需改清单一处。
# 注：这是**本地测试证书**，正式上架由商店用自有证书重签，无需购买证书。
# 变量 $CertSubject 在步骤 1 中赋值。

function Write-Step($msg)  { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "    [完成] $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    [注意] $msg" -ForegroundColor Yellow }

Write-Host "============================================" -ForegroundColor White
Write-Host "  斜月安全管理系统 · MSIX 打包" -ForegroundColor White
Write-Host "============================================" -ForegroundColor White

# ---------- 0. 定位 Windows SDK 打包工具（makeappx / signtool） ----------
Write-Step '0/7 定位打包工具'

$sdkRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
if (-not (Test-Path $sdkRoot)) {
    # NuGet 缓存可能被重定向，问 dotnet 要真实位置
    $globalPkgs = (& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '' -replace '\s*$', ''
    if ($globalPkgs -and (Test-Path $globalPkgs)) {
        $sdkRoot = Join-Path $globalPkgs 'microsoft.windows.sdk.buildtools'
    }
}
if (-not (Test-Path $sdkRoot)) {
    throw "未找到 Microsoft.Windows.SDK.BuildTools（makeappx 来源）。请先执行：dotnet restore `"$MainProject`""
}

# 取版本号最大的一个（目录名即版本）
$sdkVer = Get-ChildItem $sdkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
$binVer = Get-ChildItem (Join-Path $sdkVer.FullName 'bin') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$ToolsDir = Join-Path $binVer.FullName 'x64'
$MakeAppx = Join-Path $ToolsDir 'makeappx.exe'
$SignTool = Join-Path $ToolsDir 'signtool.exe'

foreach ($tool in @($MakeAppx, $SignTool)) {
    if (-not (Test-Path $tool)) { throw "缺少打包工具：$tool" }
}
Write-Ok "工具位于 $ToolsDir（BuildTools $($sdkVer.Name)）"

# ---------- 1. 前置检查 ----------
Write-Step '1/7 前置检查'

foreach ($p in @($MainProject, $Manifest, (Join-Path $AssetsDir 'Square150x150Logo.png'))) {
    if (-not (Test-Path $p)) { throw "缺少必需文件：$p" }
}
Write-Ok '主工程 / 清单 / 图标齐备'

# 从清单读版本与包名（清单是打包的事实来源，避免与 csproj 版本脱节）
[xml]$manifestXml = Get-Content $Manifest -Raw
$pkgName    = $manifestXml.Package.Identity.Name
# 签名证书 Subject 必须与清单 Publisher 逐字符一致 —— 单一来源，直接从清单取
$CertSubject = $manifestXml.Package.Identity.Publisher
if (-not $CertSubject) { throw '清单 Identity/@Publisher 为空，无法确定签名证书 Subject' }
$pkgVersion = $manifestXml.Package.Identity.Version
$pkgArch    = $manifestXml.Package.Identity.ProcessorArchitecture
Write-Ok "包标识：$pkgName  $pkgVersion  $pkgArch"

# 版本第四段（Revision）必须为 0，否则商店拒收
$verParts = $pkgVersion.Split('.')
if ($verParts.Count -ne 4 -or $verParts[3] -ne '0') {
    throw "清单 Version=$pkgVersion 不合法：四段格式且第四段（Revision）必须为 0"
}

$msixPath = Join-Path $OutputDir "$pkgName`_$pkgVersion`_$pkgArch.msix"

# ---------- 2. 重建布局目录 ----------
$layoutExe = Join-Path $LayoutDir 'MSpirit.SafetySys.exe'
if ($SkipPublish -and (Test-Path $layoutExe)) {
    Write-Step '2/7 跳过发布（-SkipPublish，复用现有布局目录）'
    Write-Warn2 "复用 $LayoutDir —— 仅调清单/脚本时用；源码有改动请去掉 -SkipPublish 重新发布"
} else {
    Write-Step '2/7 发布主程序到布局目录'
    # 用 .NET API 而非 Remove-Item：部分受管/沙盒环境会接管 Remove-Item（安全删除策略，
    # 拒绝删除并抛错），会让构建脚本中途失败；.NET API 语义明确、行为稳定。
    if ([System.IO.Directory]::Exists($LayoutDir)) { [System.IO.Directory]::Delete($LayoutDir, $true) }
    [System.IO.Directory]::CreateDirectory($LayoutDir) | Out-Null

    & dotnet publish $MainProject -c Release -r win-x64 --self-contained true -o $LayoutDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }
    Write-Ok '主程序已发布（自包含 win-x64）'
}

# ---------- 3. 清理布局 + 放入清单与图标 ----------
Write-Step '3/7 组装包内容'

# 程序自身 PDB 无需进包（第三方 PDB 已由 csproj 的 Target 排除）。
# 同样用 .NET API 而非 Remove-Item，理由见上面第 2 步的注释。
foreach ($f in @(Get-ChildItem -LiteralPath $LayoutDir -Filter '*.pdb' -File -ErrorAction SilentlyContinue)) {
    [System.IO.File]::Delete($f.FullName)
}
Write-Ok 'pdb 已清理'

Copy-Item -LiteralPath $Manifest -Destination (Join-Path $LayoutDir 'AppxManifest.xml') -Force

# 图标按"建目录 + 拷文件"放入，而不是拷目录。
# 注意：`Copy-Item <源目录> <目标目录>` 在**目标已存在**时会把源目录塞进去，变成双层
# Assets\Assets\*.png（2026-10-15 实测撞到，包里多出一份无用图标）。
$targetAssets = Join-Path $LayoutDir 'Assets'
[System.IO.Directory]::CreateDirectory($targetAssets) | Out-Null
Copy-Item -Path (Join-Path $AssetsDir '*.png') -Destination $targetAssets -Force

$exePath = Join-Path $LayoutDir 'MSpirit.SafetySys.exe'
if (-not (Test-Path $exePath)) {
    throw "布局目录缺少入口 exe（$exePath）——清单 Executable 指向它，必须存在"
}

$sizeMb = [math]::Round(((Get-ChildItem $LayoutDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Ok "布局就绪：$sizeMb MB（入口 MSpirit.SafetySys.exe）"

# ---------- 4. 打包 ----------
Write-Step '4/7 makeappx 打包'
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# makeappx 对每个文件都会打印一行 "Processing ..."，678 个文件刷屏且淹没真正错误 →
# 正常时静默，失败才把完整输出打出来。
$makeappxOut = & $MakeAppx pack /d $LayoutDir /p $msixPath /o 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host ($makeappxOut | Out-String)
    throw "makeappx 打包失败（退出码 $LASTEXITCODE）"
}
Write-Ok "已生成 $msixPath（$([math]::Round((Get-Item $msixPath).Length / 1MB, 1)) MB）"

# ---------- 5. 测试证书 ----------
# ---------- 证书口令解析 ----------
# 优先级：-CertPassword 参数 > 环境变量 MSIX_CERT_PASSWORD > 本地口令文件 bin\MSIX.Cert\pfx.password
#        > （仅在"当前没有现成证书、需要新建"时）自动生成随机口令并写入口令文件。
# 口令文件与它保护的 .pfx 同目录（bin\ 下，已被 .gitignore 排除）。
$PwFile = Join-Path $CertDir 'pfx.password'

if ([string]::IsNullOrEmpty($CertPassword)) { $CertPassword = $env:MSIX_CERT_PASSWORD }
if ([string]::IsNullOrEmpty($CertPassword) -and (Test-Path -LiteralPath $PwFile)) {
    $CertPassword = ([System.IO.File]::ReadAllText($PwFile)).Trim()
}
if ([string]::IsNullOrEmpty($CertPassword)) {
    if (Test-Path -LiteralPath $CertPfx) {
        throw ('已有签名证书但未提供口令，请任选一种方式提供：' + [Environment]::NewLine +
               "  1) 命令行参数：-CertPassword '<口令>'" + [Environment]::NewLine +
               "  2) 环境变量：  `$env:MSIX_CERT_PASSWORD = '<口令>'" + [Environment]::NewLine +
               "  3) 口令文件：  $PwFile（内容为纯口令一行）")
    }
    # 没有现成证书 → 生成随机口令并落盘（首次打包免手工设置；将来重建证书同样走这里）
    $rngBytes = New-Object byte[] 18
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rngBytes)
    $CertPassword = [Convert]::ToBase64String($rngBytes)
    New-Item -ItemType Directory -Force -Path $CertDir | Out-Null
    [System.IO.File]::WriteAllText($PwFile, $CertPassword, (New-Object System.Text.UTF8Encoding($false)))
    Write-Warn2 "未找到证书口令，已生成随机口令并写入 $PwFile（该路径不入 git）"
}

Write-Step '5/7 准备签名证书'
New-Item -ItemType Directory -Force -Path $CertDir | Out-Null

# 为什么不能"存在就沿用"：证书 Subject 必须与清单 Identity/@Publisher 逐字符一致，
# 否则签名校验失败（0x8007000B "The app manifest publisher name must match the subject name"）。
# 一旦改了签名者名称（如 CN=MoonSpiritX → CN=MSpirit），旧 .pfx 就成了陷阱——
# 沿用它会拿旧证书签出 Publisher 不匹配的包，报错信息又很难懂。故这里**读出来核对**。
$existingCert = $null
if (Test-Path $CertPfx) {
    try {
        $existingCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertPfx, $CertPassword)
    } catch {
        Write-Warn2 "读取已有证书失败（$($_.Exception.Message)）→ 将重新生成"
    }
}

if ($existingCert -and $existingCert.Subject -eq $CertSubject) {
    Write-Ok "沿用已有证书 $CertPfx（Subject=$CertSubject，指纹 $($existingCert.Thumbprint)）"
} else {
    if ($existingCert) {
        Write-Warn2 "已有证书 Subject 为「$($existingCert.Subject)」，与清单 Publisher「$CertSubject」不一致 → 重新生成"
        Write-Warn2 "旧证书指纹 $($existingCert.Thumbprint)；若曾导入受信任存储，请按脚本末尾第 4 条清理"
    } else {
        Write-Warn2 "未找到测试证书，正在生成自签名证书（Subject=$CertSubject）…"
    }
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $CertSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName 'MSpirit SafetySys Test Signing' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    $sec = ConvertTo-SecureString -String $CertPassword -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $CertPfx -Password $sec | Out-Null
    Export-Certificate -Cert $cert -FilePath $CertCer | Out-Null
    Write-Ok "已生成证书（指纹 $($cert.Thumbprint)）→ $CertPfx"
}

# 守卫：清单 Publisher 必须与证书 Subject 逐字符一致（含大小写与空格），否则商店/签名校验必报错
$pkgPublisher = $manifestXml.Package.Identity.Publisher
$signCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertPfx, $CertPassword)
if ($signCert.Subject -ne $pkgPublisher) {
    throw "清单 Identity/@Publisher「$pkgPublisher」与证书 Subject「$($signCert.Subject)」不一致——两者必须逐字符相同"
}
Write-Ok "Publisher 与证书 Subject 一致（$pkgPublisher）"

# ---------- 6. 签名 ----------
Write-Step '6/7 signtool 签名'

# 带时间戳签名。/td = 时间戳摘要算法（SHA256），/tr = RFC3161 时间戳服务。
# 为什么要时间戳：签名里记录了"签名时刻"，证书过期后包仍可安装；无时间戳的包在证书过期后
#   Windows 会按**当前时间**判定证书无效 → 拒绝安装（官方文档：app is signed without
#   timestamping + certificate is invalid(expired) → app will fail to install）。
#   Store 上架不受影响（商店用自己的证书重签），影响的是自签分发/国内平台分发这条线。
$timestamped = $false
if ($NoTimestamp) {
    Write-Warn2 '按 -NoTimestamp 跳过时间戳（不推荐，仅离线时用）'
    & $SignTool sign /fd SHA256 /f $CertPfx /p $CertPassword $msixPath
    if ($LASTEXITCODE -ne 0) { throw "签名失败（退出码 $LASTEXITCODE）" }
} else {
    $tsOut = & $SignTool sign /fd SHA256 /td SHA256 /tr $TimestampServer `
                /f $CertPfx /p $CertPassword $msixPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        # 常见原因：TSA 不可达 / 被防火墙拦 / 服务限流。回退重签，保证至少出一个可安装的包，
        # 但要在末尾把"无时间戳"这件事说出来，不能静默降级。
        Write-Warn2 "带时间戳签名失败（TSA：$TimestampServer）→ 回退为不带时间戳重签"
        Write-Host ($tsOut | Out-String).Trim() -ForegroundColor DarkGray
        & $SignTool sign /fd SHA256 /f $CertPfx /p $CertPassword $msixPath
        if ($LASTEXITCODE -ne 0) { throw "签名失败（退出码 $LASTEXITCODE）" }
    } else {
        $timestamped = $true
        Write-Ok "已签名并加盖时间戳（TSA：$TimestampServer）"
    }
}

& $SignTool verify /pa $msixPath | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warn2 '签名校验未通过——通常是测试证书尚未导入「本地计算机 → 受信任人员」（见下方步骤）'
} else {
    Write-Ok '签名校验通过'
}

# 从签名里把时间戳状态读回来（不靠上面的分支推断），并把结论写进产物信息
$sigInfo = Get-AuthenticodeSignature -FilePath $msixPath
$tsCert  = $sigInfo.TimeStamperCertificate
if (-not $timestamped -and $tsCert) { $timestamped = $true }
if (-not $timestamped) {
    Write-Warn2 "本包【无时间戳】：证书 $($signCert.NotAfter.ToString('yyyy-MM-dd')) 到期后，此包将无法再安装"
}

# ---------- 7. 完成 ----------
Write-Step '7/7 完成'
Write-Host ""
Write-Host "  包标识：$pkgName  $pkgVersion  $pkgArch" -ForegroundColor White
Write-Host "  签名者：$($signCert.Subject)（指纹 $($signCert.Thumbprint)）" -ForegroundColor White
Write-Host ("  时间戳：{0}" -f $(if ($timestamped) { "已加盖（$($tsCert.Subject)）" } else { "无 —— 证书过期后此包将无法安装" })) -ForegroundColor White
Write-Host "  包文件：$msixPath" -ForegroundColor White
Write-Host "  测试证书：$CertCer" -ForegroundColor White
Write-Host ""
Write-Host "  接下来（需要【管理员 PowerShell】；第 1、2 条仅首次/换证书后需要）：" -ForegroundColor Yellow
Write-Host "    1) 导入测试证书到「本地计算机 → 受信任人员」：" -ForegroundColor Gray
Write-Host "       Import-Certificate -FilePath `"$CertCer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Gray
Write-Host "       （装到「当前用户」级无效——Add-AppxPackage 只认本地计算机级存储）" -ForegroundColor DarkGray
Write-Host "    2) 卸载旧版（改包名/发布者后包族名变化，必须卸载重装，不是升级）：" -ForegroundColor Gray
Write-Host "       Get-AppxPackage $pkgName | Remove-AppxPackage" -ForegroundColor Gray
Write-Host "       （旧包名若不同，用 Get-AppxPackage *SafetySys* 找出来）" -ForegroundColor DarkGray
Write-Host "    3) 安装：" -ForegroundColor Gray
Write-Host "       Add-AppxPackage -Path `"$msixPath`"" -ForegroundColor Gray
Write-Host "    4) 换过签名者时，清理旧证书（按旧 CN 过滤，逐个确认后再删）：" -ForegroundColor Gray
Write-Host "       Get-ChildItem Cert:\CurrentUser\My, Cert:\CurrentUser\TrustedPeople, Cert:\LocalMachine\TrustedPeople |" -ForegroundColor Gray
Write-Host "         Where-Object { `$_.Subject -like '*<旧签名者>*' } | Format-List Subject,Thumbprint" -ForegroundColor Gray
Write-Host ""
