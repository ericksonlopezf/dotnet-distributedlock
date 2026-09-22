// Copyright © Erickson Lopez. MIT License.
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.PostgreSql;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.PostgreSql.Tests;

public sealed class NoOpAsyncDisposableTests
{
    [Fact]
    public async Task NoOpAsyncDisposable_InitializesPropertiesAndDisposesSafely()
    {
        var handle = new NoOpAsyncDisposable("order-100", 555);

        handle.ResourceId.Should().Be("order-100");
        handle.LockId.Should().Be(555);
        handle.HandleLostToken.Should().Be(CancellationToken.None);

        await handle.DisposeAsync();
    }
}
