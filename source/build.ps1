$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
$destination = Split-Path -Parent $sourceDirectory
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkDirectory 'csc.exe'
$references = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Security.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll')
$compilerArguments = @('/nologo','/target:winexe','/platform:anycpu32bitpreferred','/optimize+','/codepage:65001',('/out:' + (Join-Path $destination 'EndfieldWidget.exe')),('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')))
foreach ($view in Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.xaml') { $compilerArguments += '/resource:' + $view.FullName + ',' + $view.Name }
$localeDirectory = Join-Path $destination 'locales'
foreach ($locale in Get-ChildItem -LiteralPath $localeDirectory -Filter '*.json') { $compilerArguments += '/resource:' + $locale.FullName + ',Locale.' + $locale.Name }
foreach ($reference in $references) { $compilerArguments += '/reference:' + (Join-Path $frameworkDirectory $reference) }
foreach ($notice in @('LICENSE','THIRD-PARTY-NOTICES.md')) { $compilerArguments += '/resource:' + (Join-Path $destination $notice) + ',Notices.' + $notice }
$assetsDirectory = Join-Path $destination 'assets'
foreach ($asset in Get-ChildItem -LiteralPath $assetsDirectory -Filter '*-portrait.png') { $compilerArguments += '/resource:' + $asset.FullName + ',Art.' + $asset.BaseName }
foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs') { $compilerArguments += $sourceFile.FullName }
& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Widget build failed.' }
Write-Output (Join-Path $destination 'EndfieldWidget.exe')
