# === 請在這裡修改你的 .py 檔案名稱 ===
$scriptFileName = "clear_expired_metrics.py"

# === 任務名稱 ===
$taskName = "clearJob"

# === 自動尋找 python.exe 的路徑（相容 PowerShell 5.1）===
$pythonCommand = Get-Command python -ErrorAction SilentlyContinue
if ($pythonCommand) {
    $pythonPath = $pythonCommand.Source
} else {
    Write-Error "❌ 找不到 python 指令，請確認已安裝 Python 並設定好環境變數。"
    exit 1
}

# === 取得目前 .ps1 檔案所在的資料夾 ===
$thisScriptPath = $MyInvocation.MyCommand.Path
$thisScriptDir = Split-Path $thisScriptPath -Parent

# === 僅在當前資料夾尋找 .py 檔案 ===
$scriptPath = Get-ChildItem -Path $thisScriptDir -Filter $scriptFileName -ErrorAction SilentlyContinue -File |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $scriptPath) {
    Write-Error "❌ 找不到檔案 '$scriptFileName'，請將它放在此 .ps1 同一個資料夾中。"
    exit 1
}

# === 建立排程任務 ===
$action = New-ScheduledTaskAction -Execute $pythonPath -Argument "`"$scriptPath`""
$trigger = New-ScheduledTaskTrigger -RepetitionInterval (New-TimeSpan -Minutes 5) -Once -At (Get-Date).AddMinutes(1)
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

# 如果任務已存在就先刪除
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Write-Host "🔁 已移除舊的 '$taskName'"
}

# 註冊新任務
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings

Write-Host "✅ 任務 '$taskName' 已建立，每 5 分鐘執行一次："
Write-Host "   - Python 路徑：$pythonPath"
Write-Host "   - 腳本路徑：$scriptPath"
