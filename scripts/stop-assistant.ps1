# AI 课堂助手 - 关机前自动导出（任务计划 AtShutdown 调用）
# 请求核心优雅停机：结束当前课程、归档监控、清理过期录像、释放设备。
$ErrorActionPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$cfgPath = Join-Path $root "data\config\config.json"

# 读实际 web 端口（核心在被占用时会顺延写回 config）
$port = 18760
if (Test-Path $cfgPath) {
    try {
        $cfg = Get-Content $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($cfg.web_port) { $port = [int]$cfg.web_port }
    } catch {}
}

# 通过 /api/state 判断核心是否真的活着（比只探 /api/health 更可靠）
$alive = $false
try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/state" -UseBasicParsing -TimeoutSec 3
    if ($r.StatusCode -eq 200) { $alive = $true }
} catch {}

if ($alive) {
    try {
        $null = Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/shutdown" -Method POST -UseBasicParsing -TimeoutSec 15
        Write-Host "核心已优雅停机（导出完成）"
    } catch {
        Write-Host "优雅停机请求失败，执行离线导出兜底"
        & python -m assistant_core.cli --export-only
    }
} else {
    # 核心未运行：离线导出兜底（不起感知线程、不起 Web）
    & python -m assistant_core.cli --export-only
    Write-Host "离线导出兜底完成"
}

# 托盘先于桥退出（托盘有自愈定时器会拉活桥）
Get-Process -Name "AssistantTray" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Get-Process -Name "ClassIslandBridge" -ErrorAction SilentlyContinue | Stop-Process -Force
