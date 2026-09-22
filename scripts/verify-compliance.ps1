# Copyright © Erickson Lopez. MIT License.
<#
.SYNOPSIS
    Automated zero-tolerance repository compliance & governance verifier for EricksonLopez.Distributedlock.
.DESCRIPTION
    Validates:
    1. Documentation file naming strictly in kebab-case.
    2. Canonical MIT copyright header across all source, project, script, and CI configuration files.
    3. Strict "One Type Per File" rule for all top-level types in src/.
    4. Zero [Obsolete] usages across all solution code.
    5. Official support and security email normalization (ericksonlopezf@gmail.com).
    6. Directory.Build.props standards (ImplicitUsings=disable, PackageProjectUrl, icon.png).
    7. Zero prohibited <NoWarn> suppressions across all projects (no bypasses).
    8. Valid internal Markdown links.
#>

[CmdletBinding()]
param (
    [string]$RootDirectory = "."
)

$ErrorActionPreference = "Stop"
$violations = 0

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  REPOSITORY COMPLIANCE & ARCHITECTURE AUDITOR    " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Kebab-case documentation verification
Write-Host "`n[1/8] Checking documentation file naming (kebab-case)..." -ForegroundColor Yellow
$standardExceptions = @(
    "README.md", "LICENSE", "SECURITY.md", "SUPPORT.md", 
    "CONTRIBUTING.md", "CODE_OF_CONDUCT.md", "CHANGELOG.md",
    "GOVERNANCE.md", "ROADMAP.md", "TESTING-ROADMAP.md"
)

$docsFiles = Get-ChildItem -Path (Join-Path $RootDirectory "docs") -Recurse -Filter "*.md" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch "[\\/](obj|bin)[\\/]" }
$rootMdFiles = Get-ChildItem -Path $RootDirectory -Depth 0 -Filter "*.md" -ErrorAction SilentlyContinue
$allDocs = @($docsFiles) + @($rootMdFiles)

$badDocNames = 0
foreach ($doc in $allDocs) {
    $filename = $doc.Name
    if ($standardExceptions -contains $filename) {
        continue
    }
    if ($filename -cne $filename.ToLower() -or $filename -match "_") {
        Write-Host "  ❌ Non-kebab-case document: $($doc.FullName)" -ForegroundColor Red
        $violations++
        $badDocNames++
    }
}
if ($badDocNames -eq 0) { Write-Host "  ✅ All documentation files use valid kebab-case naming." -ForegroundColor Green }

# 2. Canonical MIT Copyright Header
Write-Host "`n[2/8] Checking canonical MIT copyright headers..." -ForegroundColor Yellow
$missingHeaders = 0

$allCsFiles = Get-ChildItem -Path $RootDirectory -Recurse -Filter "*.cs" | Where-Object { $_.FullName -notmatch "[\\/](obj|bin)[\\/]" }
foreach ($cs in $allCsFiles) {
    $firstLine = (Get-Content $cs.FullName -TotalCount 1 -Encoding UTF8)
    if ($firstLine -notmatch "Copyright .* Erickson Lopez.* MIT License") {
        Write-Host "  ❌ Missing MIT header in $($cs.FullName)" -ForegroundColor Red
        $violations++
        $missingHeaders++
    }
}

$allXmlFiles = Get-ChildItem -Path $RootDirectory -Recurse -Include "*.csproj", "*.props", "*.targets", "*.slnx" | Where-Object { $_.FullName -notmatch "[\\/](obj|bin)[\\/]" }
foreach ($proj in $allXmlFiles) {
    $firstLine = (Get-Content $proj.FullName -TotalCount 1 -Encoding UTF8)
    if ($firstLine -notmatch "Copyright .* Erickson Lopez.* MIT License") {
        Write-Host "  ❌ Missing MIT XML header in $($proj.FullName)" -ForegroundColor Red
        $violations++
        $missingHeaders++
    }
}

$configFiles = @(
    (Join-Path $RootDirectory ".editorconfig"),
    (Join-Path $RootDirectory ".codecov.yml"),
    (Join-Path $RootDirectory ".github/CODEOWNERS")
)
$ghFiles = Get-ChildItem -Path (Join-Path $RootDirectory ".github") -Recurse -Include "*.yml", "*.yaml" -ErrorAction SilentlyContinue
$scriptFiles = Get-ChildItem -Path (Join-Path $RootDirectory "scripts") -Recurse -Include "*.ps1", "*.js" -ErrorAction SilentlyContinue
$extraFiles = @($configFiles) + @($ghFiles) + @($scriptFiles)

