param(
    [string]$TestRoot = "",
    [ValidateSet("", "Dark", "Light", "Inherit", "Keep")]
    [string]$DefaultMode = "",
    [switch]$NonInteractive,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

# Resolve target roots (real machine vs isolated sandbox test root)
$IsSandbox = -not [string]::IsNullOrWhiteSpace($TestRoot)
if ($IsSandbox) {
    $UserProfile = Join-Path $TestRoot "UserProfile"
    $AppData = Join-Path $TestRoot "AppData"
    $LocalAppData = Join-Path $TestRoot "LocalAppData"
    foreach ($dir in @($UserProfile, $AppData, $LocalAppData)) {
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }
} else {
    $UserProfile = $env:USERPROFILE
    $AppData = $env:APPDATA
    $LocalAppData = $env:LOCALAPPDATA
}

$PkgRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$InstallDir = Join-Path $LocalAppData "AntigravitySakuraMapleTheme"
$BackupDir = Join-Path $InstallDir "backup"

Write-Host "============================================================" -ForegroundColor Magenta
Write-Host "  Antigravity 櫻花 & 楓葉主題安裝程式" -ForegroundColor Magenta
Write-Host "  Antigravity Sakura & Maple Theme Installer (Option A)" -ForegroundColor Magenta
Write-Host "============================================================" -ForegroundColor Magenta
Write-Host ""

# Choose default theme mode
if ([string]::IsNullOrWhiteSpace($DefaultMode)) {
    if ($NonInteractive) {
        $DefaultMode = "Light"
    } else {
        Write-Host "請選擇預設主題模式 / Choose default theme mode:" -ForegroundColor Cyan
        Write-Host "  [1] 淺色櫻花 Light Sakura (預設 / Default)"
        Write-Host "  [2] 深色楓葉 Dark Maple"
        Write-Host "  [3] 跟隨系統 System Inherit"
        Write-Host "  [4] 保持目前模式 Keep current"
        $choice = Read-Host "請輸入選項 [1-4, Enter=1]"
        switch ($choice.Trim()) {
            "2" { $DefaultMode = "Dark" }
            "3" { $DefaultMode = "Inherit" }
            "4" { $DefaultMode = "Keep" }
            default { $DefaultMode = "Light" }
        }
        Write-Host ""
    }
}

# Helper functions
function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $tmp = "$Path.$PID.tmp"
    [System.IO.File]::WriteAllText($tmp, $Text, $utf8NoBom)
    $moved = $false
    foreach ($delay in @(0, 50, 120, 250)) {
        if ($delay -gt 0) { Start-Sleep -Milliseconds $delay }
        try {
            Move-Item -LiteralPath $tmp -Destination $Path -Force
            $moved = $true
            break
        } catch {
            continue
        }
    }
    if (-not $moved) {
        try {
            [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
        } finally {
            if (Test-Path -LiteralPath $tmp) {
                Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Read-JsonFile {
    param([string]$Path)
    $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return (New-Object PSObject)
    }
    return ($raw | ConvertFrom-Json)
}

function Has-JsonProp {
    param($Obj, [string]$Name)
    if ($null -eq $Obj) { return $false }
    return ($null -ne $Obj.PSObject.Properties[$Name])
}

function Set-JsonProp {
    param($Obj, [string]$Name, $Value)
    Add-Member -InputObject $Obj -MemberType NoteProperty -Name $Name -Value $Value -Force
}

function Remove-JsonProp {
    param($Obj, [string]$Name)
    if (Has-JsonProp $Obj $Name) {
        $Obj.PSObject.Properties.Remove($Name) | Out-Null
    }
}

function Get-PropSnapshot {
    param($Obj, [string]$Name)
    if (Has-JsonProp $Obj $Name) {
        return [PSCustomObject]@{
            present = $true
            value   = $Obj.PSObject.Properties[$Name].Value
        }
    }
    return [PSCustomObject]@{
        present = $false
        value   = $null
    }
}

# 1. Stop running overlay if upgrading on live host
if (-not $IsSandbox) {
    $running = Get-Process -Name "AntigravitySakuraMapleOverlay" -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "[1/5] 正在關閉目前執行中的特效程式 / Stopping running overlay..." -ForegroundColor Yellow
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 350
    } else {
        Write-Host "[1/5] 準備安裝目錄 / Preparing install directory..." -ForegroundColor Cyan
    }
} else {
    Write-Host "[1/5] 準備沙盒安裝目錄 / Preparing sandbox install directory..." -ForegroundColor Cyan
}

# 2. Copy assets, source, and scripts into permanent install dir
foreach ($sub in @("", "assets", "src", "scripts", "backup")) {
    $targetSub = if ([string]::IsNullOrEmpty($sub)) { $InstallDir } else { Join-Path $InstallDir $sub }
    if (-not (Test-Path -LiteralPath $targetSub)) {
        New-Item -ItemType Directory -Path $targetSub -Force | Out-Null
    }
}

$resolvedInstall = (Resolve-Path -LiteralPath $InstallDir).Path
if (-not [string]::Equals($PkgRoot, $resolvedInstall, [System.StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath (Join-Path $PkgRoot "src\Program.cs") -Destination (Join-Path $InstallDir "src\Program.cs") -Force
    foreach ($assetName in @("antigravity_sakura_maple.ico", "antigravity_sakura_maple.png", "maple_bg_dark.png", "sakura_bg_light.png")) {
        $srcAsset = Join-Path $PkgRoot ("assets\" + $assetName)
        if (Test-Path -LiteralPath $srcAsset) {
            Copy-Item -LiteralPath $srcAsset -Destination (Join-Path $InstallDir ("assets\" + $assetName)) -Force
        }
    }
    foreach ($scriptName in @("install.ps1", "uninstall.ps1")) {
        $srcScript = Join-Path $PkgRoot ("scripts\" + $scriptName)
        if (Test-Path -LiteralPath $srcScript) {
            Copy-Item -LiteralPath $srcScript -Destination (Join-Path $InstallDir ("scripts\" + $scriptName)) -Force
        }
    }
    foreach ($rootFile in @("Install_theme.bat", "Uninstall_default.bat", "README.md", "README.tw.md", "LICENSE")) {
        $srcFile = Join-Path $PkgRoot $rootFile
        if (Test-Path -LiteralPath $srcFile) {
            Copy-Item -LiteralPath $srcFile -Destination (Join-Path $InstallDir $rootFile) -Force
        }
    }
}

# 3. Compile AntigravitySakuraMapleOverlay.exe locally using Windows built-in .NET 4.x csc.exe
Write-Host "[2/5] 正在本機編譯特效程式 / Compiling overlay locally via .NET csc.exe..." -ForegroundColor Cyan
$fwCandidates = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319"
)
$fwDir = $null
foreach ($cand in $fwCandidates) {
    if (Test-Path -LiteralPath (Join-Path $cand "csc.exe")) {
        $fwDir = $cand
        break
    }
}
if ($null -eq $fwDir) {
    throw "找不到 Windows 內建 .NET Framework 4.x 編譯器 (csc.exe)。 / Could not locate .NET Framework 4.x csc.exe."
}

$cscExe = Join-Path $fwDir "csc.exe"
$srcCs = Join-Path $InstallDir "src\Program.cs"
$icoPath = Join-Path $InstallDir "assets\antigravity_sakura_maple.ico"
$exeOut = Join-Path $InstallDir "AntigravitySakuraMapleOverlay.exe"

$cscArgs = @(
    "/nologo",
    "/optimize+",
    "/target:winexe",
    "/platform:anycpu",
    "/win32icon:$icoPath",
    "/out:$exeOut",
    "/r:$fwDir\WPF\PresentationFramework.dll",
    "/r:$fwDir\WPF\PresentationCore.dll",
    "/r:$fwDir\WPF\WindowsBase.dll",
    "/r:$fwDir\System.Xaml.dll",
    "/r:$fwDir\System.Windows.Forms.dll",
    "/r:$fwDir\System.Drawing.dll",
    $srcCs
)
$compileOutput = & $cscExe @cscArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    $compileOutput | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw "編譯失敗 (ExitCode=$LASTEXITCODE)。 / Compilation failed."
}

# 4. Back up and safely apply theme color seeds (UTF-8 without BOM)
Write-Host "[3/5] 正在備份並套用櫻花＆楓葉色票 / Backing up & applying theme colors..." -ForegroundColor Cyan
$configPath = Join-Path $UserProfile ".gemini\config\config.json"
$storagePath = Join-Path $AppData "Antigravity\app_storage.json"
$snapshotPath = Join-Path $BackupDir "theme_before.json"

$configExisted = Test-Path -LiteralPath $configPath
$storageExisted = Test-Path -LiteralPath $storagePath

$cfg = if ($configExisted) { Read-JsonFile $configPath } else { New-Object PSObject }
$st = if ($storageExisted) { Read-JsonFile $storagePath } else { $null }

if (-not (Test-Path -LiteralPath $snapshotPath)) {
    if ($configExisted -and -not (Test-Path -LiteralPath (Join-Path $BackupDir "config.json.bak"))) {
        Copy-Item -LiteralPath $configPath -Destination (Join-Path $BackupDir "config.json.bak") -Force
    }
    if ($storageExisted -and -not (Test-Path -LiteralPath (Join-Path $BackupDir "app_storage.json.bak"))) {
        Copy-Item -LiteralPath $storagePath -Destination (Join-Path $BackupDir "app_storage.json.bak") -Force
    }

    $usOld = if (Has-JsonProp $cfg "userSettings") { $cfg.userSettings } else { $null }
    $snapshot = [PSCustomObject]@{
        config = [PSCustomObject]@{
            existed               = $configExisted
            themeMode             = (Get-PropSnapshot $usOld "themeMode")
            customThemeSeedsDark  = (Get-PropSnapshot $usOld "customThemeSeedsDark")
            customThemeSeedsLight = (Get-PropSnapshot $usOld "customThemeSeedsLight")
        }
        appStorage = [PSCustomObject]@{
            existed                   = $storageExisted
            hubSurfaceBackgroundColor = (Get-PropSnapshot $st "hubSurfaceBackgroundColor")
            themePresetDark           = (Get-PropSnapshot $st "theme-preset-dark")
            themePresetLight          = (Get-PropSnapshot $st "theme-preset-light")
        }
    }
    Write-Utf8NoBom -Path $snapshotPath -Text ($snapshot | ConvertTo-Json -Depth 32)
}

if (-not (Has-JsonProp $cfg "userSettings") -or $null -eq $cfg.userSettings) {
    Set-JsonProp -Obj $cfg -Name "userSettings" -Value (New-Object PSObject)
}

$mapleDark = [PSCustomObject]@{
    background                = "#1C1414"
    primary                   = "#EA6C4D"
    foregroundOverride        = "#E5D3C8"
    primaryForegroundOverride = "#1C1414"
    codeForegroundOverride    = "#F3AE6D"
}
$sakuraLight = [PSCustomObject]@{
    background                = "#FFF6F8"
    primary                   = "#BE3E6B"
    foregroundOverride        = "#3A222A"
    primaryForegroundOverride = "#FFF9FB"
    codeForegroundOverride    = "#982E55"
}

Set-JsonProp -Obj $cfg.userSettings -Name "customThemeSeedsDark" -Value $mapleDark
Set-JsonProp -Obj $cfg.userSettings -Name "customThemeSeedsLight" -Value $sakuraLight

switch ($DefaultMode) {
    "Dark"    { Set-JsonProp -Obj $cfg.userSettings -Name "themeMode" -Value "THEME_MODE_DARK" }
    "Light"   { Set-JsonProp -Obj $cfg.userSettings -Name "themeMode" -Value "THEME_MODE_LIGHT" }
    "Inherit" { Set-JsonProp -Obj $cfg.userSettings -Name "themeMode" -Value "THEME_MODE_INHERIT" }
    "Keep"    { }
}

Write-Utf8NoBom -Path $configPath -Text ($cfg | ConvertTo-Json -Depth 32)

if ($storageExisted -and $null -ne $st) {
    $effectiveLight = $false
    if ($DefaultMode -eq "Light") {
        $effectiveLight = $true
    } elseif ($DefaultMode -eq "Keep" -and (Has-JsonProp $cfg.userSettings "themeMode")) {
        if ($cfg.userSettings.themeMode -eq "THEME_MODE_LIGHT") {
            $effectiveLight = $true
        }
    }
    $hubBg = if ($effectiveLight) { "#FFF6F8" } else { "#1C1414" }
    Set-JsonProp -Obj $st -Name "hubSurfaceBackgroundColor" -Value $hubBg
    Remove-JsonProp -Obj $st -Name "theme-preset-dark"
    Remove-JsonProp -Obj $st -Name "theme-preset-light"
    Write-Utf8NoBom -Path $storagePath -Text ($st | ConvertTo-Json -Depth 32)
}

# 5. Back up and apply custom icon to Antigravity Windows shortcuts
Write-Host "[4/5] 正在套用專屬捷徑 Icon / Updating Antigravity shortcut icons..." -ForegroundColor Cyan
$agCandidates = @(
    (Join-Path $LocalAppData "Programs\antigravity\Antigravity.exe"),
    (Join-Path $LocalAppData "Programs\Antigravity\Antigravity.exe")
)
$agExe = $null
foreach ($cand in $agCandidates) {
    if (Test-Path -LiteralPath $cand) {
        $agExe = $cand
        break
    }
}

$deskLnk = Join-Path $UserProfile "Desktop\Antigravity.lnk"
$startMenuDir = Join-Path $AppData "Microsoft\Windows\Start Menu\Programs"
$startLnk = Join-Path $startMenuDir "Antigravity.lnk"

$wsh = New-Object -ComObject WScript.Shell
try {
    foreach ($entry in @(
        @{ Path = $deskLnk; Bak = (Join-Path $BackupDir "Desktop_Antigravity.lnk.bak") },
        @{ Path = $startLnk; Bak = (Join-Path $BackupDir "StartMenu_Antigravity.lnk.bak") }
    )) {
        if (Test-Path -LiteralPath $entry.Path) {
            if (-not (Test-Path -LiteralPath $entry.Bak)) {
                Copy-Item -LiteralPath $entry.Path -Destination $entry.Bak -Force
            }
            $sc = $wsh.CreateShortcut($entry.Path)
            try {
                if ([string]::IsNullOrWhiteSpace($sc.TargetPath) -and $null -ne $agExe) {
                    $sc.TargetPath = $agExe
                    $sc.WorkingDirectory = Split-Path -Parent $agExe
                    if ([string]::IsNullOrWhiteSpace($sc.Description)) {
                        $sc.Description = "Antigravity"
                    }
                }
                $sc.IconLocation = "$icoPath,0"
                $sc.Save()
            } finally {
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc) | Out-Null
            }
        }
    }

    # Convenience overlay launcher shortcuts
    if (-not (Test-Path -LiteralPath $startMenuDir)) {
        New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
    }
    foreach ($lnkOut in @(
        (Join-Path $InstallDir "啟動櫻花楓葉特效.lnk"),
        (Join-Path $startMenuDir "Antigravity 櫻花楓葉特效.lnk")
    )) {
        $sc = $wsh.CreateShortcut($lnkOut)
        try {
            $sc.TargetPath = $exeOut
            $sc.WorkingDirectory = $InstallDir
            $sc.Description = "Antigravity 櫻花與楓葉主題特效"
            $sc.IconLocation = "$icoPath,0"
            $sc.Save()
        } finally {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc) | Out-Null
        }
    }
} finally {
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($wsh) | Out-Null
}

if (-not $IsSandbox) {
    try {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ShellIconRefresh {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
"@ -ErrorAction SilentlyContinue
        [ShellIconRefresh]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)
    } catch {
    }
}

# 6. Launch overlay
if (-not $NoLaunch -and -not $IsSandbox) {
    Write-Host "[5/5] 正在啟動櫻花＆楓葉浮層特效 / Starting overlay effect..." -ForegroundColor Cyan
    Start-Process -FilePath $exeOut -WorkingDirectory $InstallDir
} else {
    Write-Host "[5/5] 安裝完成（依參數略過自動啟動） / Installation complete (launch skipped)." -ForegroundColor Cyan
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  安裝完成！ / Installation Complete!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "安裝位置 / Install Path: $InstallDir"
Write-Host ""
Write-Host "【使用提醒 / Usage Tips】" -ForegroundColor Yellow
Write-Host "1. 若 Antigravity 已開啟且顏色尚未切換，請在視窗內按 Ctrl + R 重新整理。"
Write-Host "   If Antigravity is open and colors haven't updated yet, press Ctrl + R inside Antigravity."
Write-Host "2. 按 Ctrl + , 進入 Settings > Appearance 即可切換 Dark(楓葉) / Light(櫻花)。"
Write-Host "   Press Ctrl + , (Settings > Appearance) to switch between Dark (Maple) and Light (Sakura)."
Write-Host "3. 右下角系統匣圖示按右鍵可切換模式、調整花枝背景與落葉密度，或勾選「開機時自動啟動」。"
Write-Host "   Right-click the system tray icon to adjust density/opacity or enable Run at Startup."
Write-Host "4. 如需完整還原，隨時執行 Uninstall_default.bat 即可。"
Write-Host "   Run Uninstall_default.bat anytime to restore original theme & icons."
