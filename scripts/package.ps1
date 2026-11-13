param([string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric semantic version.' }
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'NLA_CHECKER/bin/Release'
$exe = Join-Path $build 'UK_NLA_CHECKER.exe'
if (!(Test-Path $exe)) { throw 'Run scripts/build.ps1 first.' }
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne "$Version.0") { throw 'Executable version does not match the package version.' }
$output = Join-Path $root 'artifacts'
$stage = Join-Path $output ('package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item -LiteralPath $exe,(Join-Path $build 'UK_NLA_CHECKER.exe.config') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'README.md'),(Join-Path $root 'README.fa.md'),(Join-Path $root 'CHANGELOG.md') -Destination $stage
New-Item -ItemType Directory -Force (Join-Path $stage 'docs/images') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs/VALIDATION.md') -Destination (Join-Path $stage 'docs')
Copy-Item -LiteralPath (Join-Path $root 'docs/images/01-overview.png') -Destination (Join-Path $stage 'docs/images')
$zip = Join-Path $output "NLA-Checker-v$Version-windows.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output $zip
Write-Output "SHA256: $hash"
