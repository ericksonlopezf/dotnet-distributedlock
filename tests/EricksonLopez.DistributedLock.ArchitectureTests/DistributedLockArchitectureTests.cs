// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.MariaDb;
using EricksonLopez.DistributedLock.MySql;
using EricksonLopez.DistributedLock.Oracle;
using EricksonLopez.DistributedLock.PostgreSql;
using EricksonLopez.DistributedLock.Redis;
using EricksonLopez.DistributedLock.Sqlite;
using EricksonLopez.DistributedLock.SqlServer;
using NetArchTest.Rules;
using Xunit;

namespace EricksonLopez.DistributedLock.ArchitectureTests;

public sealed class DistributedLockArchitectureTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(IDistributedLockProvider).Assembly;
    private static readonly Assembly PostgreSqlAssembly = typeof(PostgresDistributedLockProvider).Assembly;
    private static readonly Assembly SqlServerAssembly = typeof(SqlServerDistributedLockProvider).Assembly;
    private static readonly Assembly MySqlAssembly = typeof(MySqlDistributedLockProvider).Assembly;
    private static readonly Assembly MariaDbAssembly = typeof(MariaDbDistributedLockProvider).Assembly;
    private static readonly Assembly OracleAssembly = typeof(OracleDistributedLockProvider).Assembly;
    private static readonly Assembly SqliteAssembly = typeof(SqliteDistributedLockProvider).Assembly;
    private static readonly Assembly RedisAssembly = typeof(RedisDistributedLockProvider).Assembly;

    private static readonly Assembly[] AllSourceAssemblies =
    [
        AbstractionsAssembly,
        PostgreSqlAssembly,
        SqlServerAssembly,
        MySqlAssembly,
        MariaDbAssembly,
        OracleAssembly,
        SqliteAssembly,
        RedisAssembly
    ];

    private static readonly (Assembly Assembly, string Name, string[] ForbiddenDependencies)[] DialectDependencies =
    [
        (PostgreSqlAssembly, "PostgreSql", ["SqlServer", "MySql", "MariaDb", "Oracle", "Sqlite", "Redis", "Microsoft.Data.SqlClient", "MySqlConnector", "Oracle.ManagedDataAccess", "Microsoft.Data.Sqlite", "StackExchange.Redis"]),
        (SqlServerAssembly, "SqlServer", ["PostgreSql", "MySql", "MariaDb", "Oracle", "Sqlite", "Redis", "Npgsql", "MySqlConnector", "Oracle.ManagedDataAccess", "Microsoft.Data.Sqlite", "StackExchange.Redis"]),
        (MySqlAssembly, "MySql", ["PostgreSql", "SqlServer", "MariaDb", "Oracle", "Sqlite", "Redis", "Npgsql", "Microsoft.Data.SqlClient", "Oracle.ManagedDataAccess", "Microsoft.Data.Sqlite", "StackExchange.Redis"]),
        (MariaDbAssembly, "MariaDb", ["PostgreSql", "SqlServer", "Oracle", "Sqlite", "Redis", "Npgsql", "Microsoft.Data.SqlClient", "Oracle.ManagedDataAccess", "Microsoft.Data.Sqlite", "StackExchange.Redis"]),
        (OracleAssembly, "Oracle", ["PostgreSql", "SqlServer", "MySql", "MariaDb", "Sqlite", "Redis", "Npgsql", "Microsoft.Data.SqlClient", "MySqlConnector", "Microsoft.Data.Sqlite", "StackExchange.Redis"]),
        (SqliteAssembly, "Sqlite", ["PostgreSql", "SqlServer", "MySql", "MariaDb", "Oracle", "Redis", "Npgsql", "Microsoft.Data.SqlClient", "MySqlConnector", "Oracle.ManagedDataAccess", "StackExchange.Redis"]),
        (RedisAssembly, "Redis", ["PostgreSql", "SqlServer", "MySql", "MariaDb", "Oracle", "Sqlite", "Npgsql", "Microsoft.Data.SqlClient", "MySqlConnector", "Oracle.ManagedDataAccess", "Microsoft.Data.Sqlite"])
    ];

    [Fact]
    public void Abstractions_ShouldNotDependOn_ConcreteDriversOrAdapters()
    {
        TestResult result = Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "Microsoft.Data.SqlClient",
                "MySqlConnector",
                "Oracle.ManagedDataAccess",
                "Microsoft.Data.Sqlite",
                "StackExchange.Redis",
                "Dapper",
                "EricksonLopez.DistributedLock.PostgreSql",
                "EricksonLopez.DistributedLock.SqlServer",
                "EricksonLopez.DistributedLock.MySql",
                "EricksonLopez.DistributedLock.MariaDb",
                "EricksonLopez.DistributedLock.Oracle",
                "EricksonLopez.DistributedLock.Sqlite",
                "EricksonLopez.DistributedLock.Redis")
            .GetResult();

        result.IsSuccessful.Should().BeTrue("Abstractions assembly must remain pure without concrete driver or adapter dependencies.");
    }

    [Fact]
    public void DialectAdapters_ShouldNotDependOn_OtherDialectsOrForeignDrivers()
    {
        foreach (var (assembly, name, forbidden) in DialectDependencies)
        {
            List<string> forbiddenNamespacePrefixes = forbidden
                .Select(f => f.Contains('.') ? f : $"EricksonLopez.DistributedLock.{f}")
                .ToList();

            TestResult result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(forbiddenNamespacePrefixes.ToArray())
                .GetResult();

            result.IsSuccessful.Should().BeTrue($"Dialect adapter {name} must not depend on foreign dialects or drivers: {string.Join(", ", forbidden)}");
        }
    }

    [Fact]
    public void AllAssemblies_ShouldHaveStandardEricksonLopezPrefixes()
    {
        AbstractionsAssembly.GetName().Name.Should().Be("EricksonLopez.DistributedLock.Abstractions");
        PostgreSqlAssembly.GetName().Name.Should().Be("EricksonLopez.DistributedLock.PostgreSql");
        SqlServerAssembly.GetName().Name.Should().Be("EricksonLopez.DistributedLock.SqlServer");
        MySqlAssembly.GetName().Name.Should().Be("EricksonLopez.DistributedLock.MySql");
        MariaDbAssembly.GetName().Name.Should().Be("EricksonLopez.DistributedLock.MariaDb");
        OracleAssembly.GetName().Name.Should().Be("EricksonLopez.DistributedLock.Oracle");
        SqliteAssembly.GetName().Name.Should().Be("EricksonLopez.DistributedLock.Sqlite");
        RedisAssembly.GetName().Name.Should().Be("EricksonLopez.DistributedLock.Redis");
    }

    [Fact]
    public void AllConcreteProviders_MustImplement_IDistributedLockProvider_AndBeSealed()
    {
        Type providerInterface = typeof(IDistributedLockProvider);

        foreach (Assembly asm in AllSourceAssemblies.Where(a => a != AbstractionsAssembly))
        {
            var providerTypes = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && providerInterface.IsAssignableFrom(t))
                .ToList();

            providerTypes.Should().NotBeEmpty($"Assembly {asm.GetName().Name} must contain at least one concrete IDistributedLockProvider implementation.");

            foreach (Type providerType in providerTypes)
            {
                providerType.IsSealed.Should().BeTrue($"Provider class '{providerType.FullName}' in {asm.GetName().Name} must be sealed for Native AOT devirtualization.");
            }
        }
    }

    [Fact]
    public void AllConcreteHandles_MustImplement_IDistributedLockHandle_AndBeSealed()
    {
        Type handleInterface = typeof(IDistributedLockHandle);

        foreach (Assembly asm in AllSourceAssemblies.Where(a => a != AbstractionsAssembly))
        {
            var handleTypes = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && handleInterface.IsAssignableFrom(t))
                .ToList();

            handleTypes.Should().NotBeEmpty($"Assembly {asm.GetName().Name} must contain at least one concrete IDistributedLockHandle implementation.");

            foreach (Type handleType in handleTypes)
            {
                handleType.IsSealed.Should().BeTrue($"Handle class '{handleType.FullName}' in {asm.GetName().Name} must be sealed for Native AOT devirtualization.");
            }
        }
    }

    [Fact]
    public void AllLockOptions_MustFollowNamingConvention_AndBeSealed()
    {
        foreach (Assembly asm in AllSourceAssemblies.Where(a => a != AbstractionsAssembly))
        {
            var optionsTypes = asm.GetTypes()
                .Where(t => t.IsClass && t.Name.Contains("LockOptions", StringComparison.Ordinal))
                .ToList();

            optionsTypes.Should().NotBeEmpty($"Assembly {asm.GetName().Name} must contain lock options type.");

            foreach (Type opt in optionsTypes)
            {
                opt.Name.Should().EndWith("LockOptions", $"Options class '{opt.Name}' must end with LockOptions.");
                opt.IsSealed.Should().BeTrue($"Options class '{opt.Name}' must be sealed.");
            }
        }
    }

    [Fact]
    public void AllInterfaces_MustStartWithI()
    {
        foreach (Assembly asm in AllSourceAssemblies)
        {
            var interfaces = asm.GetTypes().Where(t => t.IsInterface).ToList();
            foreach (Type iface in interfaces)
            {
                iface.Name.Should().StartWith("I", $"Interface '{iface.Name}' in assembly '{asm.GetName().Name}' must start with 'I'.");
            }
        }
    }

    [Fact]
    public void AllSourceAssemblies_MustNotContainObsoleteTypesOrMembers()
    {
        foreach (Assembly asm in AllSourceAssemblies)
        {
            var obsoleteTypes = asm.GetTypes()
                .Where(t => t.GetCustomAttribute<ObsoleteAttribute>() is not null)
                .Select(t => t.FullName)
                .ToList();

            obsoleteTypes.Should().BeEmpty($"Assembly {asm.GetName().Name} contains [Obsolete] types: {string.Join(", ", obsoleteTypes)}");

            var obsoleteMembers = asm.GetTypes()
                .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() is not null)
                .Select(m => $"{m.DeclaringType?.Name}.{m.Name}")
                .ToList();

            obsoleteMembers.Should().BeEmpty($"Assembly {asm.GetName().Name} contains [Obsolete] members: {string.Join(", ", obsoleteMembers)}");
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root could not be located.");
    }

    [Fact]
    public void AllSourceFiles_MustHave_MitLicenseHeader()
    {
        string root = FindRepositoryRoot();
        var csFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        csFiles.Should().NotBeEmpty();

        var missing = new List<string>();
        foreach (string file in csFiles)
        {
            string firstLine = File.ReadLines(file).FirstOrDefault() ?? string.Empty;
            if (firstLine.Trim() != "// Copyright © Erickson Lopez. MIT License.")
            {
                missing.Add(Path.GetRelativePath(root, file));
            }
        }

        missing.Should().BeEmpty("all C# source and test files must begin with the standard MIT License header.");
    }

    [Fact]
    public void AllSourceFiles_MustHave_OneTopLevelTypePerFile()
    {
        string root = FindRepositoryRoot();
        var csFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var violations = new List<string>();
        foreach (string file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            var topTypes = new List<string>();
            foreach (string line in lines)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"^(public|internal)?\s*(sealed|abstract|static|readonly)?\s*(class|interface|struct|record|enum|delegate)\s+([A-Za-z0-9_]+)");
                if (match.Success)
                {
                    topTypes.Add(match.Groups[4].Value);
                }
            }

            if (topTypes.Count > 1)
            {
                violations.Add($"{Path.GetRelativePath(root, file)} contains [{string.Join(", ", topTypes)}]");
            }
        }

        violations.Should().BeEmpty("every C# file must declare at most one top-level type.");
    }

    [Fact]
    public void AllDocumentationFiles_MustFollow_KebabCaseNaming()
    {
        string root = FindRepositoryRoot();
        var mdFiles = Directory.GetFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}coveragereport", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}StrykerOutput", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}MEGA-AUDITORIA", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var standardExceptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "README.md",
            "LICENSE",
            "SECURITY.md",
            "SUPPORT.md",
            "CONTRIBUTING.md",
            "CHANGELOG.md",
            "CODE_OF_CONDUCT.md",
            "PULL_REQUEST_TEMPLATE.md"
        };

        var nonKebabCase = new List<string>();
        foreach (string file in mdFiles)
        {
            string fileName = Path.GetFileName(file);
            if (standardExceptions.Contains(fileName))
            {
                continue;
            }

            string nameWithoutExt = Path.GetFileNameWithoutExtension(file);
            bool isKebab = System.Text.RegularExpressions.Regex.IsMatch(nameWithoutExt, @"^[a-z0-9]+(-[a-z0-9]+)*$");
            if (!isKebab)
            {
                nonKebabCase.Add(Path.GetRelativePath(root, file));
            }
        }

        nonKebabCase.Should().BeEmpty("all documentation files outside standard tooling conventions must adhere to lowercase kebab-case naming.");
    }

    [Fact]
    public void Documentation_MustInclude_SupportEmail()
    {
        string root = FindRepositoryRoot();
        string[] requiredFiles = ["SECURITY.md", "CONTRIBUTING.md", "README.md"];

        foreach (string relativeFile in requiredFiles)
        {
            string fullPath = Path.Combine(root, relativeFile);
            File.Exists(fullPath).Should().BeTrue($"{relativeFile} must exist.");

            string text = File.ReadAllText(fullPath);
            text.Should().Contain("ericksonlopezf@gmail.com", $"{relativeFile} must provide the official maintainer support email.");
        }
    }

    [Fact]
    public void AllCodeAndDocumentation_MustBeInEnglish()
    {
        string root = FindRepositoryRoot();
        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}coveragereport", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}StrykerOutput", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}MEGA-AUDITORIA", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.EndsWith("DistributedLockArchitectureTests.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var spanishRegex = new System.Text.RegularExpressions.Regex(@"[\u00e1\u00e9\u00ed\u00f3\u00fa\u00c1\u00c9\u00cd\u00d3\u00da\u00f1\u00d1\u00bf\u00a1]", System.Text.RegularExpressions.RegexOptions.Compiled);
        var violations = new List<string>();

        foreach (string file in files)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (spanishRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{Path.GetRelativePath(root, file)}:{i + 1} -> {lines[i].Trim()}");
                    break;
                }
            }
        }

        violations.Should().BeEmpty("code, comments, XML docs, and markdown documentation must use consistent technical English without Spanish characters.");
    }

    [Fact]
    public void NoWarn_MustNotSuppressMandatoryRules()
    {
        string root = FindRepositoryRoot();
        var projectFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".props", StringComparison.OrdinalIgnoreCase)) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        string[] forbiddenSuppressedRules = ["IDE1006", "CA1707", "CA1852", "CA1305", "CS0619", "CS0618", "xUnit1051", "CS1591", "CS159", "CS0159"];
        var violations = new List<string>();

        foreach (string file in projectFiles)
        {
            string content = File.ReadAllText(file);
            var matches = System.Text.RegularExpressions.Regex.Matches(content, @"<NoWarn>(.*?)</NoWarn>", System.Text.RegularExpressions.RegexOptions.Singleline);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string noWarnVal = match.Groups[1].Value;
                foreach (string rule in forbiddenSuppressedRules)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(noWarnVal, $@"\b{rule}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        violations.Add($"{Path.GetRelativePath(root, file)} suppresses mandatory rule '{rule}' via <NoWarn>.");
                    }
                }
            }
        }

        violations.Should().BeEmpty("mandatory analyzers must never be silenced via <NoWarn> in project files.");
    }
}

