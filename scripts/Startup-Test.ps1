param([string]$SourceRoot=(Split-Path -Parent $PSScriptRoot),[string]$OutputDirectory,[switch]$LegacyOnly)
$ErrorActionPreference='Stop'
$repository=Split-Path -Parent $PSScriptRoot
$SourceRoot=[IO.Path]::GetFullPath($SourceRoot)
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repository ('.test-output\startup-'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$failed=0
$cases=if($LegacyOnly){@('StartupLegacyProbe')}else{@('StartupLegacyProbe','StartupHarness')}
foreach($case in $cases){
  $directory=Join-Path $OutputDirectory ($case+' with spaces')
  [void](New-Item -ItemType Directory -Path $directory -Force)
  $probe=Join-Path $directory 'EndfieldWidget.exe'
  $options=@('/nologo','/target:exe',('/main:EndfieldWidget.'+$case),'/platform:anycpu32bitpreferred','/optimize+','/codepage:65001',('/out:'+$probe),('/win32manifest:'+(Join-Path $SourceRoot 'source\app.manifest')))
  foreach($reference in @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')){$options+='/reference:'+(Join-Path $framework $reference)}
  foreach($file in Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'source') -Filter '*.xaml'){$options+='/resource:'+$file.FullName+','+$file.Name}
  foreach($file in Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'locales') -Filter '*.json'){$options+='/resource:'+$file.FullName+',Locale.'+$file.Name}
  foreach($file in Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'assets') -Filter '*-portrait.png'){$options+='/resource:'+$file.FullName+',Art.'+$file.BaseName}
  $options+=@(Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'source') -Filter '*.cs'|ForEach-Object FullName)
  $options+=Join-Path $repository ('tests\'+$case+'.cs')
  & (Join-Path $framework 'csc.exe') @options
  if($LASTEXITCODE -ne 0){throw 'Startup test compilation failed'}
  $report=Join-Path $directory 'CHECKS.txt'
  $process=Start-Process -FilePath $probe -ArgumentList ('"'+$report+'"') -WorkingDirectory (Join-Path $env:WINDIR 'System32') -WindowStyle Hidden -PassThru
  if(-not $process.WaitForExit(45000)){throw ('Startup test timed out: '+$case)}
  if(-not(Test-Path -LiteralPath $report)){throw ('No startup test report: '+$case)}
  $lines=Get-Content -LiteralPath $report
  $failures=@($lines|Where-Object {$_ -like 'FAIL:*'});$failed+=$failures.Count
  Write-Output ($case+': '+@($lines|Where-Object {$_ -like 'PASS:*'}).Count+' passed, '+$failures.Count+' failed.')
  $failures|Write-Output
  if($process.ExitCode -ne 0 -and $failures.Count -eq 0){throw ('Startup probe failed without diagnostics: '+$case)}
}
Write-Output ('Startup reports: '+$OutputDirectory)
if($failed){exit 1}
