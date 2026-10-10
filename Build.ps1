[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameDir,[switch]$Rebuild)
$ErrorActionPreference='Stop'
$game=[IO.Path]::GetFullPath($GameDir)
$project=Join-Path $PSScriptRoot 'src/RealTimeShop/RealTimeShop.csproj'
& dotnet restore $project "-p:GameDir=$game"
if($LASTEXITCODE){throw 'Restore failed'}
$target=if($Rebuild){'Rebuild'}else{'Build'}
& dotnet build $project -c Release --no-restore "-p:GameDir=$game" -p:UseSharedCompilation=false "-t:$target"
if($LASTEXITCODE){throw 'Build failed'}
Get-FileHash (Join-Path $PSScriptRoot 'src/RealTimeShop/bin/Release/net6.0/ProbablyStolen.RealTimeShop.dll') -Algorithm SHA256
