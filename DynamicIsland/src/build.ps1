#requires -version 5
<#
  灵动岛 · 系统监控版 - 编译脚本
  使用 Windows 自带的 .NET Framework 编译器，无需安装任何 SDK。
  用法:  powershell -ExecutionPolicy Bypass -File build.ps1
#>
[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src  = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = $root

Write-Host "== 灵动岛 编译 ==" -ForegroundColor Cyan

# ---------- 定位编译器 ----------
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw "找不到 csc.exe，请确认已安装 .NET Framework 4.x" }
Write-Host "编译器: $csc"

# ---------- 定位引用程序集 ----------
$fw   = Split-Path -Parent $csc
$wpf  = Join-Path $fw 'WPF'
if (-not (Test-Path (Join-Path $wpf 'PresentationFramework.dll'))) {
    $wpf = $fw
}

function Find-Gac([string]$name) {
    $gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
    $hit = Get-ChildItem -Path $gac -Recurse -Filter $name -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -match 'GAC_MSIL' } | Select-Object -First 1
    if (-not $hit) { throw "找不到 GAC 程序集: $name" }
    return $hit.FullName
}

$refs = @(
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $fw  'System.Xaml.dll'),
    (Join-Path $fw  'System.dll'),
    (Join-Path $fw  'System.Core.dll'),
    (Join-Path $fw  'System.Drawing.dll'),
    (Find-Gac 'System.Web.Extensions.dll'),
    (Find-Gac 'System.Management.dll')
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "缺少引用: $r" } }

# ---------- 编译 ----------
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$sources = Get-ChildItem -Path $src -Filter *.cs | Select-Object -ExpandProperty FullName
$exe     = Join-Path $dist 'DynamicIsland.exe'

$cscArgs = @(
    '/nologo', '/utf8output', '/noconfig', '/target:winexe', '/optimize+', '/debug-',
    ('/out:"' + $exe + '"'),
    ('/win32manifest:"' + (Join-Path $src 'app.manifest') + '"')
)
foreach ($r in $refs)   { $cscArgs += ('/r:"' + $r + '"') }
foreach ($s in $sources){ $cscArgs += ('"' + $s + '"') }

Write-Host "编译 $($sources.Count) 个源文件 ..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

# ---------- 附带文件 ----------




$exeInfo = Get-Item $exe
Write-Host ("编译成功: " + $exe + "  (" + [math]::Round($exeInfo.Length/1kb) + " KB)") -ForegroundColor Green
Write-Host ("输出目录: " + $dist) -ForegroundColor Green

if ($Run) { Start-Process -FilePath $exe }


