# AI 课堂助手 - 关机前自动导出（任务计划 AtShutdown 调用）
# 请求核心优雅停机：结束当前课程、未归档监控导出、清理过期录像、释放设备
$ErrorActionPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$cfgPath = Join-Path $root "data\config\config.json"
$port = 18760
if (Test-Path $cfgPath) {
    try {
        $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
        if ($cfg.web_port) { $port = [int]$cfg.web_port }
    } catch {}
}
$deadline = (Get-Date).AddSeconds(20)

# 等待核心 Web 服务就绪（最多 20 秒）
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) { break }
    } catch {
        Start-Sleep -Milliseconds 500
    }
}

try {
    $null = Invoke-WebRequest -Uri "http://127.0.0.1:$port/api/shutdown" -Method POST -UseBasicParsing -TimeoutSec 3
    Write-Host "核心已优雅停机（导出完成）"
} catch {
    # 核心未运行或已退出：兜底执行未归档导出
    $script = Join-Path $root "assistant_core\exporter_standalone.py"
    if (Test-Path $script) {
        python "`"$script`""
        Write-Host "兜底导出完成"
    }
}
