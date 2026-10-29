# End-to-end tests for the region-limit bypass.
#
# 说明：
# - 路径统一用 [IO.Path]::Combine 拼接（本环境下 Join-Path 在该脚本上下文中会返回空值）
# - 输出捕获统一用 ProcessStartInfo 重定向：被测程序里既有控制台程序也有 winexe，
#   PowerShell 的 `&` 管道捕获对二者行为不一致，直接读进程 stdout 最稳
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = [IO.Path]::Combine($root, 'bin')
$tool = [IO.Path]::Combine($bin, 'PCLRegionBypass.exe')
$target = [IO.Path]::Combine($bin, 'PolicyTarget.exe')
$regTarget = [IO.Path]::Combine($bin, 'RegHookTarget.exe')
$policyTest = [IO.Path]::Combine($bin, 'PolicyTest.exe')
$pathTest = [IO.Path]::Combine($bin, 'PathTest.exe')

& ([IO.Path]::Combine($root, 'build.ps1')) | Out-Null

$script:failures = 0

function Invoke-Captured([string]$exe, [string]$arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    if ($arguments) { $psi.Arguments = $arguments }
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [Text.Encoding]::UTF8

    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    return @{ Code = $process.ExitCode; Text = "$stdout$stderr" }
}

function Assert-Equal($name, $expected, $actual) {
    if ("$expected" -eq "$actual") {
        Write-Host ("  PASS  {0} (exit {1})" -f $name, $actual) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL  {0} expected {1}, got {2}" -f $name, $expected, $actual) -ForegroundColor Red
        $script:failures++
    }
}

function Assert-Match($name, $pattern, $text) {
    if ("$text" -match $pattern) {
        Write-Host ("  PASS  {0}" -f $name) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL  {0} (pattern {1} not found)" -f $name, $pattern) -ForegroundColor Red
        $script:failures++
    }
}

Write-Host "`n[A] policy logic (deterministic) + baseline on this machine" -ForegroundColor Cyan

# A1：判定逻辑单元测试，直接喂固定的地区信号，与本机区域无关
$policyResult = Invoke-Captured $policyTest
Assert-Equal 'policy logic unit tests' 0 $policyResult.Code
Assert-Match 'policy logic reports all passed' 'POLICY: ALL PASSED' $policyResult.Text

# A2：基线取决于这台机器的真实区域（CI runner 在境外，本来就不受限），
#     所以期望值从 --inspect 的结果推出来，而不是写死“必须受限”。
$inspect = Invoke-Captured $tool '--inspect'
$expectedBaseline = if ($inspect.Text -match '是否受限：是') { 1 } else { 0 }
$baseline = Invoke-Captured $target
Assert-Equal "baseline matches machine signal (expected $expectedBaseline)" $expectedBaseline $baseline.Code

