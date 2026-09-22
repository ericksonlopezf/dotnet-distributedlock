# Copyright © Erickson Lopez. MIT License.
<#
.SYNOPSIS
    Detects which packages are affected by a Git diff (e.g. Pull Request vs main).
.DESCRIPTION
    Analyzes changed files between a base commit and head commit.
    Categorizes changes to determine:
    1. Whether production C# code or tests were modified (skipping Stryker if docs-only).
    2. Which specific package(s) were modified, building a targeted matrix for Stryker.
.PARAMETER BaseRef
    Base git reference (default: "origin/main").
.PARAMETER HeadRef
    Head git reference (default: "HEAD").
.PARAMETER RunAll
    Forces inclusion of all packages regardless of git diff (useful for main push, schedule, or manual release).
#>

[CmdletBinding()]
param (
    [string]$BaseRef = "origin/main",
    [string]$HeadRef = "HEAD",
    [switch]$RunAll,
    [string]$RunAllMode = "false"
)

$ErrorActionPreference = "Stop"

$shouldRunAll = $RunAll.IsPresent -or ($RunAllMode -eq "true" -or $RunAllMode -eq "True" -or $RunAllMode -eq "1" -or $RunAllMode -eq "all")

$packageMap = [ordered]@{
    "Core" = @{
        patterns = @("src/EricksonLopez.DistributedLock.Abstractions/", "tests/EricksonLopez.DistributedLock.Abstractions.Tests/")
        config = "stryker-config.json"
        outputDir = "StrykerOutput/core"
        artifact = "stryker-report-core"
    }
    "PostgreSql" = @{
        patterns = @("src/EricksonLopez.DistributedLock.PostgreSql/", "tests/EricksonLopez.DistributedLock.PostgreSql.Tests/")
        config = "stryker-postgresql-config.json"
        outputDir = "StrykerOutput/postgresql"
        artifact = "stryker-report-postgresql"
    }
    "SqlServer" = @{
        patterns = @("src/EricksonLopez.DistributedLock.SqlServer/", "tests/EricksonLopez.DistributedLock.SqlServer.Tests/")
        config = "stryker-sqlserver-config.json"
        outputDir = "StrykerOutput/sqlserver"
        artifact = "stryker-report-sqlserver"
    }
    "MySql" = @{
        patterns = @("src/EricksonLopez.DistributedLock.MySql/", "tests/EricksonLopez.DistributedLock.MySql.Tests/")
        config = "stryker-mysql-config.json"
        outputDir = "StrykerOutput/mysql"
        artifact = "stryker-report-mysql"
    }
    "MariaDb" = @{
        patterns = @("src/EricksonLopez.DistributedLock.MariaDb/", "tests/EricksonLopez.DistributedLock.MariaDb.Tests/")
        config = "stryker-mariadb-config.json"
        outputDir = "StrykerOutput/mariadb"
        artifact = "stryker-report-mariadb"
    }
    "Oracle" = @{
        patterns = @("src/EricksonLopez.DistributedLock.Oracle/", "tests/EricksonLopez.DistributedLock.Oracle.Tests/")
        config = "stryker-oracle-config.json"
        outputDir = "StrykerOutput/oracle"
        artifact = "stryker-report-oracle"
    }
    "Sqlite" = @{
        patterns = @("src/EricksonLopez.DistributedLock.Sqlite/", "tests/EricksonLopez.DistributedLock.Sqlite.Tests/")
        config = "stryker-sqlite-config.json"
        outputDir = "StrykerOutput/sqlite"
        artifact = "stryker-report-sqlite"
    }
    "Redis" = @{
        patterns = @("src/EricksonLopez.DistributedLock.Redis/", "tests/EricksonLopez.DistributedLock.Redis.Tests/")
        config = "stryker-redis-config.json"
        outputDir = "StrykerOutput/redis"
        artifact = "stryker-report-redis"
    }
}

$globalTriggers = @("Directory.Build.props", "Directory.Packages.props", "EricksonLopez.DistributedLock.slnx", ".editorconfig")

$hasCodeChanges = $false
$allAffected = $false
$affectedPackages = [System.Collections.Generic.HashSet[string]]::new()

