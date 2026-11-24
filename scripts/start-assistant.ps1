# AI 课堂助手 - 开机启动（任务计划 AtStartup 调用）
# 优先启动托盘；托盘负责拉起 Python 核心与 ClassIsland 桥。
# 托盘未构建时，退化为直接启动 Python 核心。
$ErrorActionPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$tray = Join-Path $root "tray\AssistantTray\bin\Release\net8.0-windows\AssistantTray.exe"
$trayAlt = Join-Path $root "dist\AssistantTray.exe"

$exe = $null
if (Test-Path $tray) { $exe = $tray }
elseif (Test-Path $trayAlt) { $exe = $trayAlt }

if (-not $exe) {
    # 托盘未构建：直接以模块方式启动 Python 核心
    if (Get-Process -Name "python" -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -like "*$root*" -or $_.MainWindowTitle -like "*assistant*"
        }) {
        Write-Host "核心可能已在运行，跳过重复启动"
        exit 0
    }
    $py = "python"
    $entry = Join-Path $root "assistant_core\cli.py"
    if (Test-Path $entry) {
        Start-Process -FilePath $py -ArgumentList "-m assistant_core.cli" -WorkingDirectory $root -WindowStyle Hidden
        Write-Host "核心已直接启动（托盘未构建）"
        exit 0
    }
    Write-Host "未找到托盘与核心入口"
    exit 1
}

if (Get-Process -Name "AssistantTray" -ErrorAction SilentlyContinue) {
    Write-Host "托盘已在运行"
    exit 0
}
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
Write-Host "托盘已启动: $exe"
