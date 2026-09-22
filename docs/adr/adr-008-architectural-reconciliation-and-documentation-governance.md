# ADR-008: Architectural Reconciliation and Technical Documentation Governance

## Status
Accepted

## Date
2026-09-12

**Context**: Documentation Standardization, Kebab-Case Naming Governance, Technical English Normalization, and Community Health Architecture  
**Decision Makers**: Principal Software Architect, DevOps Architect, Technical Writer Senior  

---

[← Previous: ADR-007](adr-007-test-project-symmetry-and-architecture-rules.md) | [Index](README.md)

---

## 1. Context and Problem Statement

Following the multi-engine expansion and comprehensive forensic audit of `EricksonLopez.DistributedLock`, an audit of repository documentation revealed critical discrepancies:
1. **Naming Inconsistencies**: Documentation files in `/docs/` were created using uppercase naming (`API-REFERENCE.md`, `ARCHITECTURE.md`, `SHOWCASE.md`), directly violating repository architectural test `DistributedLockArchitectureTests.AllDocumentationFiles_MustFollow_KebabCaseNaming`.
2. **Language Fragmentation**: Core architectural and operational guides were authored in Spanish, conflicting with the repository's English-first invariant established in `CONTRIBUTING.md`.
3. **Broken Local Link References**: `docs/SHOWCASE.md` contained 21 hardcoded `file:///d:/DevData/...` absolute links which broke on external platforms, CI pipelines, and Linux environments.
4. **Community Health Gaps**: Standard OSS community files (`SUPPORT.md`, `.github/CODEOWNERS`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/ISSUE_TEMPLATE/`) were missing or unaligned with quality gates.

---

## 2. Decision

We formalize strict architectural and technical documentation governance across the repository:

### 2.1. Lowercase Kebab-Case File Naming in `/docs/`
All documentation files located in `/docs/` (including nested subdirectories such as `/docs/adr/`) must strictly adhere to lowercase `kebab-case.md` naming (matching `^[a-z0-9]+(-[a-z0-9]+)*\.md$`). The only exceptions permitted are root-level standard tooling files (`README.md`, `LICENSE`, `SECURITY.md`, `SUPPORT.md`, `CONTRIBUTING.md`, `CHANGELOG.md`, `CODE_OF_CONDUCT.md`, `PULL_REQUEST_TEMPLATE.md`).

### 2.2. English-First Technical Documentation
All documentation files, code comments, XML docstrings, ADRs, and commit messages must be written exclusively in professional technical English.

### 2.3. Portable Relative Markdown Links
All internal cross-document links must use relative Markdown paths (e.g., `../samples/EricksonLopez.DistributedLock.Showcase/...` or `api-reference.md`). Absolute local file URIs (`file:///`) and hardcoded GitHub repository URLs for internal navigation are prohibited.

### 2.4. Whitelist Harmonization in Architecture Tests
`DistributedLockArchitectureTests.AllDocumentationFiles_MustFollow_KebabCaseNaming` is synchronized with `scripts/verify-compliance.ps1` to recognize `SUPPORT.md` and `PULL_REQUEST_TEMPLATE.md` as valid standard exceptions.

---

## 3. Consequences

### Positive
- **100% Architecture Test Compliance**: All tests in `EricksonLopez.DistributedLock.ArchitectureTests` pass without naming violations.
- **Cross-Platform Compatibility**: Relative links resolve correctly across GitHub, local clones, and Linux CI runners.
- **Global Maintainability**: English documentation provides a consistent onboarding experience for international developers and enterprise adopters.

### Neutral / Trade-offs
- Internal bookmarks or links to old uppercase files must be updated to the new `kebab-case.md` paths.
