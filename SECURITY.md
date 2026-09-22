# Security Policy — EricksonLopez.DistributedLock

## Supported Versions

We provide security patches, CVE mitigations, and critical vulnerability updates for the following active target framework versions:

| Framework | Supported | Notes |
|:---:|:---:|---|
| .NET 10.0 | :white_check_mark: Yes | Current LTS Target |
| .NET 9.0 | :white_check_mark: Yes | Active STS Target |
| .NET 8.0 | :white_check_mark: Yes | Active LTS Target |
| < .NET 8.0 | :x: No | End-of-Life (EOL) by Microsoft |

---

## Reporting a Vulnerability

Security and integrity are paramount for foundational distributed systems infrastructure. If you identify a potential security vulnerability, privilege escalation vector, timing attack in lock state derivation, connection pool leakage, or race condition flaw within `EricksonLopez.DistributedLock`, please report it responsibly.

### Disclosure Guidelines

1. **Do not create a public issue or discussion** on GitHub.
2. Send an email with complete details to:
   - **Direct Contact**: [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com)
3. Please include:
   - Specific package name(s) and version(s) affected.
   - Detailed step-by-step reproduction steps or a minimal working proof of concept (PoC).
   - Expected vs. actual behavior and potential exploit vector impact.
   - Any proposed remediation or pull request patch.

### Response Timeline

- **Initial Acknowledgment**: Within 24 hours.
- **Triage and Impact Assessment**: Within 48 hours.
- **Remediation & Patch Release**: Within 7 business days for critical vulnerabilities, accompanied by a coordinated disclosure announcement and security advisory.

---

## Supply Chain Security

To ensure downstream consumers can trust binary artifacts published by this repository:

1. **Deterministic Strong Name Signing**: Releases are cryptographically signed using a Strong Name Key (`EricksonLopez.snk`) during CI/CD execution.
2. **Automated Publishing via GitHub Actions**: Packages are compiled and packed on clean GitHub-hosted runners under `/warnaserror` with embedded source links (`Microsoft.SourceLink.GitHub`).
3. **NuGet Gallery Publishing**: Official releases are pushed exclusively from GitHub Actions workflows to [NuGet.org](https://www.nuget.org/packages?q=EricksonLopez.DistributedLock) with `--skip-duplicate` protection.

---

## Known Security Boundaries

- **Session Advisory Locks & Connection Poolers**: Session-level locks (`pg_advisory_lock`) are bound to the backend database process (`backend_pid`). Multiplexing through connection poolers (PgBouncer, ProxySQL) in `pool_mode = transaction` is an insecure operational boundary that can leak or orphan locks. Always use transaction-bound locks (`TryAcquireInTransactionAsync`) or dedicated session-mode connection pools.
- **Resource Key Injections**: Resource identifier keys are mathematically converted into signed 64-bit integers via SHA-256 (`PostgresDistributedLockProvider.GenerateLockId`) or parameterized as database command parameters (`@LockId`, `@ResourceKey`), eliminating SQL injection vectors across all dialect providers.
