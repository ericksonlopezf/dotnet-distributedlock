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

public sealed class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Open;

    public override string ConnectionString { get; set; } = "Host=localhost;Database=test";
    public override string Database => "test";
    public override string DataSource => "localhost";
    public override string ServerVersion => "17.0";
    public override ConnectionState State => _state;

    public void SetState(ConnectionState state) => _state = state;

    public Func<FakeDbCommand, CancellationToken, Task<object?>>? ExecuteScalarAsyncHandler { get; set; }
    public Func<FakeDbCommand, CancellationToken, Task<int>>? ExecuteNonQueryAsyncHandler { get; set; }
    public Func<FakeDbCommand, object?>? ExecuteScalarHandler { get; set; }
    public Func<FakeDbCommand, int>? ExecuteNonQueryHandler { get; set; }
    public Func<CancellationToken, Task>? OpenAsyncHandler { get; set; }

    public List<FakeDbCommand> CommandsCreated { get; } = new();
    public int OpenAsyncCallCount { get; private set; }
    public int DisposeAsyncCallCount { get; private set; }

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
    public override void Open() => _state = ConnectionState.Open;

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        OpenAsyncCallCount++;
        if (OpenAsyncHandler != null)
        {
            await OpenAsyncHandler(cancellationToken);
        }
        _state = ConnectionState.Open;
    }

    public override ValueTask DisposeAsync()
    {
        DisposeAsyncCallCount++;
        _state = ConnectionState.Closed;
        return ValueTask.CompletedTask;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
        new FakeDbTransaction(this, isolationLevel);

    protected override DbCommand CreateDbCommand()
    {
        var cmd = new FakeDbCommand
        {
            ExecuteScalarAsyncHandler = ExecuteScalarAsyncHandler,
            ExecuteNonQueryAsyncHandler = ExecuteNonQueryAsyncHandler,
            ExecuteScalarHandler = ExecuteScalarHandler,
            ExecuteNonQueryHandler = ExecuteNonQueryHandler
        };
        CommandsCreated.Add(cmd);
        return cmd;
    }
}

// Pure IDbConnection / IDbTransaction / IDbCommand (NOT inheriting from DbConnection / DbTransaction / DbCommand)

