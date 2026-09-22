# Contributing to EricksonLopez.DistributedLock

Thank you for your interest in contributing to **EricksonLopez.DistributedLock**! This project is an enterprise-grade, high-performance distributed locking framework for .NET 8, .NET 9, and .NET 10 with 100% Native AOT compatibility and zero-allocation error handling.

To maintain professional software quality and long-term maintainability, all contributions must adhere to the standards outlined below.

---

## Code of Conduct

All contributors and maintainers must abide by our [Code of Conduct](CODE_OF_CONDUCT.md). Please report any unacceptable behavior to [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com).

---

## Architectural Invariants & Quality Standards

Every pull request must preserve the following non-negotiable architectural invariants:

1. **Native AOT Compatibility (`IsAotCompatible=true`)**:
   - Zero trimmer warnings (`IL2026`, `IL3050`).
   - No runtime reflection or dynamic IL emission in hot paths.
   - All concrete provider classes and lock handles must be `sealed` to facilitate devirtualization.
2. **Zero-Allocation Error Handling (Result Pattern)**:
   - Contention, timeout, or lock loss must return typed `Result<T>` or `Result<IAsyncDisposable>` without throwing exceptions.
3. **One Type Per File**:
   - Every C# file must declare at most one top-level type whose name matches the filename.
4. **Explicit Usings (`ImplicitUsings=disable`)**:
   - Solution-wide `ImplicitUsings` is disabled. All namespaces must be explicitly imported to guarantee clarity, deterministic compilation, and full isolation.
5. **MIT License Header**:
   - Every `.cs` file must begin with:
     ```csharp
     // Copyright © Erickson Lopez. MIT License.
     ```
6. **Zero `[Obsolete]` Policy**:
   - No `[Obsolete]` attributes or deprecated API calls are permitted in the codebase.
7. **Document Naming Convention (kebab-case)**:
   - All markdown documentation files must use lowercase kebab-case (e.g., `functional-parity-audit.md`, `adr-001-distributed-lock-abstraction.md`), with the sole exception of standard tooling files (`README.md`, `LICENSE`, `SECURITY.md`, `SUPPORT.md`, `CONTRIBUTING.md`, `CHANGELOG.md`, `CODE_OF_CONDUCT.md`, `PULL_REQUEST_TEMPLATE.md`).
8. **English-First Documentation & Code**:
   - All comments, XML documentation, diagnostic messages, ADRs, and documentation must be written in clear technical English.

---

## Local Development Workflow

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or .NET 8/9 SDK)
- [PowerShell 7+](https://github.com/PowerShell/PowerShell)
- [Docker](https://www.docker.com/) (required for integration tests using Testcontainers)

### Build and Test

```bash
# Restore dependencies
dotnet restore EricksonLopez.DistributedLock.slnx

# Compile with warnings as errors and full documentation generation
dotnet build EricksonLopez.DistributedLock.slnx -c Release /warnaserror

# Execute all unit and architecture tests
dotnet test EricksonLopez.DistributedLock.slnx -c Release

# Verify formatting and analyzer conventions
dotnet format --verify-no-changes
```

### Mutation Testing with Stryker.NET

Before submitting major modifications to providers or abstractions, audit mutation resistance:

```bash
cd src/EricksonLopez.DistributedLock.PostgreSql
dotnet-stryker --config-file stryker-config.json
```

---

## Submitting Pull Requests

1. **Fork the repository** and create a feature branch (`feature/your-feature-name` or `fix/issue-description`).
2. Implement your changes adhering to the architectural standards.
3. Add or update unit tests to ensure 100% test symmetry and full branch coverage.
4. Run `dotnet test` and `dotnet format` locally.
5. Submit a pull request against `main`. All CI checks and automated architectural tests must pass before review.

---

## Questions & Support

For architectural inquiries, discussions, or assistance, contact:
- Maintainer: **Erickson Lopez**
- Email: [ericksonlopezf@gmail.com](mailto:ericksonlopezf@gmail.com)
- GitHub Issues: [Issue Tracker](https://github.com/ericksonlopezf/dotnet-distributedlock/issues)
