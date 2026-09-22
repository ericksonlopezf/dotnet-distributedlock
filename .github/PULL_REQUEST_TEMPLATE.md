## Pull Request Summary

### Description of Changes
<!-- Provide a concise description of the changes introduced by this pull request. -->

---

### Affected Packages
<!-- Select all packages modified by this PR: -->
- [ ] `EricksonLopez.DistributedLock.Abstractions`
- [ ] `EricksonLopez.DistributedLock.PostgreSql`
- [ ] `EricksonLopez.DistributedLock.SqlServer`
- [ ] `EricksonLopez.DistributedLock.MySql`
- [ ] `EricksonLopez.DistributedLock.MariaDb`
- [ ] `EricksonLopez.DistributedLock.Oracle`
- [ ] `EricksonLopez.DistributedLock.Sqlite`
- [ ] `EricksonLopez.DistributedLock.Redis`
- [ ] `EricksonLopez.DistributedLock.Showcase` (Sample)
- [ ] Documentation / CI / Infrastructure

---

### Quality & Architectural Verification Checklist
<!-- Ensure all requirements below are satisfied before requesting review: -->
- [ ] **Zero Warnings Policy**: `dotnet build EricksonLopez.DistributedLock.slnx -c Release /warnaserror` succeeds with 0 warnings and 0 errors.
- [ ] **Architectural Compliance**: `dotnet test tests/EricksonLopez.DistributedLock.ArchitectureTests/ -c Release` passes with 0 failures.
- [ ] **Native AOT Compatibility**: No reflection or trimmable dynamic IL in hot paths (`IsAotCompatible=true`).
- [ ] **Test Symmetry**: New or modified logic is matched by corresponding unit tests with $\ge 95\%$ coverage.
- [ ] **Mutation Resilience**: Stryker mutation score satisfies the $\ge 95\%$ break-at threshold (`mutation-testing.yml`).
- [ ] **Documentation Standards**: All markdown files adhere to lowercase `kebab-case.md` and are written in technical English.
- [ ] **Formatting**: Code adheres to `.editorconfig` rules (`dotnet format --verify-no-changes`).
- [ ] **Copyright Headers**: Canonical MIT license header present in all new files.
