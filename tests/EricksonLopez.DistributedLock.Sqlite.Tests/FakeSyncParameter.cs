// Copyright © Erickson Lopez. MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.DistributedLock.Sqlite.Tests;

public sealed class FakeSyncParameter : IDbDataParameter
{
    public byte Precision { get; set; }
    public byte Scale { get; set; }
    public int Size { get; set; }
    public DbType DbType { get; set; } = DbType.String;
    public ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public bool IsNullable { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public string SourceColumn { get; set; } = string.Empty;
    public DataRowVersion SourceVersion { get; set; }
    public object? Value { get; set; }
}

