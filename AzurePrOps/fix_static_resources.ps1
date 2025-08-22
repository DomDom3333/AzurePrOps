# PowerShell script to fix remaining StaticResource to DynamicResource for theme switching
# This script updates all remaining StaticResource references to use DynamicResource

Write-Host "Fixing remaining StaticResource references for uniform dark mode..." -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green

$projectRoot = "X:\GitKraken\Personal\AzurePrOps\AzurePrOps\AzurePrOps"

# Define patterns to replace
$replacements = @{
    "StaticResource SurfaceBrush" = "DynamicResource SurfaceBrush"
    "StaticResource BackgroundBrush" = "DynamicResource BackgroundBrush"
    "StaticResource TextPrimaryBrush" = "DynamicResource TextPrimaryBrush"
    "StaticResource BorderBrush" = "DynamicResource BorderBrush"
    "StaticResource HoverBrush" = "DynamicResource HoverBrush"
    "StaticResource SelectedBrush" = "DynamicResource SelectedBrush"
    "StaticResource MutedBrush" = "DynamicResource MutedBrush"
    "StaticResource PrimaryBrush" = "DynamicResource PrimaryBrush"
}

function Update-FileResources {
    param(
        [string]$FilePath,
        [string]$FileType
    )
    
    Write-Host "Processing $FileType`: $($FilePath | Split-Path -Leaf)" -ForegroundColor Yellow
    
    try {
        $content = Get-Content $FilePath -Raw -Encoding UTF8
        $originalContent = $content
        $changeCount = 0
        
        foreach ($pattern in $replacements.Keys) {
            $replacement = $replacements[$pattern]
            
            if ($content -match [regex]::Escape("{$pattern}")) {
                $matches = ([regex]::Matches($content, [regex]::Escape("{$pattern}"))).Count
                $content = $content -replace [regex]::Escape("{$pattern}"), "{$replacement}"
                $changeCount += $matches
                Write-Host "  - Updated $matches instances of $pattern" -ForegroundColor White
            }
        }
        
        if ($changeCount -gt 0) {
            Set-Content -Path $FilePath -Value $content -Encoding UTF8 -NoNewline
            Write-Host "  ✓ Total changes: $changeCount" -ForegroundColor Green
        } else {
            Write-Host "  - No changes needed" -ForegroundColor Gray
        }
        
        return $changeCount
    }
    catch {
        Write-Host "  ✗ Error processing file: $($_.Exception.Message)" -ForegroundColor Red
        return 0
    }
}

$totalChanges = 0

# Process all relevant files
$filePatterns = @(
    "Views\*.axaml",
    "Controls\*.axaml", 
    "Styles\*.xaml"
)

foreach ($pattern in $filePatterns) {
    $fullPattern = Join-Path $projectRoot $pattern
    Write-Host "`nProcessing files matching: $pattern" -ForegroundColor Cyan
    
    Get-ChildItem -Path $fullPattern -ErrorAction SilentlyContinue | ForEach-Object {
        $changes = Update-FileResources -FilePath $_.FullName -FileType ($pattern -split '\' | Select-Object -First 1)
        $totalChanges += $changes
    }
}

Write-Host "`n================================================================" -ForegroundColor Green
Write-Host "StaticResource to DynamicResource fix completed!" -ForegroundColor Green
Write-Host "Total changes made: $totalChanges" -ForegroundColor White

if ($totalChanges -gt 0) {
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "1. Test the application with theme switching" -ForegroundColor White
    Write-Host "2. Verify dark mode backgrounds are no longer white" -ForegroundColor White
    Write-Host "3. Check that all UI components adapt properly to theme changes" -ForegroundColor White
} else {
    Write-Host "`nNo changes were needed - resources may already be correct." -ForegroundColor Gray
}