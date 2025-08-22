# Test script to verify date filtering functionality
Write-Host "Testing Date Filtering Functionality" -ForegroundColor Green

# Build the project first
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build AzurePrOps\AzurePrOps.csproj

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Create a simple test to verify date filtering logic
Write-Host "Date filtering logic test:" -ForegroundColor Yellow

# Test current date vs 30 days ago
$currentDate = Get-Date
$thirtyDaysAgo = $currentDate.AddDays(-30)

Write-Host "Current Date: $currentDate"
Write-Host "30 Days Ago: $thirtyDaysAgo"

# Check if DateTime.Now.AddDays(-30) produces expected results
Write-Host "Testing DateTime.Now.AddDays(-30) calculation..." -ForegroundColor Yellow
$testDate = [DateTime]::Now.AddDays(-30)
Write-Host "Result: $testDate"

# Test date comparison logic similar to what's in the filtering service
$testPRDate1 = $currentDate.AddDays(-15) # 15 days ago - should pass filter
$testPRDate2 = $currentDate.AddDays(-45) # 45 days ago - should not pass filter

Write-Host "`nTesting filter logic:"
Write-Host "PR Date 1 (15 days ago): $testPRDate1"
Write-Host "PR Date 2 (45 days ago): $testPRDate2"
Write-Host "Filter Date (30 days ago): $thirtyDaysAgo"

$result1 = $testPRDate1 -ge $thirtyDaysAgo
$result2 = $testPRDate2 -ge $thirtyDaysAgo

Write-Host "PR1 passes filter (>= 30 days ago): $result1" -ForegroundColor $(if($result1) {"Green"} else {"Red"})
Write-Host "PR2 passes filter (>= 30 days ago): $result2" -ForegroundColor $(if($result2) {"Red"} else {"Green"})

Write-Host "`nDate filtering test completed!" -ForegroundColor Green