foreach ($extra in $extraFiles) {
    if (Test-Path $extra) {
        $firstLine = (Get-Content $extra -TotalCount 1 -Encoding UTF8)
        if ($firstLine -notmatch "Copyright .* Erickson Lopez.* MIT License") {
            Write-Host "  ❌ Missing MIT header in $extra" -ForegroundColor Red
            $violations++
            $missingHeaders++
        }
    }
}

if ($missingHeaders -eq 0) { Write-Host "  ✅ All source, project, script, and CI configuration files contain the canonical MIT header." -ForegroundColor Green }

# 3. One Type Per File Governance (src/)
Write-Host "`n[3/9] Checking One Type Per File rule across src/..." -ForegroundColor Yellow
$srcCsFiles = Get-ChildItem -Path (Join-Path $RootDirectory "src") -Recurse -Filter "*.cs" | Where-Object { $_.FullName -notmatch "[\\/](obj|bin)[\\/]" }
$multiTypeFiles = 0
foreach ($cs in $srcCsFiles) {
    $lines = Get-Content $cs.FullName
    $typeCount = 0
    foreach ($line in $lines) {
        if ($line -match "^\s*(public|internal|protected)\s+(sealed\s+|abstract\s+|static\s+|readonly\s+)*(class|interface|struct|record|enum|delegate)\s+([A-Za-z0-9_]+)") {
            $typeCount++
        }
    }
    if ($typeCount -gt 1) {
        Write-Host "  ❌ Multiple top-level types ($typeCount) found in: $($cs.FullName)" -ForegroundColor Red
        $violations++
        $multiTypeFiles++
    }
}
if ($multiTypeFiles -eq 0) { Write-Host "  ✅ 100% compliance with One Type Per File across src/." -ForegroundColor Green }

# 4. Zero Obsolete APIs in solution
Write-Host "`n[4/9] Checking for [Obsolete] attribute usages in solution..." -ForegroundColor Yellow
$obsoleteCount = 0
foreach ($cs in $allCsFiles) {
    $lines = Get-Content $cs.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "^\s*\[Obsolete\b" -and $lines[$i] -notmatch "^\s*//") {
            Write-Host "  ❌ [Obsolete] found in $($cs.FullName):$($i + 1)" -ForegroundColor Red
            $violations++
            $obsoleteCount++
        }
    }
}
if ($obsoleteCount -eq 0) { Write-Host "  ✅ Zero [Obsolete] attributes in codebase." -ForegroundColor Green }

# 5. Official Contact & Support Email Normalization
Write-Host "`n[5/9] Checking contact and security email normalization (ericksonlopezf@gmail.com)..." -ForegroundColor Yellow
$badEmails = 0
$metaFiles = @("SECURITY.md", "CODE_OF_CONDUCT.md", "SUPPORT.md", "CONTRIBUTING.md")
foreach ($meta in $metaFiles) {
    $fullPath = Join-Path $RootDirectory $meta
    if (Test-Path $fullPath) {
        $lines = Get-Content $fullPath
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "(security@ericksonlopez\.dev|support@ericksonlopez\.dev|dev@ericksonlopez\.dev|ericksonlopez\.dev@gmail\.com)") {
                Write-Host "  ❌ Legacy email detected in $meta : line $($i + 1)" -ForegroundColor Red
                $violations++
                $badEmails++
            }
        }
    }
}
if ($badEmails -eq 0) { Write-Host "  ✅ Official contact emails normalized to ericksonlopezf@gmail.com." -ForegroundColor Green }

# 6. Global Build Settings & Packaging Invariants
Write-Host "`n[6/9] Checking Directory.Build.props settings & packaging invariants..." -ForegroundColor Yellow
$propsPath = Join-Path $RootDirectory "Directory.Build.props"
$iconPath = Join-Path $RootDirectory "icon.png"
$propsContent = Get-Content -Raw $propsPath

if (-not (Test-Path $iconPath)) {
    Write-Host "  ❌ icon.png missing from root directory." -ForegroundColor Red
    $violations++
}
if ($propsContent -notmatch "<ImplicitUsings>disable</ImplicitUsings>") {
    Write-Host "  ❌ Directory.Build.props must specify <ImplicitUsings>disable</ImplicitUsings>." -ForegroundColor Red
    $violations++
}
if ($propsContent -notmatch "<PackageProjectUrl>https://ericksonlopez\.dev/distributedlock</PackageProjectUrl>") {
    Write-Host "  ❌ PackageProjectUrl must be set to https://ericksonlopez.dev/distributedlock." -ForegroundColor Red
    $violations++
}
Write-Host "  ✅ Directory.Build.props invariants and icon.png verified." -ForegroundColor Green

