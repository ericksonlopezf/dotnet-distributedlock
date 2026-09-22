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

public sealed class FakeDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Open;

    public override string ConnectionString { get; set; } = "Data Source=localhost:1521/XEPDB1;User Id=system;Password=oracle;";
    public override string Database => "XEPDB1";
    public override string DataSource => "localhost:1521/XEPDB1";
    public override string ServerVersion => "19.0.0.0.0";
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
    public bool WasDisposed { get; private set; }
    public bool WasDisposedAsync { get; private set; }

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

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        _state = ConnectionState.Closed;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        DisposeAsyncCallCount++;
        WasDisposedAsync = true;
        _state = ConnectionState.Closed;
        await base.DisposeAsync();
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

// Pure IDbConnection / IDbTransaction / IDbCommand

