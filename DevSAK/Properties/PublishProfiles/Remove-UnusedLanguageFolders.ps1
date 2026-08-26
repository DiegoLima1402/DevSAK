param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDir
)

$ErrorActionPreference = 'Stop'
$PublishDir = $PublishDir.Trim().Trim('"').TrimEnd('\', '/')

if (-not (Test-Path -LiteralPath $PublishDir)) {
    throw "Publish directory not found: $PublishDir"
}

$keep = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@(
    'Assets',
    'Resources',
    'Tools',
    'Microsoft.UI.Xaml',
    'NpuDetect',
    'pt-BR',
    'en-US',
    'en-GB'
) | ForEach-Object { [void]$keep.Add($_) }

# WASDK self-contained copies UI satellite folders for many cultures.
# DevSAK UI is PT-BR; keep English as framework fallback only.
$cultureName = [regex]::new('^[a-z]{2,3}([_-][A-Za-z0-9]+)+$', 'IgnoreCase')

Get-ChildItem -LiteralPath $PublishDir -Directory |
    Where-Object { -not $keep.Contains($_.Name) -and $cultureName.IsMatch($_.Name) } |
    ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }
