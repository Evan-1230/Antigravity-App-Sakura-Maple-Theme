param(
    [string]$TestRoot = "",
    [switch]$NonInteractive
)

$ErrorActionPreference = "Stop"

$IsSandbox = -not [string]::IsNullOrWhiteSpace($TestRoot)
if ($IsSandbox) {
    $UserProfile = Join-Path $TestRoot "UserProfile"
    $AppData = Join-Path $TestRoot "AppData"
    $LocalAppData = Join-Path $TestRoot "LocalAppData"
} else {
    $UserProfile = $env:USERPROFILE
    $AppData = $env:APPDATA
    $LocalAppData = $env:LOCALAPPDATA
}

$InstallDir = Join-Path $LocalAppData "AntigravitySakuraMapleTheme"
$BackupDir = Join-Path $InstallDir "backup"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Antigravity 櫻花 & 楓葉主題還原程式" -ForegroundColor Cyan
Write-Host "  Antigravity Sakura & Maple Theme Uninstaller" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

if (-not $NonInteractive) {
    $ans = Read-Host "確定要移除櫻花＆楓葉主題並還原原本的設定與圖示嗎？ / Restore original theme and icons? [Y/n]"
    if (-not [string]::IsNullOrWhiteSpace($ans) -and $ans.Trim().ToLowerInvariant() -eq "n") {
        Write-Host "已取消還原。 / Cancelled." -ForegroundColor Yellow
        exit 0
    }
    Write-Host ""
}

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

function Restore-JsonProp {
    param($Obj, [string]$JsonKey, $SnapEntry)
    if ($null -ne $SnapEntry -and $SnapEntry.present) {
        Set-JsonProp -Obj $Obj -Name $JsonKey -Value $SnapEntry.value
    } else {
        Remove-JsonProp -Obj $Obj -Name $JsonKey
    }
}

# 1. Stop running overlay & remove startup registry entry
if (-not $IsSandbox) {
    Write-Host "[1/4] 正在停止特效程式與清除開機啟動項 / Stopping overlay & cleaning startup entry..." -ForegroundColor Cyan
    $running = Get-Process -Name "AntigravitySakuraMapleOverlay" -ErrorAction SilentlyContinue
    if ($running) {
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 350
    }
    $runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    if (Test-Path -LiteralPath $runKeyPath) {
        $regVal = Get-ItemProperty -Path $runKeyPath -Name "AntigravitySakuraMapleOverlay" -ErrorAction SilentlyContinue
        if ($null -ne $regVal) {
            Remove-ItemProperty -Path $runKeyPath -Name "AntigravitySakuraMapleOverlay" -ErrorAction SilentlyContinue
        }
    }
} else {
    Write-Host "[1/4] 正在進行沙盒還原 / Running sandbox uninstall..." -ForegroundColor Cyan
}

# 2. Smart theme restoration (preserves plugins & settings added after installation)
Write-Host "[2/4] 正在還原 Antigravity 主題色票設定 / Restoring Antigravity color settings..." -ForegroundColor Cyan
$snapshotPath = Join-Path $BackupDir "theme_before.json"
$snapshot = if (Test-Path -LiteralPath $snapshotPath) { Read-JsonFile $snapshotPath } else { $null }

$configPath = Join-Path $UserProfile ".gemini\config\config.json"
if (Test-Path -LiteralPath $configPath) {
    $cfg = Read-JsonFile $configPath
    if (Has-JsonProp $cfg "userSettings" -and $null -ne $cfg.userSettings) {
        $cfgSnap = if ($null -ne $snapshot -and (Has-JsonProp $snapshot "config")) { $snapshot.config } else { $null }
        $snapThemeMode = if ($null -ne $cfgSnap) { $cfgSnap.themeMode } else { $null }
        $snapDark = if ($null -ne $cfgSnap) { $cfgSnap.customThemeSeedsDark } else { $null }
        $snapLight = if ($null -ne $cfgSnap) { $cfgSnap.customThemeSeedsLight } else { $null }
        Restore-JsonProp -Obj $cfg.userSettings -JsonKey "themeMode" -SnapEntry $snapThemeMode
        Restore-JsonProp -Obj $cfg.userSettings -JsonKey "customThemeSeedsDark" -SnapEntry $snapDark
        Restore-JsonProp -Obj $cfg.userSettings -JsonKey "customThemeSeedsLight" -SnapEntry $snapLight
        Write-Utf8NoBom -Path $configPath -Text ($cfg | ConvertTo-Json -Depth 32)
    }
}

