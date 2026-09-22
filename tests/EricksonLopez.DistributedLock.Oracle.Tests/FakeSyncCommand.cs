// Copyright © Erickson Lopez. MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.DistributedLock.Oracle.Tests;

public sealed class FakeSyncCommand : IDbCommand
{
    public string CommandText { get; set; } = string.Empty;
    public int CommandTimeout { get; set; }
    public CommandType CommandType { get; set; }
    public IDbConnection? Connection { get; set; }
    public IDataParameterCollection Parameters { get; } = new FakeSyncParameterCollection();
    public IDbTransaction? Transaction { get; set; }
    public UpdateRowSource UpdatedRowSource { get; set; }

    public Func<FakeSyncCommand, object?>? ExecuteScalarHandler { get; set; }
    public Func<FakeSyncCommand, int>? ExecuteNonQueryHandler { get; set; }

    public void Cancel() { }
    public IDbDataParameter CreateParameter() => new FakeSyncParameter();
    public void Dispose() { }
    public int ExecuteNonQuery()
    {
        if (Parameters.Contains("OutStatus") && ((IDbDataParameter)Parameters["OutStatus"]).Value == null)
        {
            ((IDbDataParameter)Parameters["OutStatus"]).Value = 0;
        }
        if (Parameters.Contains("OutHandle") && ((IDbDataParameter)Parameters["OutHandle"]).Value == null)
        {
            ((IDbDataParameter)Parameters["OutHandle"]).Value = "ORA_SYNC_HANDLE";
        }
        return ExecuteNonQueryHandler != null ? ExecuteNonQueryHandler(this) : 1;
    }
    public IDataReader ExecuteReader() => throw new NotSupportedException();
    public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
    public object? ExecuteScalar() => ExecuteScalarHandler != null ? ExecuteScalarHandler(this) : 0;
    public void Prepare() { }
}

