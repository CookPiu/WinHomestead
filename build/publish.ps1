# Release 构建（Costura 单 exe）→ 校验产物 → 可选签名 → 复制到 artifacts\。
# 用法：
#   .\build\publish.ps1                       # 构建 + 校验
#   .\build\publish.ps1 -Thumbprint <sha1>    # 构建 + 用本机证书库中的代码签名证书签名
#   .\build\publish.ps1 -Thumbprint <sha1> -TimestampUrl http://timestamp.digicert.com
# 前置：D:\Applications\dotnet 下有 .NET 10 SDK；签名需要 Windows SDK 的 signtool.exe。
param(
  [string]$Thumbprint,
  [string]$TimestampUrl = 'http://timestamp.digicert.com',
  [int]$MaxSizeMB = 10
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = 'D:\Applications\dotnet'
if (Test-Path "$sdk\dotnet.exe") { $env:DOTNET_ROOT = $sdk; $env:PATH = "$sdk;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$out = Join-Path $root 'artifacts'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

# 1. Release 构建（Costura 在 Release 织入）
dotnet build "$root\src\WinHomestead.App\WinHomestead.App.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$bin = Join-Path $root 'src\WinHomestead.App\bin\Release\net48'
$exe = Get-Item (Join-Path $bin 'WinHomestead.exe')

# 2. 校验：依赖已嵌入（exe 内含 costura 资源）、体积上限
$asm = [System.Reflection.Assembly]::LoadFile($exe.FullName)
$embedded = @($asm.GetManifestResourceNames() | Where-Object { $_ -like 'costura.*' })
if ($embedded.Count -eq 0) { throw "Costura 未织入：exe 中没有 costura.* 资源" }
foreach ($must in 'wpf.ui', 'communitytoolkit.mvvm', 'system.text.json', 'winhomestead.core', 'winhomestead.tasks', 'winhomestead.native') {
  if (-not ($embedded | Where-Object { $_ -like "costura.$must.dll*" })) { throw "缺少嵌入依赖: $must" }
}
$sizeMB = [math]::Round($exe.Length / 1MB, 2)
if ($sizeMB -gt $MaxSizeMB) { throw "产物 $sizeMB MB 超过上限 $MaxSizeMB MB" }

# 3. 复制：单 exe + 配置文件（绑定重定向与 supportedRuntime，体积可忽略）
Copy-Item $exe.FullName $out
Copy-Item (Join-Path $bin 'WinHomestead.exe.config') $out
$target = Join-Path $out 'WinHomestead.exe'

# 4. 可选签名
if ($Thumbprint) {
  $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $signtool) { throw "找不到 signtool.exe，请安装 Windows SDK" }
  & $signtool.FullName sign /fd SHA256 /sha1 $Thumbprint /tr $TimestampUrl /td SHA256 $target
  if ($LASTEXITCODE -ne 0) { throw "signtool 失败" }
  & $signtool.FullName verify /pa $target
  if ($LASTEXITCODE -ne 0) { throw "签名校验失败" }
}

"产物: $target  $sizeMB MB  嵌入依赖 $($embedded.Count) 个" + $(if ($Thumbprint) { '  已签名' } else { '  未签名' })
