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

public sealed class FakeSyncConnection : IDbConnection
{
    public string ConnectionString { get; set; } = string.Empty;
    public int ConnectionTimeout => 30;
    public string Database => "sync_db";
    public ConnectionState State { get; set; } = ConnectionState.Open;

    public Func<FakeSyncCommand, object?>? ExecuteScalarHandler { get; set; }
    public Func<FakeSyncCommand, int>? ExecuteNonQueryHandler { get; set; }
    public List<FakeSyncCommand> CommandsCreated { get; } = new();

    public IDbTransaction BeginTransaction() => new FakeSyncTransaction(this);
    public IDbTransaction BeginTransaction(IsolationLevel il) => new FakeSyncTransaction(this, il);
    public void ChangeDatabase(string databaseName) { }
    public void Close() => State = ConnectionState.Closed;
    public void Dispose() => State = ConnectionState.Closed;
    public void Open() => State = ConnectionState.Open;

    public IDbCommand CreateCommand()
    {
        var cmd = new FakeSyncCommand
        {
            Connection = this,
            ExecuteScalarHandler = ExecuteScalarHandler,
            ExecuteNonQueryHandler = ExecuteNonQueryHandler
        };
        CommandsCreated.Add(cmd);
        return cmd;
    }
}