# 7. NoWarn Governance Audit (Zero Exceptions for Mandatory Analyzers)
Write-Host "`n[7/9] Checking NoWarn suppressions in props and csproj files..." -ForegroundColor Yellow
$illegalNoWarn = 0
foreach ($proj in $allXmlFiles) {
    if ($proj.Extension -eq ".slnx") { continue }
    $lines = Get-Content $proj.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "<NoWarn>.*(CS0618|CS0619|CS1591|CS159|CS0159|CA1707|CA1852|CA1305|IDE1006|xUnit1051|SYSLIB0057).*</NoWarn>") {
            Write-Host "  ❌ Prohibited warning suppression in $($proj.FullName):$($i + 1)" -ForegroundColor Red
            $violations++
            $illegalNoWarn++
        }
    }
}
if ($illegalNoWarn -eq 0) { Write-Host "  ✅ Zero prohibited NoWarn suppressions found (zero bypasses)." -ForegroundColor Green }

# 8. Markdown Internal Link Verification
Write-Host "`n[8/9] Checking Markdown internal links..." -ForegroundColor Yellow
$brokenLinks = 0
$allMdFiles = Get-ChildItem -Path $RootDirectory -Recurse -Filter "*.md" | Where-Object { $_.FullName -notmatch "[\\/](obj|bin|\.git)[\\/]" }
foreach ($md in $allMdFiles) {
    $content = Get-Content $md.FullName -Raw
    $links = [System.Text.RegularExpressions.Regex]::Matches($content, '\[([^\]]+)\]\(([^)]+)\)')
    foreach ($match in $links) {
        $target = $match.Groups[2].Value.Trim()
        if ($target -match '^(https?://|mailto:|#)') { continue }
        $targetPath = $target.Split('#')[0]
        if ([string]::IsNullOrWhiteSpace($targetPath)) { continue }
        
        $resolved = Join-Path (Split-Path $md.FullName) $targetPath
        if (-not (Test-Path $resolved)) {
            Write-Host "  ❌ Broken link in $($md.FullName): '$target'" -ForegroundColor Red
            $violations++
            $brokenLinks++
        }
    }
}
if ($brokenLinks -eq 0) { Write-Host "  ✅ All internal Markdown links verified and resolve correctly." -ForegroundColor Green }

# 9. Technical English Consistency Verification (Zero Spanish Accents)
Write-Host "`n[9/9] Checking technical English language consistency..." -ForegroundColor Yellow
$spanishErrors = 0
$checkFiles = Get-ChildItem -Path $RootDirectory -Recurse -Include "*.cs", "*.md" | Where-Object {
    $_.FullName -notmatch "[\\/](obj|bin|\.git|coveragereport|StrykerOutput|MEGA-AUDITORIA)[\\/]"
}
$spanishPattern = [System.Text.RegularExpressions.Regex]::new("[\u00e1\u00e9\u00ed\u00f3\u00fa\u00c1\u00c9\u00cd\u00d3\u00da\u00f1\u00d1\u00bf\u00a1]", [System.Text.RegularExpressions.RegexOptions]::Compiled)

foreach ($f in $checkFiles) {
    $lines = Get-Content $f.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($spanishPattern.IsMatch($lines[$i])) {
            Write-Host "  ❌ Spanish text detected in $($f.FullName):$($i + 1) -> $($lines[$i].Trim())" -ForegroundColor Red
            $violations++
            $spanishErrors++
            break
        }
    }
}
if ($spanishErrors -eq 0) { Write-Host "  ✅ Technical English verified across 100% of solution source and documentation." -ForegroundColor Green }

# -----------------------------------------------------------------------------
# Stryker.NET Configuration, Concurrency, Anti-Gaming Blacklist & Matrix Synchronization
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Stryker] Validating Stryker.NET configuration, concurrency, anti-gaming blacklist & package matrix..." -ForegroundColor Yellow
$strykerErrors = 0
$targetRoot = if (Get-Variable -Name "RootDirectory" -Scope 0 -ErrorAction SilentlyContinue) { $RootDirectory } elseif (Get-Variable -Name "WorkspaceRoot" -Scope 0 -ErrorAction SilentlyContinue) { $WorkspaceRoot } elseif (Get-Variable -Name "repoRoot" -Scope 0 -ErrorAction SilentlyContinue) { $repoRoot } elseif (Get-Variable -Name "RepoRoot" -Scope 0 -ErrorAction SilentlyContinue) { $RepoRoot } else { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path }

