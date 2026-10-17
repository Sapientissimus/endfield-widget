param([string]$SourceRoot=(Split-Path -Parent $PSScriptRoot),[string]$OutputDirectory,[string[]]$Cases)
$ErrorActionPreference='Stop'
$repository=Split-Path -Parent $PSScriptRoot
$SourceRoot=[IO.Path]::GetFullPath($SourceRoot)
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repository ('.test-output\audit-'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$probe=Join-Path $OutputDirectory 'EndfieldWidget.exe'
$options=@('/nologo','/target:exe','/main:EndfieldWidget.AuditHarness','/platform:anycpu32bitpreferred','/optimize+','/codepage:65001',('/out:'+$probe),('/win32manifest:'+(Join-Path $SourceRoot 'source\app.manifest')))
foreach($reference in @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')){$options+='/reference:'+(Join-Path $framework $reference)}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'source') -Filter '*.xaml'){$options+='/resource:'+$file.FullName+','+$file.Name}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'locales') -Filter '*.json'){$options+='/resource:'+$file.FullName+',Locale.'+$file.Name}
foreach($file in Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'assets') -Filter '*-portrait.png'){$options+='/resource:'+$file.FullName+',Art.'+$file.BaseName}
$options+=@(Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'source') -Filter '*.cs'|ForEach-Object FullName)
$options+=@(Get-ChildItem -LiteralPath (Join-Path $repository 'tests') -Filter 'Audit*.cs'|ForEach-Object FullName)
& (Join-Path $framework 'csc.exe') @options
if($LASTEXITCODE -ne 0){throw 'Audit probe compilation failed'}
$failed=0;$passed=0
if(-not $Cases){$Cases=@('settings-backup','startup-role','startup-zero','startup-time','startup-banner','startup-schedule','ui','network','images','invariants','next-timer','next-account','next-language','next-details','next-wire','next-years')}
foreach($case in $Cases){
  $report=Join-Path $OutputDirectory ($case+'.txt')
  $arguments=@($report,$case,(Join-Path $repository 'tests\fixtures'))|ForEach-Object {'"'+$_+'"'}
  $process=Start-Process -FilePath $probe -ArgumentList $arguments -WindowStyle Hidden -PassThru
  if(-not $process.WaitForExit(45000)){Stop-Process -Id $process.Id;throw ('Audit case timed out: '+$case)}
  if(-not(Test-Path -LiteralPath $report)){throw ('Audit report missing: '+$case)}
  $lines=Get-Content -LiteralPath $report
  $failed+=@($lines|Where-Object {$_ -like 'FAIL:*'}).Count;$passed+=@($lines|Where-Object {$_ -like 'PASS:*'}).Count
  $lines|Where-Object {$_ -like 'FAIL:*' -or $_ -like 'METRIC:*'}|Write-Output
}
Write-Output ('Audit checks: '+$passed+' passed; '+$failed+' failed. Reports: '+$OutputDirectory)
if($failed -gt 0){exit 1}
