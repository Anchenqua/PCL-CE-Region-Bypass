# End-to-end tests for the region-limit bypass. ASCII-only output.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tool = Join-Path $root 'bin\PCLRegionBypass.exe'
$target = Join-Path $root 'bin\PolicyTarget.exe'
$regTarget = Join-Path $root 'bin\RegHookTarget.exe'

& (Join-Path $root 'build.ps1') | Out-Null

$script:failures = 0
function Assert-Equal($name, $expected, $actual) {
    if ("$expected" -eq "$actual") {
        Write-Host ("  PASS  {0} (exit {1})" -f $name, $actual) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL  {0} expected {1}, got {2}" -f $name, $expected, $actual) -ForegroundColor Red
        $script:failures++
    }
}
function Assert-Match($name, $pattern, $text) {
    if ($text -match $pattern) {
        Write-Host ("  PASS  {0}" -f $name) -ForegroundColor Green
    } else {
        Write-Host ("  FAIL  {0} (pattern {1} not found)" -f $name, $pattern) -ForegroundColor Red
        $script:failures++
    }
}

Write-Host "`n[A] baseline: policy target without patches (expect RESTRICTED)" -ForegroundColor Cyan
& $target 2>&1 | Out-Null
Assert-Equal 'baseline is restricted' 1 $LASTEXITCODE

Write-Host "`n[B] patched via launcher, immediate hooks" -ForegroundColor Cyan
$outB = (& $tool --no-delay --exe $target -- --delay 2500 2>&1 | Out-String)
Assert-Equal 'patched is unrestricted' 0 $LASTEXITCODE
Assert-Match 'GeoName spoofed to US' 'GeoName\s+=\s+"US"' $outB
Assert-Match 'LocaleName spoofed to en-US' 'LocaleName\s+=\s+"en-US"' $outB
Assert-Match 'verdict is Other' 'Classification\s+=\s+Other' $outB

Write-Host "`n[C] patched via launcher, deferred locale hooks (window appears first)" -ForegroundColor Cyan
$outC = (& $tool --exe $target -- --form 3000 2>&1 | Out-String)
Assert-Equal 'deferred patch is unrestricted' 0 $LASTEXITCODE
Assert-Match 'deferred patch still reports en-US locale' 'LocaleName\s+=\s+"en-US"' $outC
Assert-Match 'process culture untouched (zh-CN)' 'CurrentCulture\s+=\s+zh-CN' $outC
Assert-Match 'process UI culture untouched (zh-CN)' 'CurrentUICulture\s+=\s+zh-CN' $outC

Write-Host "`n[D] registry DeviceRegion hook" -ForegroundColor Cyan
& $regTarget 2>&1 | Out-Null
Assert-Equal 'without hook the value is readable' 0 $LASTEXITCODE

$outD = (& $tool --no-delay --registry-hooks --exe $regTarget -- --delay 3000 2>&1 | Out-String)
Assert-Equal 'with hook DeviceRegion is masked' 3 $LASTEXITCODE
Assert-Match 'hook hides DeviceRegion' 'DEVICE_REGION_QUERY\s+=\s+2' $outD
Assert-Match 'hook hides DeviceRegion (RegGetValue)' 'DEVICE_REGION_GETVALUE=\s*2' $outD
Assert-Match 'other values pass through' 'OTHER_VALUE\s+=\s+0' $outD
Assert-Match 'similar name passes through' 'PREFIX_DEVICEREGIO\s+=\s+0' $outD

Write-Host ""
if ($script:failures -eq 0) {
    Write-Host "ALL TESTS PASSED" -ForegroundColor Green
    exit 0
}
Write-Host ("{0} TEST(S) FAILED" -f $script:failures) -ForegroundColor Red
exit 1