$storagePath = Join-Path $AppData "Antigravity\app_storage.json"
if (Test-Path -LiteralPath $storagePath) {
    $st = Read-JsonFile $storagePath
    $stSnap = if ($null -ne $snapshot -and (Has-JsonProp $snapshot "appStorage")) { $snapshot.appStorage } else { $null }
    $snapHub = if ($null -ne $stSnap) { $stSnap.hubSurfaceBackgroundColor } else { $null }
    $snapPresetDark = if ($null -ne $stSnap) { $stSnap.themePresetDark } else { $null }
    $snapPresetLight = if ($null -ne $stSnap) { $stSnap.themePresetLight } else { $null }
    Restore-JsonProp -Obj $st -JsonKey "hubSurfaceBackgroundColor" -SnapEntry $snapHub
    Restore-JsonProp -Obj $st -JsonKey "theme-preset-dark" -SnapEntry $snapPresetDark
    Restore-JsonProp -Obj $st -JsonKey "theme-preset-light" -SnapEntry $snapPresetLight
    Write-Utf8NoBom -Path $storagePath -Text ($st | ConvertTo-Json -Depth 32)
}

# 3. Restore shortcuts
Write-Host "[3/4] 正在還原捷徑圖示 / Restoring shortcut icons..." -ForegroundColor Cyan
$deskLnk = Join-Path $UserProfile "Desktop\Antigravity.lnk"
$startMenuDir = Join-Path $AppData "Microsoft\Windows\Start Menu\Programs"
$startLnk = Join-Path $startMenuDir "Antigravity.lnk"
$overlayStartLnk = Join-Path $startMenuDir "Antigravity 櫻花楓葉特效.lnk"

$wsh = New-Object -ComObject WScript.Shell
try {
    foreach ($entry in @(
        @{ Path = $deskLnk; Bak = (Join-Path $BackupDir "Desktop_Antigravity.lnk.bak") },
        @{ Path = $startLnk; Bak = (Join-Path $BackupDir "StartMenu_Antigravity.lnk.bak") }
    )) {
        if (Test-Path -LiteralPath $entry.Bak) {
            Copy-Item -LiteralPath $entry.Bak -Destination $entry.Path -Force
        } elseif (Test-Path -LiteralPath $entry.Path) {
            $sc = $wsh.CreateShortcut($entry.Path)
            try {
                if ($sc.IconLocation -like "*antigravity_sakura_maple*") {
                    $sc.IconLocation = ",0"
                    $sc.Save()
                }
            } finally {
                [System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc) | Out-Null
            }
        }
    }
} finally {
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($wsh) | Out-Null
}

if (Test-Path -LiteralPath $overlayStartLnk) {
    Remove-Item -LiteralPath $overlayStartLnk -Force -ErrorAction SilentlyContinue
}

if (-not $IsSandbox) {
    try {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ShellIconRefreshUninstall {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
"@ -ErrorAction SilentlyContinue
        [ShellIconRefreshUninstall]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)
    } catch {
    }
}

# 4. Remove installed files
Write-Host "[4/4] 正在清理主題檔案 / Removing installed theme files..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $InstallDir) {
    $resolvedInstall = (Resolve-Path -LiteralPath $InstallDir).Path
    $resolvedScriptRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
    $runningInsideInstall = [string]::Equals($resolvedInstall, $resolvedScriptRoot, [System.StringComparison]::OrdinalIgnoreCase)

    foreach ($item in @("AntigravitySakuraMapleOverlay.exe", "啟動櫻花楓葉特效.lnk", "overlay_lang.txt", "assets", "src", "backup")) {
        $p = Join-Path $InstallDir $item
        if (Test-Path -LiteralPath $p) {
            Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not $runningInsideInstall) {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "註：因正在從安裝目錄執行還原，關閉此視窗後可手動刪除空資料夾：$InstallDir" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  已完整還原為原本設定！ / Theme Restored Successfully!" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "若 Antigravity 目前仍開啟，請在視窗內按 Ctrl + R 更新介面顏色。"
Write-Host "If Antigravity is currently open, press Ctrl + R inside it to refresh colors."
