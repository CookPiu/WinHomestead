# Debug 构建 + 单元测试。前置：D:\Applications\dotnet 下有 .NET 10 SDK。
param([switch]$NoTest)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sdk = 'D:\Applications\dotnet'
if (Test-Path "$sdk\dotnet.exe") { $env:DOTNET_ROOT = $sdk; $env:PATH = "$sdk;$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $root
try {
  dotnet build "$root\WinHomestead.slnx" -c Debug
  if ($LASTEXITCODE -ne 0) { throw "build failed" }
  if (-not $NoTest) {
    dotnet test "$root\WinHomestead.slnx" -c Debug --no-build
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
  }
} finally { Pop-Location }
