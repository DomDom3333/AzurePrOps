#!/usr/bin/env pwsh

Write-Host "Testing VSCode Detection Issue" -ForegroundColor Yellow
Write-Host "===============================" -ForegroundColor Yellow

# Test what EditorDetector.GetDefaultEditor() actually returns
Write-Host "`n1. Testing EditorDetector.GetDefaultEditor()..." -ForegroundColor Cyan

# Build a simple test program to call EditorDetector
$testCode = @'
using System;
using AzurePrOps.Models;

class Program 
{
    static void Main()
    {
        Console.WriteLine($"GetDefaultEditor: '{EditorDetector.GetDefaultEditor()}'");
        
        var available = EditorDetector.GetAvailableEditors();
        Console.WriteLine($"Available editors count: {available.Count}");
        foreach (var editor in available)
        {
            Console.WriteLine($"  - {editor}");
        }
        
        var codePath = EditorDetector.GetEditorFullPath("code");
        Console.WriteLine($"GetEditorFullPath('code'): '{codePath}'");
    }
}
'@

# Write test program
$testFile = "TestEditorDetector.cs"
Set-Content -Path $testFile -Value $testCode

Write-Host "Building and running EditorDetector test..." -ForegroundColor White

try {
    # Compile the test
    $projectRef = "AzurePrOps\AzurePrOps.csproj"
    & dotnet run --project $projectRef --verbosity quiet -- test-editor 2>&1 | Out-Null
    
    # Since we can't easily run isolated code, let's check the actual VSCode installations
    Write-Host "`n2. Checking VSCode installation paths directly..." -ForegroundColor Cyan
    
    $vscodeLocations = @(
        "$env:LOCALAPPDATA\Programs\Microsoft VS Code\bin\code.cmd",
        "$env:PROGRAMFILES\Microsoft VS Code\bin\code.cmd", 
        "${env:PROGRAMFILES(X86)}\Microsoft VS Code\bin\code.cmd",
        "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe",
        "$env:PROGRAMFILES\Microsoft VS Code\Code.exe",
        "${env:PROGRAMFILES(X86)}\Microsoft VS Code\Code.exe"
    )
    
    $foundVSCode = $null
    foreach ($location in $vscodeLocations) {
        if (Test-Path $location) {
            Write-Host "✓ Found VSCode at: $location" -ForegroundColor Green
            $foundVSCode = $location
            break
        }
    }
    
    if ($foundVSCode) {
        Write-Host "`n3. Testing VSCode launch capability..." -ForegroundColor Cyan
        try {
            $tempFile = [System.IO.Path]::GetTempFileName()
            "test content" | Out-File -FilePath $tempFile
            
            # Test if we can at least start the process (don't wait for it)
            $processInfo = New-Object System.Diagnostics.ProcessStartInfo
            $processInfo.FileName = $foundVSCode
            $processInfo.Arguments = "-g `"$tempFile`":1"
            $processInfo.UseShellExecute = $false
            $processInfo.CreateNoWindow = $true
            
            $process = [System.Diagnostics.Process]::Start($processInfo)
            if ($process -ne $null) {
                Write-Host "✓ VSCode can be launched successfully" -ForegroundColor Green
                $process.Kill() # Don't leave VSCode running
            }
            
            Remove-Item $tempFile -Force
        } catch {
            Write-Host "✗ Failed to launch VSCode: $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        Write-Host "✗ No VSCode installation found" -ForegroundColor Red
    }
    
    Write-Host "`n4. Checking PATH for 'code' command..." -ForegroundColor Cyan
    $codeInPath = Get-Command "code" -ErrorAction SilentlyContinue
    if ($codeInPath) {
        Write-Host "✓ 'code' command found in PATH: $($codeInPath.Source)" -ForegroundColor Green
    } else {
        Write-Host "✗ 'code' command NOT in PATH" -ForegroundColor Red
        Write-Host "  This explains why IDEIntegrationService fails with 'code' command" -ForegroundColor Yellow
    }

} catch {
    Write-Host "✗ Test error: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    if (Test-Path $testFile) {
        Remove-Item $testFile -Force
    }
}

Write-Host "`nTest completed!" -ForegroundColor Green