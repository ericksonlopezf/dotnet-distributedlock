// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Abstractions.Tests;

public sealed class IDistributedLockProviderDefaultTests
{
    private sealed class MinimalLockProvider : IDistributedLockProvider
    {
        public Func<string, CancellationToken, Task<Result<IAsyncDisposable>>>? TryAcquireHandler { get; set; }
        public Func<string, TimeSpan, CancellationToken, Task<Result<IAsyncDisposable>>>? TryAcquireWithTimeoutHandler { get; set; }

        public Task<Result<IAsyncDisposable>> TryAcquireAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            if (TryAcquireHandler is not null)
            {
                return TryAcquireHandler(resourceId, cancellationToken);
            }

            return Task.FromResult(Result<IAsyncDisposable>.Success(new TestAsyncDisposable()));
        }

        public Task<Result<IAsyncDisposable>> TryAcquireAsync(string resourceId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (TryAcquireWithTimeoutHandler is not null)
            {
                return TryAcquireWithTimeoutHandler(resourceId, timeout, cancellationToken);
            }

            return Task.FromResult(Result<IAsyncDisposable>.Success(new TestAsyncDisposable()));
        }
    }

    private sealed class TestAsyncDisposable : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CustomTypedLockHandle : IDistributedLockHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public string ResourceId { get; init; } = "custom-res";
        public long LockId { get; init; } = 42;
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task AcquireAsync_NoTimeout_DelegatesToTryAcquireWithInfiniteTimeSpan()
    {
        var minimal = new MinimalLockProvider();
        IDistributedLockProvider provider = minimal;
        string? passedResource = null;
        TimeSpan? passedTimeout = null;
        CancellationToken passedToken = default;

        using var cts = new CancellationTokenSource();
        var disposable = new TestAsyncDisposable();

        minimal.TryAcquireWithTimeoutHandler = (res, timeout, ct) =>
        {
            passedResource = res;
            passedTimeout = timeout;
            passedToken = ct;
            return Task.FromResult(Result<IAsyncDisposable>.Success(disposable));
        };

        var result = await provider.AcquireAsync("resource-123", cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(disposable);
        passedResource.Should().Be("resource-123");
        passedTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        passedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task AcquireAsync_WithTimeout_DelegatesToTryAcquireWithSpecifiedTimeout()
    {
        var minimal = new MinimalLockProvider();
        IDistributedLockProvider provider = minimal;
        string? passedResource = null;
        TimeSpan? passedTimeout = null;
        CancellationToken passedToken = default;

        using var cts = new CancellationTokenSource();
        var disposable = new TestAsyncDisposable();
        var expectedTimeout = TimeSpan.FromSeconds(30);

        minimal.TryAcquireWithTimeoutHandler = (res, timeout, ct) =>
        {
            passedResource = res;
            passedTimeout = timeout;
            passedToken = ct;
            return Task.FromResult(Result<IAsyncDisposable>.Success(disposable));
        };

        var result = await provider.AcquireAsync("resource-456", expectedTimeout, cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(disposable);
        passedResource.Should().Be("resource-456");
        passedTimeout.Should().Be(expectedTimeout);
        passedToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WhenTryAcquireFails_ReturnsFailureError()
    {
        var minimal = new MinimalLockProvider
        {
            TryAcquireHandler = (_, _) => Task.FromResult(Result<IAsyncDisposable>.Failure(DistributedLockErrors.LockAlreadyHeld))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.TryAcquireHandleAsync("resource-held");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WhenResultIsAlreadyIDistributedLockHandle_ReturnsSameInstance()
    {
        var typedHandle = new CustomTypedLockHandle { ResourceId = "orders:789", LockId = 99 };
        var minimal = new MinimalLockProvider
        {
            TryAcquireHandler = (_, _) => Task.FromResult(Result<IAsyncDisposable>.Success(typedHandle))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.TryAcquireHandleAsync("orders:789");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(typedHandle);
        result.Value.ResourceId.Should().Be("orders:789");
        result.Value.LockId.Should().Be(99);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WhenResultIsPlainIAsyncDisposable_WrapsInAdapterAndPreservesContract()
    {
        var plainDisposable = new TestAsyncDisposable();
        var minimal = new MinimalLockProvider
        {
            TryAcquireHandler = (_, _) => Task.FromResult(Result<IAsyncDisposable>.Success(plainDisposable))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.TryAcquireHandleAsync("customers:456");

        result.IsSuccess.Should().BeTrue();
        var handle = result.Value;
        handle.Should().NotBeNull();
        handle.ResourceId.Should().Be("customers:456");
        handle.LockId.Should().Be(0);
        handle.HandleLostToken.Should().Be(CancellationToken.None);

        plainDisposable.IsDisposed.Should().BeFalse();
        await handle.DisposeAsync();
        plainDisposable.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_WhenTryAcquireFails_ReturnsFailureError()
    {
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Failure(DistributedLockErrors.Timeout))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.TryAcquireHandleAsync("resource-timeout", TimeSpan.FromSeconds(5));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_WhenResultIsAlreadyIDistributedLockHandle_ReturnsSameInstance()
    {
        var typedHandle = new CustomTypedLockHandle { ResourceId = "orders:timeout-ok", LockId = 101 };
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Success(typedHandle))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.TryAcquireHandleAsync("orders:timeout-ok", TimeSpan.FromSeconds(10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(typedHandle);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_WhenResultIsPlainIAsyncDisposable_WrapsInAdapter()
    {
        var plainDisposable = new TestAsyncDisposable();
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Success(plainDisposable))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.TryAcquireHandleAsync("invoices:timeout-wrap", TimeSpan.FromSeconds(10));

        result.IsSuccess.Should().BeTrue();
        var handle = result.Value;
        handle.ResourceId.Should().Be("invoices:timeout-wrap");
        handle.LockId.Should().Be(0);
        handle.HandleLostToken.Should().Be(CancellationToken.None);

        await handle.DisposeAsync();
        plainDisposable.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireHandleAsync_WhenAcquireFails_ReturnsFailureError()
    {
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Failure(DistributedLockErrors.Canceled))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.AcquireHandleAsync("resource-cancel");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_WhenResultIsAlreadyIDistributedLockHandle_ReturnsSameInstance()
    {
        var typedHandle = new CustomTypedLockHandle { ResourceId = "payments:acquire", LockId = 555 };
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Success(typedHandle))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.AcquireHandleAsync("payments:acquire");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(typedHandle);
    }

    [Fact]
    public async Task AcquireHandleAsync_WhenResultIsPlainIAsyncDisposable_WrapsInAdapter()
    {
        var plainDisposable = new TestAsyncDisposable();
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Success(plainDisposable))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.AcquireHandleAsync("payments:acquire-wrap");

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("payments:acquire-wrap");

        await result.Value.DisposeAsync();
        plainDisposable.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireHandleAsync_WithTimeout_WhenAcquireFails_ReturnsFailureError()
    {
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Failure(DistributedLockErrors.Timeout))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.AcquireHandleAsync("resource-timeout-acq", TimeSpan.FromSeconds(3));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_WithTimeout_WhenResultIsAlreadyIDistributedLockHandle_ReturnsSameInstance()
    {
        var typedHandle = new CustomTypedLockHandle { ResourceId = "typed:acquire-timeout", LockId = 777 };
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Success(typedHandle))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.AcquireHandleAsync("typed:acquire-timeout", TimeSpan.FromSeconds(5));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(typedHandle);
    }

    [Fact]
    public async Task AcquireHandleAsync_WithTimeout_WhenResultIsPlainIAsyncDisposable_WrapsInAdapter()
    {
        var plainDisposable = new TestAsyncDisposable();
        var minimal = new MinimalLockProvider
        {
            TryAcquireWithTimeoutHandler = (_, _, _) => Task.FromResult(Result<IAsyncDisposable>.Success(plainDisposable))
        };
        IDistributedLockProvider provider = minimal;

        var result = await provider.AcquireHandleAsync("plain:acquire-timeout", TimeSpan.FromSeconds(5));

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("plain:acquire-timeout");

        await result.Value.DisposeAsync();
        plainDisposable.IsDisposed.Should().BeTrue();
    }
}
