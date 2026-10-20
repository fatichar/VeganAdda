param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$monitors = Get-Content (Join-Path $PSScriptRoot '../deploy/betterstack-monitors.json') -Raw | ConvertFrom-Json
if (!$Apply) {
    $monitors | ConvertTo-Json -Depth 10
    Write-Host 'Preview only. Set BETTERSTACK_UPTIME_API_TOKEN to a team-scoped API token and run with -Apply to create missing monitors.'
    exit 0
}
if (!$env:BETTERSTACK_UPTIME_API_TOKEN) { throw 'Set BETTERSTACK_UPTIME_API_TOKEN in this session. Never commit it.' }
$headers = @{ Authorization = "Bearer $env:BETTERSTACK_UPTIME_API_TOKEN" }
$api = 'https://incidents.betterstack.com/api/v2/monitors'
$existing = @()
$next = $api
while ($next) {
    $uri = [Uri]$next
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'incidents.betterstack.com' -or $uri.Port -ne 443) {
        throw 'Refusing to send the API token to an unexpected pagination origin.'
    }
    $page = Invoke-RestMethod -Uri $next -Headers $headers -TimeoutSec 30
    $existing += @($page.data)
    $next = $page.pagination.next
}
foreach ($monitor in $monitors) {
    if ($existing | Where-Object { $_.attributes.url -eq $monitor.url }) {
        Write-Host "Already monitored: $($monitor.url). Existing settings preserved; compare with the manifest."
        continue
    }
    # Do not print provider error bodies or tokens to logs.
    try {
        $created = Invoke-RestMethod -Method Post -Uri $api -Headers $headers -ContentType 'application/json' -Body ($monitor | ConvertTo-Json -Depth 10) -TimeoutSec 30
    } catch { throw "Better Stack monitor creation failed for $($monitor.pronounceable_name). Check account access and plan limits in the dashboard." }
    Write-Host "Created $($monitor.pronounceable_name), ID $($created.data.id). Verify notification delivery in Better Stack."
}
