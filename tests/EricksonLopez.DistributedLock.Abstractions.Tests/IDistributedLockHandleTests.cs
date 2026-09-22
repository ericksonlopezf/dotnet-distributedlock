// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Abstractions.Tests;

public sealed class IDistributedLockHandleTests
{
    private sealed class TestLockHandle : IDistributedLockHandle
    {
        public string ResourceId { get; init; } = string.Empty;
        public long LockId { get; init; }
        public CancellationToken HandleLostToken { get; init; }
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Handle_ExposesRequiredContractsAndDisposes()
    {
        using var cts = new CancellationTokenSource();
        var handle = new TestLockHandle
        {
            ResourceId = "account:transfer:999",
            LockId = 987654321,
            HandleLostToken = cts.Token
        };

        handle.ResourceId.Should().Be("account:transfer:999");
        handle.LockId.Should().Be(987654321);
        handle.HandleLostToken.Should().Be(cts.Token);
        handle.IsDisposed.Should().BeFalse();

        await handle.DisposeAsync();
        handle.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void FencingToken_DefaultImplementation_ReturnsNull()
    {
        IDistributedLockHandle handle = new TestLockHandle
        {
            ResourceId = "account:1",
            LockId = 1,
            HandleLostToken = CancellationToken.None
        };

        handle.FencingToken.Should().BeNull();
    }
}