if ($shouldRunAll) {
    Write-Host "Forced RunAll mode enabled: running Stryker across all $($packageMap.Count) packages." -ForegroundColor Cyan
    $hasCodeChanges = $true
    $allAffected = $true
    foreach ($pkg in $packageMap.Keys) {
        [void]$affectedPackages.Add($pkg)
    }
} else {
    $changedFiles = @()
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"

    try {
        $rawOutput = git diff --name-only "$BaseRef...$HeadRef" 2>&1
        $changedFiles = @($rawOutput | Where-Object { $_ -is [string] -and $_ -notmatch '^(warning:|fatal:)' -and $_.Trim() -ne "" })
        
        if ($LASTEXITCODE -ne 0 -or $changedFiles.Count -eq 0) {
            $rawOutput2 = git diff --name-only "$BaseRef" "$HeadRef" 2>&1
            $changedFiles = @($rawOutput2 | Where-Object { $_ -is [string] -and $_ -notmatch '^(warning:|fatal:)' -and $_.Trim() -ne "" })
        }
    } catch {
        $changedFiles = @()
    }

    if ($changedFiles.Count -eq 0) {
        try {
            $rawOutput3 = git diff --name-only HEAD 2>&1
            $changedFiles = @($rawOutput3 | Where-Object { $_ -is [string] -and $_ -notmatch '^(warning:|fatal:)' -and $_.Trim() -ne "" })
        } catch {
            $changedFiles = @()
        }
    }
    $ErrorActionPreference = $prevEap

    Write-Host "Evaluating $(($changedFiles | Measure-Object).Count) changed file(s)..." -ForegroundColor Cyan

    foreach ($file in $changedFiles) {
        $norm = $file.Replace("\", "/")
        
        foreach ($gt in $globalTriggers) {
            if ($norm -eq $gt) {
                $hasCodeChanges = $true
                $allAffected = $true
                Write-Host "  Trigger: Global file modified -> $norm" -ForegroundColor Yellow
            }
        }
        
        foreach ($pkg in $packageMap.Keys) {
            foreach ($pat in $packageMap[$pkg].patterns) {
                if ($norm.StartsWith($pat)) {
                    $hasCodeChanges = $true
                    [void]$affectedPackages.Add($pkg)
                    Write-Host "  Trigger: $pkg affected by -> $norm" -ForegroundColor Green
                    # If Core / Abstractions changes, all implementations depend on it
                    if ($pkg -eq "Core") {
                        $allAffected = $true
                    }
                }
            }
        }
    }

    if ($allAffected) {
        foreach ($pkg in $packageMap.Keys) {
            [void]$affectedPackages.Add($pkg)
        }
    }
}

$matrixInclude = @()
foreach ($pkg in $packageMap.Keys) {
    if ($affectedPackages.Contains($pkg)) {
        $info = $packageMap[$pkg]
        $matrixInclude += [ordered]@{
            name = $pkg
            config = $info.config
            "output-dir" = $info.outputDir
            "artifact-name" = $info.artifact
        }
    }
}

if ($matrixInclude.Count -eq 0) {
    $matrixJson = "[]"
} elseif ($matrixInclude.Count -eq 1) {
    $singleJson = $matrixInclude[0] | ConvertTo-Json -Compress
    $matrixJson = "[$singleJson]"
} else {
    $matrixJson = $matrixInclude | ConvertTo-Json -Compress
}

Write-Host "`nSummary:" -ForegroundColor Cyan
Write-Host "  Has code changes        : $hasCodeChanges"
Write-Host "  Affected packages count : $($affectedPackages.Count)"
Write-Host "  Matrix JSON             : $matrixJson"

if ($env:GITHUB_OUTPUT) {
    $hasChangesStr = if ($hasCodeChanges) { "true" } else { "false" }
    $hasPkgsStr = if ($affectedPackages.Count -gt 0) { "true" } else { "false" }
    
    "has_code_changes=$hasChangesStr" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "has_affected_packages=$hasPkgsStr" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "affected_count=$($affectedPackages.Count)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "matrix=$matrixJson" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
