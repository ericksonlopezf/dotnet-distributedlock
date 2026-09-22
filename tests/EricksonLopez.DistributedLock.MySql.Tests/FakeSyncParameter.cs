// Copyright © Erickson Lopez. MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.DistributedLock.MySql.Tests;

public sealed class FakeSyncParameter : IDbDataParameter
{
    public DbType DbType { get; set; }
    public ParameterDirection Direction { get; set; }
    public bool IsNullable { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public string SourceColumn { get; set; } = string.Empty;
    public DataRowVersion SourceVersion { get; set; }
    public object? Value { get; set; }
    public byte Precision { get; set; }
    public byte Scale { get; set; }
    public int Size { get; set; }
}

