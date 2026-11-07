#requires -version 5
<#
  把灵动岛注册为开机自启（写入 HKCU\...\Run，不需要管理员权限）
  用法：
    .\开机自启.ps1            开启
    .\开机自启.ps1 -Remove    关闭
    .\开机自启.ps1 -Status    查看状态
#>
[CmdletBinding()]
param(
    [switch]$Remove,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'DynamicIsland.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name = 'DynamicIsland'

function Get-Current {
    (Get-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue).$name
}

if ($Status) {
    $v = Get-Current
    if ($v) { Write-Host "已开启：$v" -ForegroundColor Green }
    else { Write-Host '未开启' -ForegroundColor Yellow }
    return
}

if ($Remove) {
    Remove-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue
    Write-Host '已关闭开机自启' -ForegroundColor Yellow
    return
}

if (-not (Test-Path $exe)) {
    Write-Host "找不到 DynamicIsland.exe：$exe" -ForegroundColor Red
    exit 1
}

New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name $name -Value ('"' + $exe + '"')
Write-Host "已开启开机自启：$exe" -ForegroundColor Green
Write-Host '（如果以后移动了文件夹，重新运行一次本脚本即可）' -ForegroundColor DarkGray
