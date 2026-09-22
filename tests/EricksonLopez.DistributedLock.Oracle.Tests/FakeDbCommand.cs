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

public sealed class FakeDbCommand : DbCommand
{
    private readonly FakeDbParameterCollection _parameters = new();

    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public Func<FakeDbCommand, CancellationToken, Task<object?>>? ExecuteScalarAsyncHandler { get; set; }
    public Func<FakeDbCommand, CancellationToken, Task<int>>? ExecuteNonQueryAsyncHandler { get; set; }
    public Func<FakeDbCommand, object?>? ExecuteScalarHandler { get; set; }
    public Func<FakeDbCommand, int>? ExecuteNonQueryHandler { get; set; }

    public override void Cancel() { }

    public override int ExecuteNonQuery()
    {
        SetDefaultOracleOutputParams();
        return ExecuteNonQueryHandler != null ? ExecuteNonQueryHandler(this) : 1;
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        SetDefaultOracleOutputParams();
        return ExecuteNonQueryAsyncHandler != null ? await ExecuteNonQueryAsyncHandler(this, cancellationToken) : 1;
    }

    private void SetDefaultOracleOutputParams()
    {
        if (Parameters.Contains("OutStatus") && Parameters["OutStatus"].Value == null)
        {
            Parameters["OutStatus"].Value = 0; // Success
        }
        if (Parameters.Contains("OutHandle") && Parameters["OutHandle"].Value == null)
        {
            Parameters["OutHandle"].Value = "ORA_LOCK_HANDLE_DEFAULT";
        }
    }

    public override object? ExecuteScalar() => ExecuteScalarHandler != null ? ExecuteScalarHandler(this) : 0;

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        if (ExecuteScalarAsyncHandler != null)
        {
            return ExecuteScalarAsyncHandler(this, cancellationToken);
        }
        if (ExecuteScalarHandler != null)
        {
            return Task.FromResult(ExecuteScalarHandler(this));
        }
        return Task.FromResult<object?>(0);
    }

    public override void Prepare() { }

    protected override DbParameter CreateDbParameter() => new FakeDbParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
}

