# tools/tail_risk.ps1
# Real-time Risk Management Log Monitoring
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [string[]]$IncludeTags = @('468/CALC', '468/RISK', '468/SL', '468/STR', '468/ORD', '468/POS'),
    [int]$Seconds = 60,
    [string]$OutputFile = "",
    [switch]$ShowAll = $false
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Path)) {
    Write-Error "Log file not found: $Path"
    exit 1
}

Write-Host "📡 Monitoring Risk Management logs..." -ForegroundColor Cyan
Write-Host "   File:     $Path" -ForegroundColor Gray
Write-Host "   Tags:     $($IncludeTags -join ', ')" -ForegroundColor Gray
Write-Host "   Duration: $Seconds seconds" -ForegroundColor Gray
if ($OutputFile) {
    Write-Host "   Output:   $OutputFile" -ForegroundColor Gray
}
Write-Host ""

$end = (Get-Date).AddSeconds($Seconds)
$lastSize = (Get-Item $Path).Length
$lineBuffer = @()

while (Get-Date -lt $end) {
    $currentSize = (Get-Item $Path).Length

    if ($currentSize -gt $lastSize) {
        # Read new lines
        $newLines = Get-Content $Path -Tail 100 | Select-Object -Last 20

        foreach ($line in $newLines) {
            # Skip if line already processed
            if ($lineBuffer -contains $line) { continue }
            $lineBuffer += $line

            # Keep buffer manageable
            if ($lineBuffer.Count -gt 500) {
                $lineBuffer = $lineBuffer | Select-Object -Last 400
            }

            # Check if line matches our tags
            $matchesTag = $ShowAll.IsPresent -or ($IncludeTags | Where-Object { $line -like "*$_*" })

            if ($matchesTag) {
                # Extract timestamp and format
                if ($line -match '\[(\d{2}:\d{2}:\d{2}\.\d{3})\].*?(\d{3}/\w+)(.*)') {
                    $timestamp = $matches[1]
                    $tag = $matches[2]
                    $message = $matches[3].Trim()

                    # Color coding by tag
                    $color = switch -Wildcard ($tag) {
                        "*CALC*" { "Green" }
                        "*RISK*" { "Yellow" }
                        "*SL*"   { "Cyan" }
                        "*STR*"  { "White" }
                        "*ORD*"  { "Magenta" }
                        "*POS*"  { "Blue" }
                        default  { "Gray" }
                    }

                    $formatted = "[$timestamp] $tag$message"
                    Write-Host $formatted -ForegroundColor $color

                    # Write to output file if specified
                    if ($OutputFile) {
                        $formatted | Add-Content -Path $OutputFile -Encoding UTF8
                    }
                } else {
                    # Fallback for lines that don't match expected format
                    Write-Host $line -ForegroundColor Gray
                    if ($OutputFile) {
                        $line | Add-Content -Path $OutputFile -Encoding UTF8
                    }
                }
            }
        }
    }

    $lastSize = $currentSize
    Start-Sleep -Milliseconds 300
}

Write-Host ""
Write-Host "📡 Monitoring completed" -ForegroundColor Green
if ($OutputFile) {
    Write-Host "📄 Output saved to: $OutputFile" -ForegroundColor Green
}