$strykerConfigFiles = Get-ChildItem -Path $targetRoot -Recurse -Filter "stryker*.json" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|StrykerOutput|BenchmarkDotNet\.Artifacts|node_modules)[\\/]' -and
    $_.Name -ne "stryker-config.master.json"
}

if (-not $strykerConfigFiles -or $strykerConfigFiles.Count -eq 0) {
    Write-Host "  ❌ Zero Stryker configuration files found in repository." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Zero Stryker configuration files found.") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $strykerErrors++
} else {
    foreach ($sf in $strykerConfigFiles) {
        $json = Get-Content $sf.FullName -Raw | ConvertFrom-Json
        $cfg = if ($json.PSObject.Properties['stryker-config']) { $json.'stryker-config' } else { $json }

        if ($cfg.PSObject.Properties['thresholds']) {
            $th = $cfg.thresholds
            if ($th.high -ne 100 -or $th.low -ne 98 -or $th.break -ne 95) {
                Write-Host "  ❌ Non-compliant mutation thresholds in $($sf.FullName): high=$($th.high), low=$($th.low), break=$($th.break). Required: high=100, low=98, break=95." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Non-compliant mutation thresholds in $($sf.FullName)") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }

        if ($cfg.PSObject.Properties['concurrency']) {
            if ($cfg.concurrency -ne 2) {
                Write-Host "  ❌ Non-compliant Stryker concurrency in $($sf.FullName): $($cfg.concurrency). Required: 2." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Non-compliant Stryker concurrency in $($sf.FullName)") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }

        if ($cfg.PSObject.Properties['ignore-methods'] -and $cfg.'ignore-methods') {
            foreach ($m in $cfg.'ignore-methods') {
                if ($m -match 'ThrowIf|Exception|Guard|ScrubEphemeralMemory') {
                    Write-Host "  ❌ Prohibited anti-gaming method exclusion '$m' detected in $($sf.FullName)." -ForegroundColor Red
                    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Prohibited anti-gaming exclusion '$m' in $($sf.FullName)") } else { $Violations++ } }
                    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                    $strykerErrors++
                }
            }
        }
    }

    $strykerProfiles = Get-ChildItem -Path $targetRoot -Recurse -Filter "stryker*.json" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|StrykerOutput|BenchmarkDotNet\.Artifacts|node_modules)[\\/]' -and
        $_.Name -ne "stryker-config.master.json" -and
        ($_.Name -match '^stryker(-.+)?-config\.json$' -or $_.Name -eq "stryker-config.json")
    }

    $srcProjects = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }

    # Verify exact 1:1 count parity between Stryker profile configs and src/ projects
    if ($strykerProfiles.Count -ne $srcProjects.Count) {
        Write-Host "  ❌ Stryker profile count ($($strykerProfiles.Count)) does not match exactly the number of projects in src/ ($($srcProjects.Count))." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker profile count ($($strykerProfiles.Count)) does not match project count in src/ ($($srcProjects.Count)).") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $strykerErrors++
    }

    foreach ($proj in $srcProjects) {
        $projName = $proj.Name
        $matched = $false
        foreach ($sf in $strykerProfiles) {
            $raw = Get-Content $sf.FullName -Raw
            if ($raw -match [regex]::Escape($projName) -or $sf.Name -match [regex]::Escape($proj.BaseName)) {
                $matched = $true
                break
            }
        }

        if (-not $matched) {
            Write-Host "  ❌ Project '$projName' has no corresponding Stryker configuration profile." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' has no corresponding Stryker configuration profile.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
    }

    foreach ($sf in $strykerProfiles) {
        $raw = Get-Content $sf.FullName -Raw
        $matchedProj = $false
        foreach ($proj in $srcProjects) {
            if ($raw -match [regex]::Escape($proj.Name) -or $sf.Name -match [regex]::Escape($proj.BaseName)) {
                $matchedProj = $true
                break
            }
        }
        if (-not $matchedProj) {
            Write-Host "  ❌ Stryker profile '$($sf.Name)' does not correspond to any project in src/ (orphaned profile)." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker profile '$($sf.Name)' does not correspond to any project in src/.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
    }

    $mutationWfPath = Join-Path $targetRoot ".github/workflows/mutation-testing.yml"
    if (-not (Test-Path $mutationWfPath)) {
        Write-Host "  ❌ Missing .github/workflows/mutation-testing.yml" -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing .github/workflows/mutation-testing.yml") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $strykerErrors++
    } else {
        $wfContent = Get-Content $mutationWfPath -Raw
        if ($wfContent -match '--concurrency\s*[:\s]\s*([3-9]|\d{2,})') {
            Write-Host "  ❌ Mutation workflow overrides concurrency with value > 2 in CLI arguments." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Mutation workflow overrides concurrency > 2") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }
        if ($wfContent -match '--break-at\s*[:\s]\s*([0-8]\d|\d{1})(?!\d)') {
            Write-Host "  ❌ Mutation workflow overrides break threshold with value < 90 in CLI arguments." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Mutation workflow overrides break threshold < 90") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $strykerErrors++
        }

        foreach ($sf in $strykerConfigFiles) {
            if ($sf.Name -eq "stryker-config.json" -and $strykerConfigFiles.Count -gt 1) {
                continue
            }
            if ($sf.Name -eq "stryker-config-unit.json") {
                continue
            }
            $pkgIdent = if ($sf.Name -match '^stryker-(.+)-config\.json$') { $Matches[1] } else { $sf.Name }
            if ($wfContent -notmatch [regex]::Escape($sf.Name) -and $wfContent -notmatch "(?i)name:\s*$pkgIdent" -and $wfContent -notmatch "(?i)working-dir:.*$pkgIdent") {
                Write-Host "  ❌ Stryker configuration '$($sf.Name)' is missing from .github/workflows/mutation-testing.yml matrix." -ForegroundColor Red
                if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
                if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Stryker configuration '$($sf.Name)' is missing from matrix") } else { $Violations++ } }
                if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
                $strykerErrors++
            }
        }
    }
}

if ($strykerErrors -eq 0) {
    Write-Host "  ✅ Stryker.NET configuration, 100/98/95 thresholds, concurrency 2, anti-gaming blacklist, and package matrix synchronization verified." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Mutation Testing Release Gate Scripts, Workflow Gate & docs/testing-roadmap.md
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Release Gate] Validating mutation release gate scripts, workflow enforcement & docs/testing-roadmap.md..." -ForegroundColor Yellow
$gateErrors = 0

$gateScriptPath = Join-Path $targetRoot "scripts/verify-mutation-gate.js"
if (-not (Test-Path $gateScriptPath)) {
    Write-Host "  ❌ Missing scripts/verify-mutation-gate.js release gate script." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-mutation-gate.js") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$gateTestPath = Join-Path $targetRoot "scripts/verify-mutation-gate.test.js"
if (-not (Test-Path $gateTestPath)) {
    Write-Host "  ❌ Missing scripts/verify-mutation-gate.test.js unit tests." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-mutation-gate.test.js") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$roadmapPath = Join-Path $targetRoot "docs/testing-roadmap.md"
if (-not (Test-Path $roadmapPath)) {
    Write-Host "  ❌ Missing docs/testing-roadmap.md governance document." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing docs/testing-roadmap.md") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $gateErrors++
}

$publishWfPath = Join-Path $targetRoot ".github/workflows/publish.yml"
if (Test-Path $publishWfPath) {
    $pubContent = Get-Content $publishWfPath -Raw
    if ($pubContent -notmatch "verify-mutation-gate\.js") {
        Write-Host "  ❌ .github/workflows/publish.yml does not enforce verify-mutation-gate.js before publishing." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("publish.yml does not enforce verify-mutation-gate.js") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $gateErrors++
    }
}

if ($gateErrors -eq 0) {
    Write-Host "  ✅ Mutation release gate scripts, publish pipeline gate, and docs/testing-roadmap.md verified." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Benchmark Regression Quality Gate & CI Enforcement
# -----------------------------------------------------------------------------
$hasBenchProject = (Get-ChildItem -Path $targetRoot -Filter "*Benchmark*.csproj" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '[\\/](obj|bin|MEGA-AUDITORIA|StrykerOutput)[\\/]' } | Select-Object -First 1) -ne $null
if ($hasBenchProject) {
    Write-Host "`n[Gate: Benchmark Gate] Validating benchmark regression scripts & workflow enforcement..." -ForegroundColor Yellow
    $benchGateErrors = 0

    $benchScriptPath = Join-Path $targetRoot "scripts/verify-benchmark-gate.ps1"
    if (-not (Test-Path $benchScriptPath)) {
        Write-Host "  ❌ Missing scripts/verify-benchmark-gate.ps1 regression assertion script." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-benchmark-gate.ps1") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    $benchTestScriptPath = Join-Path $targetRoot "scripts/verify-benchmark-gate.test.ps1"
    if (-not (Test-Path $benchTestScriptPath)) {
        Write-Host "  ❌ Missing scripts/verify-benchmark-gate.test.ps1 unit tests." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing scripts/verify-benchmark-gate.test.ps1") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    $benchWfPath = Join-Path $targetRoot ".github/workflows/benchmark-regression-gate.yml"
    if (-not (Test-Path $benchWfPath)) {
        Write-Host "  ❌ Missing .github/workflows/benchmark-regression-gate.yml CI workflow." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing benchmark-regression-gate.yml") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    } else {
        $benchWfContent = Get-Content $benchWfPath -Raw -Encoding utf8
        if ($benchWfContent -notmatch "verify-benchmark-gate\.ps1" -or $benchWfContent -notmatch "--exporters json") {
            Write-Host "  ❌ .github/workflows/benchmark-regression-gate.yml does not enforce verify-benchmark-gate.ps1 and --exporters json." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Invalid benchmark-regression-gate.yml") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $benchGateErrors++
        }
    }

    $baselinePath = Join-Path $targetRoot "benchmarks/results/baseline.json"
    if (-not (Test-Path $baselinePath)) {
        Write-Host "  ❌ Missing benchmarks/results/baseline.json baseline file." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing benchmarks/results/baseline.json") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $benchGateErrors++
    }

    if ($benchGateErrors -eq 0) {
        Write-Host "  ✅ Benchmark regression assertion script, baseline, and CI workflow verified." -ForegroundColor Green
    }
}

# -----------------------------------------------------------------------------
# SourceLink & Central Package Management Integration Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: SourceLink] Validating centralized Microsoft.SourceLink.GitHub integration..." -ForegroundColor Yellow
$sourceLinkErrors = 0

$pkgPropsPath = Join-Path $targetRoot "Directory.Packages.props"
$bldPropsPath = Join-Path $targetRoot "Directory.Build.props"

if (-not (Test-Path $pkgPropsPath)) {
    Write-Host "  ❌ Missing Directory.Packages.props." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Directory.Packages.props") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $sourceLinkErrors++
} else {
    $pkgContent = Get-Content $pkgPropsPath -Raw -Encoding utf8
    if ($pkgContent -notmatch 'PackageVersion\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Packages.props must declare 'Microsoft.SourceLink.GitHub' instead of generic or missing package." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Packages.props missing Microsoft.SourceLink.GitHub") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
    if ($pkgContent -match 'PackageVersion\s+Include="Microsoft\.SourceLink\.Common"' -and $pkgContent -notmatch 'PackageVersion\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Packages.props uses generic Microsoft.SourceLink.Common without GitHub provider." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Generic Microsoft.SourceLink.Common used") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
}

if (-not (Test-Path $bldPropsPath)) {
    Write-Host "  ❌ Missing Directory.Build.props." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Directory.Build.props") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $sourceLinkErrors++
} else {
    $bldContent = Get-Content $bldPropsPath -Raw -Encoding utf8
    if ($bldContent -notmatch 'PackageReference\s+Include="Microsoft\.SourceLink\.GitHub"') {
        Write-Host "  ❌ Directory.Build.props must centralize '<PackageReference Include=""Microsoft.SourceLink.GitHub"" PrivateAssets=""All"" />'." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Build.props missing Microsoft.SourceLink.GitHub PackageReference") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
    if ($bldContent -notmatch '<PublishRepositoryUrl>\s*true\s*</PublishRepositoryUrl>' -and $bldContent -notmatch '<PublishRepositoryUrl\s+Condition=') {
        Write-Host "  ❌ Directory.Build.props must specify '<PublishRepositoryUrl>true</PublishRepositoryUrl>'." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Directory.Build.props missing PublishRepositoryUrl") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $sourceLinkErrors++
    }
}

if ($sourceLinkErrors -eq 0) {
    Write-Host "  ✅ SourceLink integration (Microsoft.SourceLink.GitHub) verified in Directory.Packages.props & Directory.Build.props." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# Native AOT Test Gate & Compilation Smoke Test Invariants
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Native AOT] Validating Native AOT compilation smoke tests & workflow enforcement..." -ForegroundColor Yellow
$aotErrors = 0

$allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}

# 1. Discover AOT-applicable projects in src/
$aotApplicableProjects = @()
foreach ($proj in $allSrcProjs) {
    $projName = $proj.Name
    $projDir = $proj.DirectoryName
    
    # Exclude Roslyn Analyzers and Source Generators
    if ($projName -match '(Analyzers?|Generators?)\.csproj$' -or $projDir -match '[\\/](Analyzers?|Generators?)[\\/]?$') {
        continue
    }
    # Exclude API endpoints / applications if applicable
    if ($projName -match '\.Api\.csproj$') {
        continue
    }
    
    $projContent = Get-Content $proj.FullName -Raw
    # Exclude projects explicitly marked as non-AOT compatible
    if ($projContent -match '<IsAotCompatible>\s*false\s*</IsAotCompatible>' -or 
        $projContent -match '<PublishAot>\s*false\s*</PublishAot>') {
        continue
    }
    
    $aotApplicableProjects += $proj
}

if ($aotApplicableProjects.Count -gt 0) {
    Write-Host "  [INFO] Detected $($aotApplicableProjects.Count) Native AOT applicable project(s) in src/." -ForegroundColor Gray
    
    # 2. Check for dedicated Native AOT smoke test project in tests/ or samples/
    $aotTestProjects = @()
    foreach ($searchDir in @("tests", "samples")) {
        $dirPath = Join-Path $targetRoot $searchDir
        if (Test-Path $dirPath) {
            $candidateTests = Get-ChildItem -Path $dirPath -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
            }
            foreach ($t in $candidateTests) {
                $content = Get-Content $t.FullName -Raw
                if ($content -match '<PublishAot>\s*true\s*</PublishAot>' -or $t.Name -match 'AotSmokeTest|AotTest|NativeAot') {
                    $aotTestProjects += $t
                }
            }
        }
    }
    
    if ($aotTestProjects.Count -eq 0) {
        Write-Host "  ❌ Missing Native AOT smoke test project in tests/ or samples/ for $($aotApplicableProjects.Count) AOT-applicable project(s)." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing Native AOT smoke test project in tests/ or samples/.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $aotErrors++
    } else {
        $hasValidExecutable = $false
        foreach ($aotProj in $aotTestProjects) {
            $aotContent = Get-Content $aotProj.FullName -Raw
            if ($aotContent -match '<OutputType>\s*Exe\s*</OutputType>' -and ($aotContent -match '<PublishAot>\s*true\s*</PublishAot>' -or $aotContent -match 'PublishAot')) {
                $hasValidExecutable = $true
                break
            }
        }
        if (-not $hasValidExecutable) {
            Write-Host "  ❌ At least one AOT smoke test project must declare OutputType=Exe and PublishAot=true." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("AOT smoke test project must declare OutputType=Exe and PublishAot=true.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $aotErrors++
        }
    }
    
    # 3. Check for CI workflow .github/workflows/aot-smoke-test.yml
    $aotWorkflowPath = Join-Path $targetRoot ".github/workflows/aot-smoke-test.yml"
    if (-not (Test-Path $aotWorkflowPath)) {
        Write-Host "  ❌ Missing .github/workflows/aot-smoke-test.yml CI workflow." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing .github/workflows/aot-smoke-test.yml CI workflow.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $aotErrors++
    } else {
        $wfContent = Get-Content $aotWorkflowPath -Raw
        if ($wfContent -notmatch 'dotnet publish' -or ($wfContent -notmatch 'linux-x64|win-x64' -and $wfContent -notmatch 'PublishAot')) {
            Write-Host "  ❌ Workflow .github/workflows/aot-smoke-test.yml does not execute a valid Native AOT publish step." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Invalid aot-smoke-test.yml workflow.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $aotErrors++
        }
    }
} else {
    Write-Host "  [INFO] Zero Native AOT applicable projects in src/ (pure analyzer/generator repository). Native AOT test gate skipped." -ForegroundColor Gray
}

if ($aotErrors -eq 0) {
    Write-Host "  ✅ Native AOT test project(s) and CI workflow verified." -ForegroundColor Green
}


# -----------------------------------------------------------------------------
# README Package Table Parity Gate
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: README Package Table Parity] Validating documentation package table synchronization..." -ForegroundColor Yellow
$readmeErrors = 0
$readmePath = Join-Path $targetRoot "README.md"

if (-not (Test-Path $readmePath)) {
    Write-Host "  ❌ Missing README.md in repository root." -ForegroundColor Red
    if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
    if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Missing README.md in repository root.") } else { $Violations++ } }
    if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
    $readmeErrors++
} else {
    $readmeContent = Get-Content $readmePath -Raw -Encoding utf8
    $allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }

    foreach ($proj in $allSrcProjs) {
        $projName = $proj.Name
        $baseName = $proj.BaseName
        
        # Check if project appears in README.md inside a table or package reference
        $escapedBase = [regex]::Escape($baseName)
        $isDocumented = ($readmeContent -match ('\|\s*`?' + $escapedBase + '`?\s*\|')) -or 
                        ($readmeContent -match ('\[`?' + $escapedBase + '`?\]')) -or
                        ($readmeContent -match "/packages/$escapedBase") -or
                        ($readmeContent -match ('\|\s*\[`?' + $escapedBase + '`?\]'))

        if (-not $isDocumented) {
            Write-Host "  ❌ Project '$projName' is missing from the packages table in README.md." -ForegroundColor Red
            if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
            if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' is missing from the packages table in README.md.") } else { $Violations++ } }
            if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
            $readmeErrors++
        }
    }

    if ($readmeErrors -eq 0) {
        Write-Host "  ✅ All $($allSrcProjs.Count) project(s) in src/ verified in README.md package table." -ForegroundColor Green
    }
}

# -----------------------------------------------------------------------------
# Test Suite Symmetry & Coverage Gate (Principle 12)
# -----------------------------------------------------------------------------
Write-Host "`n[Gate: Test Suite Symmetry] Validating test project symmetry & project references across tests/..." -ForegroundColor Yellow
$testSymErrors = 0
$testsDir = Join-Path $targetRoot "tests"

$allSrcProjs = Get-ChildItem -Path (Join-Path $targetRoot "src") -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}

$allTestProjs = @()
$testProjectReferences = @{}
if (Test-Path $testsDir) {
    $allTestProjs = Get-ChildItem -Path $testsDir -Recurse -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }
    foreach ($tp in $allTestProjs) {
        $tContent = Get-Content $tp.FullName -Raw -Encoding utf8
        $refs = [regex]::Matches($tContent, '<ProjectReference\s+Include="([^"]+)"')
        foreach ($m in $refs) {
            $refFile = Split-Path $m.Groups[1].Value.Replace('\', '/') -Leaf
            $testProjectReferences[$refFile] = $true
        }
    }
}

foreach ($proj in $allSrcProjs) {
    $projName = $proj.Name
    $baseName = $proj.BaseName
    
    # Check 1: Named test suite matching base name
    $hasNamedTest = ($allTestProjs | Where-Object { $_.BaseName -match "^$([regex]::Escape($baseName))(\..+)?Tests?$" -or $_.BaseName -like "*$baseName*" }) -ne $null
    
    # Check 2: Direct ProjectReference in any test project
    $hasReference = $testProjectReferences.ContainsKey($projName)
    
    if (-not $hasNamedTest -and -not $hasReference) {
        Write-Host "  ❌ Project '$projName' has no corresponding test suite in tests/ (missing test project or ProjectReference)." -ForegroundColor Red
        if (Get-Variable -Name "violations" -Scope 0 -ErrorAction SilentlyContinue) { $violations++ }
        if (Get-Variable -Name "Violations" -Scope 0 -ErrorAction SilentlyContinue) { if ($Violations -is [System.Collections.IList]) { $Violations.Add("Project '$projName' has no corresponding test suite in tests/.") } else { $Violations++ } }
        if (Get-Variable -Name "FailedChecks" -Scope 0 -ErrorAction SilentlyContinue) { $FailedChecks++ }
        $testSymErrors++
    }
}

if ($testSymErrors -eq 0) {
    Write-Host "  ✅ All $($allSrcProjs.Count) project(s) in src/ verified with corresponding test suite in tests/." -ForegroundColor Green
}


# Summary & Exit Code
Write-Host "`n==================================================" -ForegroundColor Cyan
if ($violations -gt 0) {
    Write-Host "  FAILED: $violations compliance violation(s) detected. " -ForegroundColor Red -BackgroundColor Black
    Write-Host "==================================================" -ForegroundColor Cyan
    exit 1
} else {
    Write-Host "  SUCCESS: 100% Governance & Compliance Verified. Zero violations. " -ForegroundColor Green -BackgroundColor Black
    Write-Host "==================================================" -ForegroundColor Cyan
    exit 0
}

