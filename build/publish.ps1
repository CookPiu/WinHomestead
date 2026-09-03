# Release 构建，产物复制到 artifacts\。M4 起在此接入 Costura 单文件与签名。
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = 'D:\Applications\dotnet'
if (Test-Path "$sdk\dotnet.exe") { $env:DOTNET_ROOT = $sdk; $env:PATH = "$sdk;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$out = Join-Path $root 'artifacts'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet build "$root\src\NewPcSetup.App\NewPcSetup.App.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$bin = Join-Path $root 'src\NewPcSetup.App\bin\Release\net48'
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item "$bin\*" $out -Recurse -Force
$exe = Get-Item (Join-Path $out 'NewPcSetup.exe')
"产物: $($exe.FullName)  $([math]::Round($exe.Length/1MB,2)) MB"
