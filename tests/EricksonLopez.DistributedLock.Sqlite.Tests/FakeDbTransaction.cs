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

public sealed class FakeDbTransaction : DbTransaction
{
    private readonly DbConnection _connection;

    public FakeDbTransaction(DbConnection connection, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    protected override DbConnection DbConnection => _connection;
    public override IsolationLevel IsolationLevel { get; }
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }

    public override void Commit() => Committed = true;
    public override void Rollback() => RolledBack = true;
}

