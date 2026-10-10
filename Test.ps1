$ErrorActionPreference='Stop'
foreach($project in @('checks/Checks.csproj','security/Checks.csproj')){
 & dotnet run --project (Join-Path $PSScriptRoot $project) -c Release
 if($LASTEXITCODE){throw "Checks failed: $project"}
}
