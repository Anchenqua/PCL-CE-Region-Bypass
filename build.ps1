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
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    ('/out:' + $out)
) + $sources

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
