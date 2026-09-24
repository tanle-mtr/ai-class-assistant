# AI 课堂助手 - 开机启动（任务计划 AtStartup 调用）
# 若托盘未运行则启动托盘；托盘负责拉起 Python 核心（监控自动开启）
$ErrorActionPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$tray = Join-Path $root "tray\AssistantTray\bin\Release\net8.0-windows\AssistantTray.exe"
$trayAlt = Join-Path $root "dist\AssistantTray.exe"

$exe = $null
if (Test-Path $tray) { $exe = $tray }
elseif (Test-Path $trayAlt) { $exe = $trayAlt }

if (-not $exe) {
    # 托盘未构建：直接以模块方式启动 Python 核心（监控仍会工作）
    $py = "python"
    $script = Join-Path $root "assistant_core\main.py"
    if (Test-Path $script) {
        Start-Process -FilePath $py -ArgumentList "-m assistant_core.main" -WorkingDirectory $root -WindowStyle Hidden
        Write-Host "核心已直接启动（托盘未构建）"
        exit 0
    }
    Write-Host "未找到托盘与核心脚本"
    exit 1
}

# 检查是否已在运行
if (Get-Process -Name "AssistantTray" -ErrorAction SilentlyContinue) {
    Write-Host "托盘已在运行"
    exit 0
}
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
Write-Host "托盘已启动: $exe"
