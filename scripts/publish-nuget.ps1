# -----------------------------------------------------------------------------
# Caveman — publish NuGet packages
# Copyright (c) 2026 Passaro Francesco Paolo - Digitalsolutions.it
#
# Packs and optionally pushes the five packages shipped this cycle, in dependency
# order (core libraries first, then the packages that reference them on NuGet):
#   1. Caveman                       1.4.4  (caveman.core.csproj)
#   2. Caveman.SemanticKernel        1.4.4  (sibling repo)
#   3. Caveman.PrivacyGuard          1.3.0  (sibling repo)
#   4. Caveman.Mcp                   1.4.4  (mcp/caveman.mcp.csproj)
#   5. Caveman.PrivacyGuard.Mcp      1.3.0  (sibling repo)
#
# When -Push is given, each package is first checked against the feed (flat-container
# API) and skipped — pack included — if that exact id+version already exists, so
# re-running the script after a partial publish just moves on. Without -Push it's a
# pack-only rehearsal (no existence check, no push).
#
# The MCP servers restore the core packages from the configured feed, so publish the
# two libraries first (or run this script in dependency order, which it already does).
#
# Usage:
#   .\scripts\publish-nuget.ps1                         # pack only, into .\artifacts
#   .\scripts\publish-nuget.ps1 -Push -ApiKey <key>     # pack + push to nuget.org
#   .\scripts\publish-nuget.ps1 -Push -ApiKey <key> -Source https://api.nuget.org/v3/index.json
#
# The -Push flag requires -ApiKey (or $env:NUGET_API_KEY). Without it the script
# only packs, so a publish run can be rehearsed before anything leaves the machine.
# -----------------------------------------------------------------------------
param(
    [switch]$Push,
    [string]$ApiKey = $env:NUGET_API_KEY,
    [string]$Source = "https://api.nuget.org/v3/index.json"
)

$ErrorActionPreference = "Stop"
$repoRoot  = Split-Path -Parent $PSScriptRoot
$sibling   = Join-Path (Split-Path -Parent $repoRoot) "Caveman.PrivacyGuard"
$outDir    = Join-Path $repoRoot "artifacts"
$config    = "Release"

$targets = @(
    @{ Name = "Caveman";                      Csproj = Join-Path $repoRoot "caveman.core.csproj";                       Version = "1.4.4" },
    @{ Name = "Caveman.SemanticKernel";       Csproj = Join-Path (Join-Path (Split-Path -Parent $repoRoot) "Caveman.SemanticKernel") "Caveman.SemanticKernel.csproj"; Version = "1.4.4" },
    @{ Name = "Caveman.PrivacyGuard";         Csproj = Join-Path $sibling "Caveman.PrivacyGuard\Caveman.PrivacyGuard.csproj"; Version = "1.3.0" },
    @{ Name = "Caveman.Mcp";                  Csproj = Join-Path $repoRoot "mcp\caveman.mcp.csproj";                    Version = "1.4.4" },
    @{ Name = "Caveman.PrivacyGuard.Mcp";     Csproj = Join-Path $sibling "Caveman.PrivacyGuard.Mcp\Caveman.PrivacyGuard.Mcp.csproj"; Version = "1.3.0" }
)

