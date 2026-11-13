param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'Install Visual Studio Build Tools with the .NET desktop build tools workload and .NET Framework 4.8 targeting pack.' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'MSBuild was not found.' }
& $msbuild (Join-Path $root 'NLA_CHECKER.sln') /p:Configuration=Release '/p:Platform=Any CPU' /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
if (!$SkipTests) {
    $csc = Join-Path (Split-Path $msbuild) 'Roslyn/csc.exe'
    $output = Join-Path $root 'artifacts/tests'
    New-Item -ItemType Directory -Force $output | Out-Null
    & $csc /nologo /optimize+ /langversion:7.3 "/out:$output/Tests.exe" /r:System.dll /r:System.Core.dll (Join-Path $root 'NLA_CHECKER/ScanTarget.cs') (Join-Path $root 'NLA_CHECKER/RdpProbe.cs') (Join-Path $root 'NLA_CHECKER/ScanSession.cs') (Join-Path $root 'tests/Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & "$output/Tests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
