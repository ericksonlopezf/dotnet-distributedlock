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

public sealed class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Open;

    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "fake_sqlite";
    public override string DataSource => "localhost";
    public override string ServerVersion => "3.40.0";
    public override ConnectionState State => _state;

    public int OpenAsyncCallCount { get; private set; }
    public int DisposeAsyncCallCount { get; private set; }
    public List<FakeDbCommand> CommandsCreated { get; } = new();

    public Func<FakeDbCommand, CancellationToken, Task<int>>? ExecuteNonQueryAsyncHandler { get; set; }
    public Func<FakeDbCommand, CancellationToken, Task<object?>>? ExecuteScalarAsyncHandler { get; set; }
    public Func<FakeDbCommand, int>? ExecuteNonQueryHandler { get; set; }
    public Func<FakeDbCommand, object?>? ExecuteScalarHandler { get; set; }

    public void SetState(ConnectionState state) => _state = state;

    public override void ChangeDatabase(string databaseName) { }

    public override void Close() => _state = ConnectionState.Closed;

    public override void Open() => _state = ConnectionState.Open;

    public override Task OpenAsync(CancellationToken cancellationToken)
    {
        OpenAsyncCallCount++;
        _state = ConnectionState.Open;
        return Task.CompletedTask;
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
            ExecuteNonQueryAsyncHandler = ExecuteNonQueryAsyncHandler,
            ExecuteScalarAsyncHandler = ExecuteScalarAsyncHandler,
            ExecuteNonQueryHandler = ExecuteNonQueryHandler,
            ExecuteScalarHandler = ExecuteScalarHandler
        };
        CommandsCreated.Add(cmd);
        return cmd;
    }
}

