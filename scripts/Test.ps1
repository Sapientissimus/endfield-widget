param([switch]$Desktop)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $root 'source\build.ps1')
$exe=Join-Path $root 'EndfieldWidget.exe'
$output=Join-Path $root '.test-output'
$checks=Join-Path $output ('run-'+[Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $checks -Force)
foreach($file in Get-ChildItem -LiteralPath (Join-Path $root 'tests\fixtures') -File){Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $checks $file.Name)}
Copy-Item -LiteralPath (Join-Path $root 'tests\fixtures\localization') -Destination $checks -Recurse
$bannerFixtures=Join-Path $output 'research\banners'
[void](New-Item -ItemType Directory -Path $bannerFixtures -Force)
Copy-Item -LiteralPath (Join-Path $root 'tests\research\banners\4489.json') -Destination (Join-Path $bannerFixtures '4489.json') -Force

function Invoke-WidgetCheck([string]$name,[string[]]$arguments,[int]$timeout){
  $report=Join-Path $checks ($name+'.txt')
  $quoted=@()
  foreach($value in $arguments){$quoted+=('"'+$value+'"')}
  $process=Start-Process -FilePath $exe -ArgumentList $quoted -WindowStyle Hidden -PassThru
  if(-not $process.WaitForExit($timeout)){Stop-Process -Id $process.Id;throw ($name+' timed out; isolated process stopped.')}
  if(-not(Test-Path -LiteralPath $report)){throw ($name+' did not produce a report: '+$report)}
  $lines=Get-Content -LiteralPath $report
  if($process.ExitCode -ne 0){$lines|Select-Object -Last 12|Write-Output;throw ($name+' failed: '+$report)}
  $passed=@($lines|Where-Object {$_ -like 'PASS:*'}).Count
  Write-Output ($name+': '+$passed+' checks passed.')
}

Invoke-WidgetCheck 'CORE-CHECKS' @('--self-test',(Join-Path $checks 'CORE-CHECKS.txt')) 30000
Invoke-WidgetCheck 'FEATURE-CHECKS' @('--feature-test',(Join-Path $checks 'FEATURE-CHECKS.txt'),(Join-Path $checks 'feature-profile')) 120000
Invoke-WidgetCheck 'LOCALIZATION-CHECKS' @('--localization-test',(Join-Path $checks 'LOCALIZATION-CHECKS.txt'),(Join-Path $checks 'localization-profile')) 180000
Invoke-WidgetCheck 'RELEASE-CHECKS' @('--release-test',(Join-Path $checks 'RELEASE-CHECKS.txt'),(Join-Path $checks 'release-profile')) 60000
if($Desktop){Invoke-WidgetCheck 'DESKTOP-CHECKS' @('--desktop-test',(Join-Path $checks 'DESKTOP-CHECKS.txt'),(Join-Path $checks 'desktop-profile')) 60000}
Write-Output ('Reports: '+$checks)
