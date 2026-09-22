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

namespace EricksonLopez.DistributedLock.SqlServer.Tests;

public sealed class FakeDbTransaction : DbTransaction
{
    private readonly DbConnection _connection;

    public FakeDbTransaction(DbConnection connection, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    public override IsolationLevel IsolationLevel { get; }
    protected override DbConnection DbConnection => _connection;

    public override void Commit() { }
    public override void Rollback() { }
}

