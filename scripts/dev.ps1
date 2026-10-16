param([switch]$Fixtures)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Development__Enabled = $Fixtures.IsPresent.ToString().ToLowerInvariant()
$env:Development__Seed = $Fixtures.IsPresent.ToString().ToLowerInvariant()
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet restore VeganAdda.sln
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet build VeganAdda.sln --no-restore -m:1 /nr:false
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet run --project src/VeganAdda.Web/VeganAdda.Web --no-build --no-launch-profile --urls http://localhost:5080
