#!/usr/bin/env pwsh

Write-Host "Testing IDE Button Issues" -ForegroundColor Yellow
Write-Host "=========================" -ForegroundColor Yellow

# Test 1: Check if VSCode is detected by EditorDetector
Write-Host "`n1. Testing EditorDetector PATH-based detection..." -ForegroundColor Cyan

# Check if 'code' is in PATH
$codeInPath = $null
try {
    $codeInPath = Get-Command "code" -ErrorAction SilentlyContinue
} catch {}

if ($codeInPath) {
    Write-Host "✓ VSCode 'code' command found in PATH: $($codeInPath.Source)" -ForegroundColor Green
} else {
    Write-Host "✗ VSCode 'code' command NOT found in PATH" -ForegroundColor Red
}

# Test 2: Check common VSCode installation locations
Write-Host "`n2. Testing VSCode installation locations..." -ForegroundColor Cyan

$vscodeLocations = @(
    "$env:LOCALAPPDATA\Programs\Microsoft VS Code\bin\code.cmd",
    "$env:PROGRAMFILES\Microsoft VS Code\bin\code.cmd",
    "${env:PROGRAMFILES(X86)}\Microsoft VS Code\bin\code.cmd",
    "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe",
    "$env:PROGRAMFILES\Microsoft VS Code\Code.exe",
    "${env:PROGRAMFILES(X86)}\Microsoft VS Code\Code.exe"
)

$foundVSCode = $false
foreach ($location in $vscodeLocations) {
    if (Test-Path $location) {
        Write-Host "✓ Found VSCode at: $location" -ForegroundColor Green
        $foundVSCode = $true
    }
}

if (-not $foundVSCode) {
    Write-Host "✗ VSCode not found in common installation locations" -ForegroundColor Red
}

# Test 3: Demonstrate the issue
Write-Host "`n3. Issue Analysis:" -ForegroundColor Cyan
Write-Host "- ViewInIDE_PointerPressed: Only sets e.Handled = true (does nothing)" -ForegroundColor Red
Write-Host "- ViewInIDE_Click: Calls IDEService.OpenInIDE() but may fail if editor not detected" -ForegroundColor Red
Write-Host "- EditorDetector: Only checks PATH, missing VSCode in default install locations" -ForegroundColor Red

Write-Host "`n4. Solutions needed:" -ForegroundColor Cyan
Write-Host "- Fix ViewInIDE_PointerPressed to actually open IDE" -ForegroundColor Yellow
Write-Host "- Enhance EditorDetector to check common installation paths" -ForegroundColor Yellow
Write-Host "- Improve error handling and user feedback" -ForegroundColor Yellow

Write-Host "`nTest completed!" -ForegroundColor Green