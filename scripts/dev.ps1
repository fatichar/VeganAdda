param([switch]$Fixtures, [switch]$Aspire)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Development__Enabled = $Fixtures.IsPresent.ToString().ToLowerInvariant()
$env:Development__Seed = $Fixtures.IsPresent.ToString().ToLowerInvariant()
$env:Monitoring__Aspire__Endpoint = if ($Aspire) { 'http://localhost:4317' } else { '' }
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet restore VeganAdda.sln
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet build VeganAdda.sln --no-restore -m:1 /nr:false
if ($LASTEXITCODE) { exit $LASTEXITCODE }
if ($Aspire) {
    docker compose -f compose.dev.yaml --profile observability up -d aspire-dashboard
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Write-Host 'Aspire Dashboard: http://localhost:18888'
}
dotnet run --project src/VeganAdda.Web/VeganAdda.Web --no-build --no-launch-profile --urls http://localhost:5080
