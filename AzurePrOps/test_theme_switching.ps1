# PowerShell script to test theme switching functionality
# This script will build and run the application to test if theme switching works

Write-Host "Testing Dark Mode Theme Switching Implementation..." -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green

# Build the project
Write-Host "`nBuilding the project..." -ForegroundColor Yellow
Set-Location "X:\GitKraken\Personal\AzurePrOps\AzurePrOps"
dotnet build AzurePrOps.sln --configuration Debug

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Build successful!" -ForegroundColor Green
    
    Write-Host "`nStarting application for manual testing..." -ForegroundColor Yellow
    Write-Host "Instructions for testing:" -ForegroundColor Cyan
    Write-Host "1. The application should start with the saved theme preference" -ForegroundColor White
    Write-Host "2. Go to Settings (if you have connection settings configured)" -ForegroundColor White
    Write-Host "3. In the Interface section, try changing the Theme dropdown:" -ForegroundColor White
    Write-Host "   - System: Should follow system theme" -ForegroundColor White
    Write-Host "   - Light: Should use light theme" -ForegroundColor White
    Write-Host "   - Dark: Should use dark theme with:" -ForegroundColor White
    Write-Host "     * Dark backgrounds" -ForegroundColor White
    Write-Host "     * Light text" -ForegroundColor White
    Write-Host "     * Darker interactive elements" -ForegroundColor White
    Write-Host "4. Click Save to persist the theme change" -ForegroundColor White
    Write-Host "5. Close and restart the app to verify theme persistence" -ForegroundColor White
    Write-Host "`nPress any key to launch the application..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    
    # Start the application
    Start-Process "X:\GitKraken\Personal\AzurePrOps\AzurePrOps\AzurePrOps\bin\Debug\net9.0\AzurePrOps.exe"
    
    Write-Host "`nApplication started! Please test the theme switching functionality." -ForegroundColor Green
    Write-Host "Check the following components for proper dark mode support:" -ForegroundColor Cyan
    Write-Host "✓ Main window background" -ForegroundColor White
    Write-Host "✓ Settings window" -ForegroundColor White
    Write-Host "✓ Buttons and controls" -ForegroundColor White
    Write-Host "✓ Text readability" -ForegroundColor White
    Write-Host "✓ Borders and separators" -ForegroundColor White
    Write-Host "! TemporaryHighlightTransformer may still use light colors (to fix later)" -ForegroundColor Yellow
    
} else {
    Write-Host "Build failed! Check the error messages above." -ForegroundColor Red
    exit 1
}