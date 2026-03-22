# Usage: .\find_namespace.ps1 -Dump <path to dump.cs> -Search <keyword>
# For every line matching a keyword, prints the nearest namespace and class
# declaration above it. Use this to find the correct fully-qualified type name.

param(
    [Parameter(Mandatory)][string]$Dump,
    [Parameter(Mandatory)][string]$Search
)

$lines = Get-Content $Dump

Write-Host "Searching for '$Search' in $Dump..."
Write-Host ""

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -notmatch $Search) { continue }

    # Walk backwards to find nearest namespace and class/struct/interface
    $ns  = ""
    $cls = ""
    for ($j = $i; $j -ge 0; $j--) {
        if (-not $ns -and $lines[$j] -match '// Namespace:') {
            $ns = $lines[$j].Trim()
        }
        if (-not $cls -and $lines[$j] -match '\b(class|struct|interface)\b' `
                         -and $lines[$j] -match '\b(public|private|internal|protected|sealed|abstract|static)\b') {
            $cls = $lines[$j].Trim()
        }
        if ($ns -and $cls) { break }
    }

    Write-Host "Line $($i+1): $($lines[$i].Trim())"
    Write-Host "  Namespace : $ns"
    Write-Host "  Class     : $cls"
    Write-Host ""
}