Write-Host "`n[B] patched via launcher, immediate hooks" -ForegroundColor Cyan
$b = Invoke-Captured $tool ("--no-delay --exe `"{0}`" -- --delay 2500" -f $target)
Assert-Equal 'patched is unrestricted' 0 $b.Code
Assert-Match 'GeoName spoofed to US' 'GeoName\s+=\s+"US"' $b.Text
Assert-Match 'LocaleName spoofed to en-US' 'LocaleName\s+=\s+"en-US"' $b.Text
Assert-Match 'verdict is Other' 'Classification\s+=\s+Other' $b.Text

Write-Host "`n[C] patched via launcher, deferred locale hooks (window appears first)" -ForegroundColor Cyan
$c = Invoke-Captured $tool ("--exe `"{0}`" -- --form 3000" -f $target)
Assert-Equal 'deferred patch is unrestricted' 0 $c.Code
Assert-Match 'deferred patch still reports en-US locale' 'LocaleName\s+=\s+"en-US"' $c.Text
Assert-Match 'process culture untouched (zh-CN)' 'CurrentCulture\s+=\s+zh-CN' $c.Text
Assert-Match 'process UI culture untouched (zh-CN)' 'CurrentUICulture\s+=\s+zh-CN' $c.Text

Write-Host "`n[D] registry DeviceRegion hook" -ForegroundColor Cyan
$plain = Invoke-Captured $regTarget
Assert-Equal 'without hook the value is readable' 0 $plain.Code

$d = Invoke-Captured $tool ("--no-delay --registry-hooks --exe `"{0}`" -- --delay 3000" -f $regTarget)
Assert-Equal 'with hook DeviceRegion is masked' 3 $d.Code
Assert-Match 'hook hides DeviceRegion' 'DEVICE_REGION_QUERY\s+=\s+2' $d.Text
Assert-Match 'hook hides DeviceRegion (RegGetValue)' 'DEVICE_REGION_GETVALUE=\s*2' $d.Text
Assert-Match 'other values pass through' 'OTHER_VALUE\s+=\s+0' $d.Text
Assert-Match 'similar name passes through' 'PREFIX_DEVICEREGIO\s+=\s+0' $d.Text

Write-Host "`n[E] path handling (empty / invalid input must not throw)" -ForegroundColor Cyan
$e = Invoke-Captured $pathTest
Assert-Equal 'path helper handles empty and invalid input' 0 $e.Code
Assert-Match 'path helper reports all passed' 'PATHUTIL: ALL PASSED' $e.Text

Write-Host "`n[F] autostart registry + silent start" -ForegroundColor Cyan

# 这一组会写真实的 HKCU 启动项和 settings.ini：先备份，结束时原样恢复。
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'PCLRegionBypass'
$settingsPath = [IO.Path]::Combine([Environment]::GetFolderPath('ApplicationData'), 'PCLRegionBypass', 'settings.ini')
$hadSettings = Test-Path -LiteralPath $settingsPath
$settingsBackup = if ($hadSettings) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
$existingRun = Get-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue
$hadRunEntry = $null -ne $existingRun
$runBackup = if ($hadRunEntry) { $existingRun.$runValueName } else { $null }
$silentProcess = $null
$autoProcess = $null
$settingProcess = $null
$normalProcess = $null

try {
    # F1：开关开机启动 → HKCU Run 项指向本程序，并且带 --autostart / --silent
    $f1 = Invoke-Captured $tool '--set-autostart on'
    Assert-Equal 'set-autostart on exits 0' 0 $f1.Code

    $registered = (Get-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue).$runValueName
    Assert-Match 'Run entry exists and is quoted path to this exe' ('^"' + [regex]::Escape($tool) + '"') "$registered"
    Assert-Match 'Run entry passes --autostart' '--autostart' "$registered"
    Assert-Match 'Run entry passes --silent (no console window at logon)' '--silent' "$registered"

    $inspect2 = Invoke-Captured $tool '--inspect'
    Assert-Match 'inspect reports autostart on' '开机启动\s+=\s+已开启' $inspect2.Text

    # F2：静默启动开关要能持久化，并且 --inspect 读得到
    $f2 = Invoke-Captured $tool '--set-silent on'
    Assert-Equal 'set-silent on exits 0' 0 $f2.Code
    $inspect3 = Invoke-Captured $tool '--inspect'
    Assert-Match 'inspect reports silent on' '静默启动\s+=\s+已开启' $inspect3.Text

    Invoke-Captured $tool '--set-silent off' | Out-Null
    $inspect4 = Invoke-Captured $tool '--inspect'
    Assert-Match 'inspect reports silent off' '静默启动\s+=\s+未开启' $inspect4.Text

    # F3：真正的窗口行为需要交互桌面；无桌面环境（部分 CI）跳过，不算失败
    $interactive = [Environment]::UserInteractive -and ($null -ne (Get-Process -Name explorer -ErrorAction SilentlyContinue))
    if (-not $interactive) {
        Write-Host '  SKIP  GUI window checks (no interactive desktop in this session)' -ForegroundColor Yellow
    } else {
        # 静默启动「设置」开启时：双击（不带参数）也直接进托盘
        Invoke-Captured $tool '--set-silent on' | Out-Null
        $settingProcess = Start-Process -FilePath $tool -PassThru
        Start-Sleep -Seconds 2
        Assert-Equal 'silent setting keeps no-arg launch in tray' $false $settingProcess.HasExited
        $settingProcess.Refresh()
        Assert-Equal 'silent setting hides the window' 0 ([int64]$settingProcess.MainWindowHandle)
        Stop-Process -Id $settingProcess.Id -Force -ErrorAction SilentlyContinue
        $settingProcess.WaitForExit(5000) | Out-Null
        $settingProcess = $null

        # 关掉设置后：双击正常显示主界面
        Invoke-Captured $tool '--set-silent off' | Out-Null
        $normalProcess = Start-Process -FilePath $tool -PassThru
        Start-Sleep -Seconds 2
        $normalProcess.Refresh()
        Assert-Equal 'without silent setting the window shows' $true ($normalProcess.MainWindowHandle -ne 0)

        # 主界面上的两个开关：点一下必须真的写/删注册表、写设置。
        # 用 BM_CLICK 直接点复选框（不依赖 UI Automation：PowerShell 5.1 里 UIA 把 WinForms
        # 控件都报成 Pane，拿不到 CheckBox 类型和 TogglePattern）。
        try {
            Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class UiClick {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    private static IntPtr Find(IntPtr parent, string prefix) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr hwnd, IntPtr param) {
            var text = new StringBuilder(512);
            GetWindowTextW(hwnd, text, 512);
            if (text.ToString().StartsWith(prefix, StringComparison.Ordinal)) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static bool Click(IntPtr parent, string prefix) {
        IntPtr target = Find(parent, prefix);
        if (target == IntPtr.Zero) return false;
        SendMessage(target, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
        return true;
    }
}
"@ -ErrorAction Stop

            $mainWindow = [IntPtr]$normalProcess.MainWindowHandle

            # 进到这里时开机启动是 F1 打开的：开关启动时就该是「勾上」，
            # 所以第一次点击必然是「关闭」——正好顺带验证了启动时的状态同步。
            $autoClicked = [UiClick]::Click($mainWindow, '开机启动')
            if (-not $autoClicked) {
                Write-Host '  SKIP  UI switch checks (autostart checkbox not found in the window)' -ForegroundColor Yellow
            } else {
                # 点掉开机启动 → Run 项应被删除
                Start-Sleep -Milliseconds 700
                $removedByUi = (Get-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue).$runValueName
                Assert-Equal 'main window switch disables autostart' $true ($null -eq $removedByUi)

                # 再点回来 → Run 项应重新写入，命令里带 --autostart --silent
                [UiClick]::Click($mainWindow, '开机启动') | Out-Null
                Start-Sleep -Milliseconds 700
                $registeredByUi = (Get-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue).$runValueName
                Assert-Equal 'main window switch enables autostart' $true ($null -ne $registeredByUi)
                Assert-Match 'main window switch writes the silent command' '--autostart --silent' "$registeredByUi"

                # 静默启动开关写 settings.ini
                [UiClick]::Click($mainWindow, '静默启动') | Out-Null
                Start-Sleep -Milliseconds 700
                Assert-Match 'main window switch enables silent start' 'SilentStart=1' ([IO.File]::ReadAllText($settingsPath))

                [UiClick]::Click($mainWindow, '静默启动') | Out-Null
                Start-Sleep -Milliseconds 700
                Assert-Match 'main window switch disables silent start' 'SilentStart=0' ([IO.File]::ReadAllText($settingsPath))
            }
        } catch {
            Write-Host ('  SKIP  UI switch checks ({0})' -f $_.Exception.Message) -ForegroundColor Yellow
        }

        Stop-Process -Id $normalProcess.Id -Force -ErrorAction SilentlyContinue
        $normalProcess.WaitForExit(5000) | Out-Null
        $normalProcess = $null

        # --silent：进程留在托盘，但不出现主窗口
        $silentProcess = Start-Process -FilePath $tool -ArgumentList '--silent' -PassThru
        Start-Sleep -Seconds 2
        Assert-Equal 'silent start keeps running in tray' $false $silentProcess.HasExited
        $silentProcess.Refresh()
        Assert-Equal 'silent start shows no window' 0 ([int64]$silentProcess.MainWindowHandle)

        # 再双击一次：不产生第二个进程，而是把已有窗口唤出来
        $second = Start-Process -FilePath $tool -ArgumentList '--silent' -PassThru
        Start-Sleep -Seconds 2
        Assert-Equal 'second launch exits (single instance)' $true $second.HasExited
        $silentProcess.Refresh()
        Assert-Equal 'second launch brings the window back' $true ($silentProcess.MainWindowHandle -ne 0)

        Stop-Process -Id $silentProcess.Id -Force -ErrorAction SilentlyContinue
        $silentProcess.WaitForExit(5000) | Out-Null
        $silentProcess = $null

        # --autostart 单独传入时也必须是「静默 GUI」：旧版本会掉进控制台分支并直接退出 1
        $autoProcess = Start-Process -FilePath $tool -ArgumentList '--autostart' -PassThru
        Start-Sleep -Seconds 2
        Assert-Equal 'autostart flag stays in tray' $false $autoProcess.HasExited
        $autoProcess.Refresh()
        Assert-Equal 'autostart flag shows no window' 0 ([int64]$autoProcess.MainWindowHandle)

        Stop-Process -Id $autoProcess.Id -Force -ErrorAction SilentlyContinue
        $autoProcess.WaitForExit(5000) | Out-Null
        $autoProcess = $null
    }

    # F4：关掉开机启动 → 启动项被删除
    $f4 = Invoke-Captured $tool '--set-autostart off'
    Assert-Equal 'set-autostart off exits 0' 0 $f4.Code
    $afterOff = Get-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue
    Assert-Equal 'Run entry removed' $true ($null -eq $afterOff)

    $inspect5 = Invoke-Captured $tool '--inspect'
    Assert-Match 'inspect reports autostart off' '开机启动\s+=\s+未开启' $inspect5.Text
}
finally {
    foreach ($process in @($silentProcess, $autoProcess, $settingProcess, $normalProcess)) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }

    if ($hadRunEntry) {
        Set-ItemProperty -Path $runKey -Name $runValueName -Value $runBackup
    } else {
        Remove-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue
    }

    if ($hadSettings) {
        [IO.File]::WriteAllBytes($settingsPath, $settingsBackup)
    } else {
        Remove-Item -LiteralPath $settingsPath -ErrorAction SilentlyContinue
    }
}

Write-Host ""
if ($script:failures -eq 0) {
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
    exit 0
}
Write-Host ("{0} TEST(S) FAILED" -f $script:failures) -ForegroundColor Red
exit 1
