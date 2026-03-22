# Usage: .\find_type.ps1 -Dump <path to dump.cs> -Search <type name or keyword>
# Finds a type by name and prints its full block including namespace,
# class declaration, fields, and methods with their RVAs.

param(
    [Parameter(Mandatory)][string]$Dump,
    [Parameter(Mandatory)][string]$Search
)

$lines = Get-Content $Dump

# Find all lines where the search term appears in a type declaration
$startLines = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $Search -and $lines[$i] -match '\b(class|struct|interface)\b') {
        $startLines += $i
    }
}

if ($startLines.Count -eq 0) {
    Write-Host "No type declarations found matching '$Search'"
    Write-Host ""
    Write-Host "--- Raw matches (non-declaration) ---"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $Search) {
            Write-Host "$($i+1): $($lines[$i])"
        }
    }
    exit
}

Write-Host "Found $($startLines.Count) type declaration(s) matching '$Search':"
Write-Host ""

foreach ($start in $startLines) {
    # Look back up to 5 lines for a namespace comment
    $nsLine = ""
    for ($i = [Math]::Max(0, $start - 5); $i -lt $start; $i++) {
        if ($lines[$i] -match '// Namespace:') {
            $nsLine = $lines[$i]
        }
    }
    if ($nsLine) { Write-Host $nsLine }

    # Print the block from the declaration until the matching closing brace
    $depth = 0
    $started = $false
    for ($i = $start; $i -lt $lines.Count; $i++) {
        Write-Host "$($i+1): $($lines[$i])"
        foreach ($char in $lines[$i].ToCharArray()) {
            if ($char -eq '{') { $depth++ ; $started = $true }
            if ($char -eq '}') {
                $depth--
                if ($started -and $depth -eq 0) { break }
            }
        }
        if ($started -and $depth -eq 0) { break }
    }
    Write-Host ""
    Write-Host "---"
    Write-Host ""
}
