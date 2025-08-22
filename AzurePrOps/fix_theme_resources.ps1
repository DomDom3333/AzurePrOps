# PowerShell script to fix StaticResource to DynamicResource for theme switching
# This script updates all theme-related color references to use DynamicResource

Write-Host "Fixing theme resource references for uniform dark mode..." -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green

$viewsPath = "X:\GitKraken\Personal\AzurePrOps\AzurePrOps\AzurePrOps\Views"
$stylesPath = "X:\GitKraken\Personal\AzurePrOps\AzurePrOps\AzurePrOps\Styles"

# Define theme-related resource names that should use DynamicResource
$themeResources = @(
    "PrimaryBrush", "PrimaryHoverBrush", "PrimaryLightBrush",
    "SuccessBrush", "SuccessLightBrush", "DangerBrush", "DangerLightBrush", 
    "WarningBrush", "WarningLightBrush", "ErrorBrush",
    "MutedBrush", "BorderBrush", "SurfaceBrush", "BackgroundBrush", 
    "TextPrimaryBrush", "TextSecondaryBrush",
    "HoverBrush", "SelectedBrush", "FocusBrush", "CardHoverBrush", "CardBackgroundBrush",
    "ErrorBackgroundBrush", "ErrorBorderBrush", "OverlayBackgroundBrush"
)

function Update-ThemeResources {
    param(
        [string]$FilePath,
        [string]$FileType
    )
    
    Write-Host "Processing $FileType`: $($FilePath | Split-Path -Leaf)" -ForegroundColor Yellow
    
    try {
        $content = Get-Content $FilePath -Raw -Encoding UTF8
        $originalContent = $content
        $changeCount = 0
        
        foreach ($resource in $themeResources) {
            $pattern = "\{StaticResource $resource\}"
            $replacement = "{DynamicResource $resource}"
            
            if ($content -match $pattern) {
                $matches = ([regex]::Matches($content, $pattern)).Count
                $content = $content -replace $pattern, $replacement
                $changeCount += $matches
                Write-Host "  - Updated $matches instances of $resource" -ForegroundColor White
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

# Process all AXAML files in Views directory
Write-Host "`nUpdating View files..." -ForegroundColor Cyan
Get-ChildItem -Path $viewsPath -Filter "*.axaml" -Recurse | ForEach-Object {
    $changes = Update-ThemeResources -FilePath $_.FullName -FileType "View"
    $totalChanges += $changes
}

# Process Styles.xaml file
Write-Host "`nUpdating Styles file..." -ForegroundColor Cyan
$stylesFile = Join-Path $stylesPath "Styles.xaml"
if (Test-Path $stylesFile) {
    $changes = Update-ThemeResources -FilePath $stylesFile -FileType "Styles"
    $totalChanges += $changes
}

Write-Host "`n=======================================================" -ForegroundColor Green
Write-Host "Theme resource fix completed!" -ForegroundColor Green
Write-Host "Total StaticResource to DynamicResource changes: $totalChanges" -ForegroundColor White

if ($totalChanges -gt 0) {
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "1. Test the application with theme switching" -ForegroundColor White
    Write-Host "2. Verify dark mode applies uniformly across all views" -ForegroundColor White
    Write-Host "3. Check that overlays and modals are visible in both themes" -ForegroundColor White
} else {
    Write-Host "`nNo changes were needed - resources may already be correct." -ForegroundColor Gray
}