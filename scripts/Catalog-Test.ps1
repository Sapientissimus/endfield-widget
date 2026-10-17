$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
function Read-Catalog([string]$path){
  $text=Get-Content -LiteralPath $path -Raw -Encoding UTF8
  if($PSVersionTable.PSVersion.Major -ge 6){return ($text|ConvertFrom-Json -AsHashtable)}
  Add-Type -AssemblyName System.Web.Extensions
  $decoder=New-Object System.Web.Script.Serialization.JavaScriptSerializer
  $decoder.MaxJsonLength=2000000
  return $decoder.DeserializeObject($text)
}
$base=Read-Catalog (Join-Path $root 'locales\en.json')
$keys=@($base.Keys)
$resources=@{}
$sha=[Security.Cryptography.SHA256]::Create()
try{foreach($key in $keys){$resource='Locale.'+[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key))).Replace('-','').Substring(0,24).ToLowerInvariant();$resources[$resource]=$true}}finally{$sha.Dispose()}
$checks=0
foreach($file in Get-ChildItem -LiteralPath (Join-Path $root 'locales') -Filter '*.json'){
  $catalog=Read-Catalog $file.FullName
  if(@(Compare-Object $keys @($catalog.Keys) -CaseSensitive).Count){throw ('Catalog keys differ: '+$file.Name)}
  if(@($catalog.Values|Where-Object {[string]::IsNullOrEmpty($_)}).Count){throw ('Empty translation: '+$file.Name)}
  $checks++
}
foreach($file in @(Get-ChildItem -LiteralPath (Join-Path $root 'source') -Filter '*.cs')+@(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs')){
  $text=Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
  foreach($match in [regex]::Matches($text,'\bL\.(?:T|Format|ResourceKey)\(\s*("(?:\\.|[^"\\])*")')){
    $key=$match.Groups[1].Value|ConvertFrom-Json
    if($keys -cnotcontains $key){throw ('Unknown message key in '+$file.Name+': '+$key)}
    $checks++
  }
}
foreach($file in @(Get-ChildItem -LiteralPath (Join-Path $root 'source') -Filter '*.cs')+@(Get-ChildItem -LiteralPath (Join-Path $root 'source') -Filter '*.xaml')){
  $text=Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
  foreach($match in [regex]::Matches($text,'Locale\.[0-9a-f]{24}')){if(-not $resources.ContainsKey($match.Value)){throw ('Unknown view message resource in '+$file.Name)};$checks++}
}
Write-Output ('Catalog checks passed: '+$checks+'. All language catalogs, literal message keys and view resources agree.')
