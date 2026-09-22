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

namespace EricksonLopez.DistributedLock.PostgreSql.Tests;

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

    public override int ExecuteNonQuery() => ExecuteNonQueryHandler != null ? ExecuteNonQueryHandler(this) : 1;

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
        ExecuteNonQueryAsyncHandler != null ? ExecuteNonQueryAsyncHandler(this, cancellationToken) : ExecuteNonQueryHandler != null ? Task.FromResult(ExecuteNonQueryHandler(this)) : Task.FromResult(1);

    public override object? ExecuteScalar() => ExecuteScalarHandler != null ? ExecuteScalarHandler(this) : true;

    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
        ExecuteScalarAsyncHandler != null ? ExecuteScalarAsyncHandler(this, cancellationToken) : Task.FromResult<object?>(true);

    public override void Prepare() { }

    protected override DbParameter CreateDbParameter() => new FakeDbParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
}

