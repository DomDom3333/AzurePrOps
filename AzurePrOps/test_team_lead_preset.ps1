# Test script to reproduce DateTime cast exception with Team Lead preset
Write-Host "Testing Team Lead Preset DateTime Cast Exception" -ForegroundColor Green

# Build the project first
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build AzurePrOps\AzurePrOps.csproj

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful. Now testing Team Lead preset..." -ForegroundColor Green

# Run a simple test to check if we can reproduce the issue
# This would require running the actual application, but let's check the logic
Write-Host "DateTime assignment test:" -ForegroundColor Yellow

# Test the DateTime.Now.AddDays(-30) assignment that happens in ApplyWorkflowPreset
$testDate = [DateTime]::Now.AddDays(-30)
Write-Host "DateTime.Now.AddDays(-30) = $testDate"
Write-Host "Type: $($testDate.GetType().FullName)"

# Test nullable DateTime assignment
$nullableDate = [System.Nullable[DateTime]]$testDate
Write-Host "Nullable DateTime assignment successful: $nullableDate"
Write-Host "Type: $($nullableDate.GetType().FullName)"

Write-Host "Basic DateTime operations seem to work correctly." -ForegroundColor Green
Write-Host "The cast exception might be occurring during JSON serialization/deserialization or UI binding." -ForegroundColor Yellow