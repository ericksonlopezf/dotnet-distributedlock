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

public sealed class FakeDbCommand : DbCommand
{
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new FakeDbParameterCollection();
    protected override DbTransaction? DbTransaction { get; set; }

    public Func<FakeDbCommand, CancellationToken, Task<int>>? ExecuteNonQueryAsyncHandler { get; set; }
    public Func<FakeDbCommand, CancellationToken, Task<object?>>? ExecuteScalarAsyncHandler { get; set; }
    public Func<FakeDbCommand, int>? ExecuteNonQueryHandler { get; set; }
    public Func<FakeDbCommand, object?>? ExecuteScalarHandler { get; set; }

    public override void Cancel() { }

    protected override DbParameter CreateDbParameter() => new FakeDbParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override int ExecuteNonQuery() => ExecuteNonQueryHandler?.Invoke(this) ?? 1;

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        if (ExecuteNonQueryAsyncHandler != null)
        {
            return ExecuteNonQueryAsyncHandler(this, cancellationToken);
        }
        if (ExecuteNonQueryHandler != null)
        {
            return Task.FromResult(ExecuteNonQueryHandler(this));
        }
        return Task.FromResult(1);
    }

    public override object? ExecuteScalar() => ExecuteScalarHandler?.Invoke(this);

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
        ExecuteScalarAsyncHandler != null ? ExecuteScalarAsyncHandler(this, cancellationToken) : Task.FromResult<object?>(null);

    public override void Prepare() { }
}

