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

Write-Host ""
if ($script:failures -eq 0) {
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
    exit 0
}
Write-Host ("{0} TEST(S) FAILED" -f $script:failures) -ForegroundColor Red
exit 1