if ($Push -and [string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "-Push requires -ApiKey (or the NUGET_API_KEY environment variable)."
}

if (-not (Test-Path $sibling)) {
    throw "Sibling PrivacyGuard repo not found at '$sibling'. Expected both repos under the same parent folder."
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Write-Host "Publishing to output: $outDir" -ForegroundColor Cyan

# Resolves the flat-container nupkg URL for a source + id + version. Prefers the
# PackageBaseAddress/3.0.0 resource advertised in the source's service index
# (e.g. https://api.nuget.org/v3-flatcontainer/... for nuget.org); falls back to
# guessing "<base>/flatcontainer/..." when the index cannot be read.
function Resolve-FlatContainerUrl([string]$source, [string]$id, [string]$version) {
    $lower = $id.ToLowerInvariant()
    $v = $version.ToLowerInvariant()
    try {
        $doc = Invoke-RestMethod -Uri $source -TimeoutSec 15 -ErrorAction Stop
        $res = @($doc.resources | Where-Object { $_.'@type' -like 'PackageBaseAddress*' } | Select-Object -First 1)
        if ($res -and $res[0].'@id') {
            return ("{0}/{1}/{2}/{1}.{2}.nupkg" -f $res[0].'@id'.TrimEnd('/'), $lower, $v)
        }
    }
    catch {
        Write-Warning "Cannot read service index at '$source' ($($_.Exception.Message)); guessing the flat-container URL."
    }
    $base = $source.TrimEnd('/') -replace 'index\.json$', ''
    return "$base/flatcontainer/$lower/$v/$lower.$v.nupkg"
}

# Returns $true when the exact id+version already exists on the source feed.
# Uses a raw HttpWebRequest with a 1-byte Range so no full package is downloaded
# and no interactive credential prompt can block CI. A 200/206 means present,
# a 404/410 means absent; anything else is treated as "unknown" (returns $false)
# so the pack/push is still attempted rather than silently skipped.
function Test-PackageOnSource([string]$source, [string]$id, [string]$version) {
    $url = Resolve-FlatContainerUrl $source $id $version
    try {
        $req = [System.Net.HttpWebRequest]::Create($url)
        $req.Method = "GET"
        $req.AddRange(0, 0)
        $req.Timeout = 30000
        $resp = $req.GetResponse()
        try {
            $status = [int]$resp.StatusCode
        }
        finally {
            $resp.Close()
        }
        if ($status -eq 200 -or $status -eq 206) { return $true }
        if ($status -eq 404 -or $status -eq 410) { return $false }
        Write-Warning "Unexpected status $status for '$id $version' on $source; proceeding."
    }
    catch [System.Net.WebException] {
        $status = $null
        if ($_.Exception.Response) {
            try { $status = [int]$_.Exception.Response.StatusCode } finally { $_.Exception.Response.Close() }
        }
        if ($status -eq 404 -or $status -eq 410) { return $false }
        Write-Warning "Cannot check '$id $version' on $source ($($_.Exception.Message)); proceeding."
    }
    catch {
        Write-Warning "Cannot check '$id $version' on $source ($($_.Exception.Message)); proceeding."
    }
    return $false
}

foreach ($t in $targets) {
    if (-not (Test-Path $t.Csproj)) {
        throw "Project not found: $($t.Csproj)"
    }

    if ($Push -and (Test-PackageOnSource $Source $t.Name $t.Version)) {
        Write-Host "`n=== Skipping $($t.Name) $($t.Version) ===" -ForegroundColor Yellow
        Write-Host "Already on $Source" -ForegroundColor Yellow
        continue
    }

    Write-Host "`n=== Packing $($t.Name) $($t.Version) ===" -ForegroundColor Cyan
    dotnet pack $t.Csproj -c $config --nologo -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for $($t.Name)." }

    $nupkg = Get-ChildItem $outDir -Filter "$($t.Name).$($t.Version).nupkg" | Select-Object -First 1
    if (-not $nupkg) {
        throw "Expected nupkg not produced: $($t.Name).$($t.Version).nupkg (csproj Version mismatch?)."
    }

    Write-Host "Packed: $($nupkg.FullName)" -ForegroundColor Green

    if ($Push) {
        Write-Host "Pushing $($nupkg.Name) to $Source ..."
        dotnet nuget push $nupkg.FullName --api-key $ApiKey --source $Source
        if ($LASTEXITCODE -ne 0) { throw "dotnet nuget push failed for $($t.Name)." }
        Write-Host "Pushed: $($t.Name) $($t.Version)" -ForegroundColor Green
    }
}

Write-Host "`nDone. Artifacts: $outDir" -ForegroundColor Green
if (-not $Push) {
    Write-Host "Pack-only run (no push). Add -Push -ApiKey <key> to upload to NuGet." -ForegroundColor Yellow
}
