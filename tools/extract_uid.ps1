# tools/extract_uid.ps1
# Extract all log entries for a specific UID from Risk Management logs
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [Parameter(Mandatory)]
    [string]$Uid,
    [string]$OutputFile = "",
    [switch]$ShowTimeline = $false
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Path)) {
    Write-Error "Log file not found: $Path"
    exit 1
}

# Auto-generate output file if not provided
if ([string]::IsNullOrEmpty($OutputFile)) {
    $dir = Split-Path $Path -Parent
    $timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $OutputFile = Join-Path $dir "uid_$($Uid)_$timestamp.log"
}

Write-Host "🔍 Extracting UID timeline..." -ForegroundColor Cyan
Write-Host "   UID:       $Uid" -ForegroundColor Gray
Write-Host "   Source:    $Path" -ForegroundColor Gray
Write-Host "   Output:    $OutputFile" -ForegroundColor Gray
Write-Host ""

# Extract all lines containing the UID
$matches = Get-Content $Path | Select-String -Pattern "uid=$Uid"

if ($matches.Count -eq 0) {
    Write-Host "❌ No entries found for UID: $Uid" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Found $($matches.Count) entries for UID: $Uid" -ForegroundColor Green
Write-Host ""

# Process and save matches
$timeline = @()
foreach ($match in $matches) {
    $line = $match.Line

    # Extract timestamp and tag if possible
    if ($line -match '\[(\d{2}:\d{2}:\d{2}\.\d{3})\].*?(\d{3}/\w+)(.*)') {
        $timestamp = $matches[1]
        $tag = $matches[2]
        $message = $matches[3].Trim()

        $timeline += @{
            Timestamp = $timestamp
            Tag = $tag
            Message = $message
            FullLine = $line
        }
    } else {
        $timeline += @{
            Timestamp = "Unknown"
            Tag = "Unknown"
            Message = $line
            FullLine = $line
        }
    }
}

# Sort by timestamp
$timeline = $timeline | Sort-Object Timestamp

# Save to file
$timeline | ForEach-Object { $_.FullLine } | Set-Content -Path $OutputFile -Encoding UTF8

# Show timeline if requested
if ($ShowTimeline.IsPresent) {
    Write-Host "📋 Timeline for UID $Uid" -ForegroundColor White
    Write-Host "=" * 60 -ForegroundColor Gray

    foreach ($entry in $timeline) {
        # Color coding by tag
        $color = switch -Wildcard ($entry.Tag) {
            "*CALC*" { "Green" }
            "*RISK*" { "Yellow" }
            "*SL*"   { "Cyan" }
            "*STR*"  { "White" }
            "*ORD*"  { "Magenta" }
            "*POS*"  { "Blue" }
            default  { "Gray" }
        }

        Write-Host "[$($entry.Timestamp)] $($entry.Tag)" -ForegroundColor $color -NoNewline
        Write-Host "$($entry.Message)" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "=" * 60 -ForegroundColor Gray
}

Write-Host "📄 Timeline saved to: $OutputFile" -ForegroundColor Green

# Summary statistics
$tagCounts = $timeline | Group-Object Tag | ForEach-Object {
    @{ Tag = $_.Name; Count = $_.Count }
} | Sort-Object Count -Descending

Write-Host ""
Write-Host "📊 Entry breakdown:" -ForegroundColor White
foreach ($tagInfo in $tagCounts) {
    Write-Host "   $($tagInfo.Tag): $($tagInfo.Count)" -ForegroundColor Gray
}