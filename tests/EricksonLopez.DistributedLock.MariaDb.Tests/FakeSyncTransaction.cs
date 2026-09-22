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

namespace EricksonLopez.DistributedLock.MariaDb.Tests;

public sealed class FakeSyncTransaction : IDbTransaction
{
    public FakeSyncTransaction(IDbConnection connection, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        Connection = connection;
        IsolationLevel = isolationLevel;
    }

    public IDbConnection? Connection { get; }
    public IsolationLevel IsolationLevel { get; }
    public void Commit() { }
    public void Dispose() { }
    public void Rollback() { }
}

