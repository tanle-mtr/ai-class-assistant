# AI 课堂助手 v3 - 关机前优雅停机（任务计划 AtShutdown 调用）
# v3 是自包含 C# 程序：先离线归档未归类监控，再关闭程序与桥。
$ErrorActionPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\App\bin\Release\net8.0-windows\AI课堂助手.exe"

# 离线导出未归档数据（不起设备/界面）
if (Test-Path $exe) {
    & $exe --export-only
    Write-Host "离线归档完成"
}

# 先停主程序（其退出逻辑会先停感知再停桥，避免自愈定时器拉活）
Get-Process -Name "AI课堂助手" -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
}
Start-Sleep -Seconds 2
Get-Process -Name "AI课堂助手" -ErrorAction SilentlyContinue | Stop-Process -Force

Start-Sleep -Milliseconds 500
Get-Process -Name "AiClassAssistant.Bridge" -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "AI 课堂助手已停止"
