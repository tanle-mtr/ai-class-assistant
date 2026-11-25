# AI 课堂助手 v3 - 开机启动（任务计划 AtStartup 调用）
# 全原生 C# 单程序，无 Python / 无浏览器
$ErrorActionPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\App\bin\Release\net8.0-windows\AI课堂助手.exe"

if (-not (Test-Path $exe)) {
    Write-Host "未找到构建产物，请先执行：dotnet build src/AiClassAssistant.sln -c Release"
    exit 1
}

if (Get-Process -Name "AI课堂助手" -ErrorAction SilentlyContinue) {
    Write-Host "AI 课堂助手已在运行"
    exit 0
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden
Write-Host "AI 课堂助手已启动（托盘常驻）"
