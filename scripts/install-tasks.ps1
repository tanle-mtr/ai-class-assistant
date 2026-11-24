# AI 课堂助手 - 注册开机/关机任务计划（管理员运行）
# 用法：右键"以管理员身份运行"本脚本
$ErrorActionPreference = "Stop"

# 修 v1 的路径 bug：脚本就在 scripts/ 下，直接用 $PSScriptRoot
$startScript = Join-Path $PSScriptRoot "start-assistant.ps1"
$stopScript  = Join-Path $PSScriptRoot "stop-assistant.ps1"

if (-not (Test-Path $startScript) -or -not (Test-Path $stopScript)) {
    throw "未找到 start-assistant.ps1 / stop-assistant.ps1，请确认脚本完整"
}

# ---- 开机启动任务 ----
$actionStart = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$startScript`""
$triggerStart = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
Register-ScheduledTask -TaskName "AI课堂助手-开机启动" -Action $actionStart -Trigger $triggerStart -Settings $settings -Description "AI 课堂助手开机自动启动（托盘+核心+监控）" -Force | Out-Null
Write-Host "✔ 已注册开机启动任务"

# ---- 关机导出任务 ----
$actionStop = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$stopScript`""
$triggerStop = New-ScheduledTaskTrigger -AtShutdown
$settingsStop = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
Register-ScheduledTask -TaskName "AI课堂助手-关机导出" -Action $actionStop -Trigger $triggerStop -Settings $settingsStop -Description "AI 课堂助手关机前自动导出监控数据" -Force | Out-Null
Write-Host "✔ 已注册关机导出任务"

Write-Host ""
Write-Host "任务注册完成。可在 任务计划程序 中查看："
Write-Host "  - AI课堂助手-开机启动"
Write-Host "  - AI课堂助手-关机导出"
