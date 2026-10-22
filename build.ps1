# Build with the .NET Framework compiler that ships with Windows (no SDK required).
# Kept ASCII-only on purpose: Windows PowerShell 5.1 reads .ps1 as ANSI without a BOM.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

$outDir = Join-Path $root 'bin'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$out = Join-Path $outDir 'PCLRegionBypass.exe'

$argsTool = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/debug-',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll'
)
$iconPath = Join-Path $root 'assets\app.ico'
if (Test-Path $iconPath) { $argsTool += ('/win32icon:' + $iconPath) }
$argsTool += ('/out:' + $out)
$argsTool += $sources

& $csc $argsTool
if ($LASTEXITCODE -ne 0) { throw "tool build failed (exit $LASTEXITCODE)" }
Write-Host ("built: " + $out)

# Test target: PCL-CE's region policy running in its own process.
$testOut = Join-Path $outDir 'PolicyTarget.exe'
$testSources = @(
    (Join-Path $root 'tests\PolicyTarget.cs'),
    (Join-Path $root 'src\PolicyPort.cs')
)
$argsTarget = @(
    '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/debug-',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    ('/out:' + $testOut)
) + $testSources

& $csc $argsTarget
if ($LASTEXITCODE -ne 0) { throw "test target build failed (exit $LASTEXITCODE)" }
Write-Host ("built: " + $testOut)

# Registry hook test target.
$regOut = Join-Path $outDir 'RegHookTarget.exe'
$argsReg = @(
    '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/debug-',
    '/r:System.dll', '/r:System.Core.dll',
    ('/out:' + $regOut),
    (Join-Path $root 'tests\RegHookTarget.cs')
)

& $csc $argsReg
if ($LASTEXITCODE -ne 0) { throw "registry test target build failed (exit $LASTEXITCODE)" }
Write-Host ("built: " + $regOut)

# Path handling regression test (empty / invalid path inputs must never throw).
$pathOut = Join-Path $outDir 'PathTest.exe'
$argsPath = @(
    '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/debug-',
    '/r:System.dll', '/r:System.Core.dll',
    ('/out:' + $pathOut),
    (Join-Path $root 'tests\PathUtilTest.cs'),
    (Join-Path $root 'src\PathUtil.cs')
)

& $csc $argsPath
if ($LASTEXITCODE -ne 0) { throw "path test build failed (exit $LASTEXITCODE)" }
Write-Host ("built: " + $pathOut)

# Deterministic policy unit test (feeds fixed RegionSignals, no OS signals involved).
$policyOut = Join-Path $outDir 'PolicyTest.exe'
$argsPolicy = @(
    '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/debug-',
    '/r:System.dll', '/r:System.Core.dll',
    ('/out:' + $policyOut),
    (Join-Path $root 'tests\PolicyLogicTest.cs'),
    (Join-Path $root 'src\PolicyPort.cs')
)

& $csc $argsPolicy
if ($LASTEXITCODE -ne 0) { throw "policy logic test build failed (exit $LASTEXITCODE)" }
Write-Host ("built: " + $policyOut)