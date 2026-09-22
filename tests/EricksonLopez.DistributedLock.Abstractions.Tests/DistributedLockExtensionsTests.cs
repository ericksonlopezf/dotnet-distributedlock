// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Abstractions.Tests;

public sealed class DistributedLockExtensionsTests
{
    private sealed class StubHandle : IDistributedLockHandle
    {
        private readonly CancellationTokenSource _cts = new();

        public string ResourceId { get; init; } = "test-resource";
        public long LockId { get; init; } = 12345;
        public CancellationToken HandleLostToken => _cts.Token;
        public bool IsDisposed { get; private set; }

        public void TriggerHandleLost() => _cts.Cancel();

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubLockProvider : IDistributedLockProvider
    {
        public StubHandle Handle { get; } = new();
        public bool ShouldAcquire { get; set; } = true;
        public Error ErrorToReturn { get; set; } = DistributedLockErrors.LockAlreadyHeld;

        public Task<Result<IAsyncDisposable>> TryAcquireAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            if (ShouldAcquire)
            {
                return Task.FromResult(Result<IAsyncDisposable>.Success(Handle));
            }
            return Task.FromResult(Result<IAsyncDisposable>.Failure(ErrorToReturn));
        }

        public Task<Result<IAsyncDisposable>> TryAcquireAsync(string resourceId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (ShouldAcquire)
            {
                return Task.FromResult(Result<IAsyncDisposable>.Success(Handle));
            }
            return Task.FromResult(Result<IAsyncDisposable>.Failure(ErrorToReturn));
        }

        public Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            if (ShouldAcquire)
            {
                return Task.FromResult(Result<IDistributedLockHandle>.Success(Handle));
            }
            return Task.FromResult(Result<IDistributedLockHandle>.Failure(ErrorToReturn));
        }

        public Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(string resourceId, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (ShouldAcquire)
            {
                return Task.FromResult(Result<IDistributedLockHandle>.Success(Handle));
            }
            return Task.FromResult(Result<IDistributedLockHandle>.Failure(ErrorToReturn));
        }
    }

    [Fact]
    public async Task ExecuteWithLockAsync_ThrowsArgumentNull_WhenProviderNull()
    {
        IDistributedLockProvider provider = null!;
        var act = () => provider.ExecuteWithLockAsync("res", ct => Task.CompletedTask);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_ThrowsArgumentException_WhenResourceIdIsNullOrWhiteSpace()
    {
        var provider = new StubLockProvider();
        var act1 = () => provider.ExecuteWithLockAsync(null!, ct => Task.CompletedTask);
        var act2 = () => provider.ExecuteWithLockAsync("", ct => Task.CompletedTask);
        var act3 = () => provider.ExecuteWithLockAsync("   ", ct => Task.CompletedTask);

        await act1.Should().ThrowAsync<ArgumentException>();
        await act2.Should().ThrowAsync<ArgumentException>();
        await act3.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_ThrowsArgumentNull_WhenActionNull()
    {
        var provider = new StubLockProvider();
        var act = () => provider.ExecuteWithLockAsync("res", (Func<CancellationToken, Task>)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_ExecutesActionAndDisposesHandle_WhenLockAcquired()
    {
        var provider = new StubLockProvider();
        var executed = false;

        var result = await provider.ExecuteWithLockAsync("res", ct =>
        {
            executed = true;
            ct.IsCancellationRequested.Should().BeFalse();
            return Task.CompletedTask;
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        executed.Should().BeTrue();
        provider.Handle.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_Generic_ReturnsValueAndDisposesHandle_WhenLockAcquired()
    {
        var provider = new StubLockProvider();

        var result = await provider.ExecuteWithLockAsync("res", ct =>
        {
            ct.IsCancellationRequested.Should().BeFalse();
            return Task.FromResult(42);
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        provider.Handle.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_ReturnsFailure_WhenLockNotAcquired()
    {
        var provider = new StubLockProvider { ShouldAcquire = false };
        var executed = false;

        var result = await provider.ExecuteWithLockAsync("res", ct =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DistributedLockErrors.LockAlreadyHeld);
        executed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_LinksHandleLostTokenToActionToken()
    {
        var provider = new StubLockProvider();
        var tcs = new TaskCompletionSource<bool>();
        var actionStarted = new TaskCompletionSource<bool>();

        var task = provider.ExecuteWithLockAsync("res", async ct =>
        {
            actionStarted.SetResult(true);
            try
            {
                await Task.Delay(5000, ct);
                tcs.SetResult(false);
            }
            catch (OperationCanceledException)
            {
                tcs.SetResult(true);
            }
        });

        await actionStarted.Task;
        provider.Handle.TriggerHandleLost();

        var wasCanceled = await tcs.Task;
        wasCanceled.Should().BeTrue();
        await task;
        provider.Handle.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_DisposesHandle_EvenWhenActionThrows()
    {
        var provider = new StubLockProvider();

        var act = () => provider.ExecuteWithLockAsync("res", ct => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        provider.Handle.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_WithTimeout_ExecutesActionAndDisposesHandle_WhenLockAcquired()
    {
        var provider = new StubLockProvider();
        var executed = false;

        var result = await provider.ExecuteWithLockAsync("res", TimeSpan.FromSeconds(5), ct =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
        provider.Handle.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_WithTimeout_ReturnsFailure_WhenLockNotAcquired()
    {
        var provider = new StubLockProvider { ShouldAcquire = false };
        var executed = false;

        var result = await provider.ExecuteWithLockAsync("res", TimeSpan.FromSeconds(5), ct =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DistributedLockErrors.LockAlreadyHeld);
        executed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_WithTimeout_Generic_ReturnsValueAndDisposesHandle_WhenLockAcquired()
    {
        var provider = new StubLockProvider();

        var result = await provider.ExecuteWithLockAsync("res", TimeSpan.FromSeconds(5), ct =>
        {
            return Task.FromResult("ok");
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
        provider.Handle.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithLockAsync_WithTimeout_Generic_ReturnsFailure_WhenLockNotAcquired()
    {
        var provider = new StubLockProvider { ShouldAcquire = false };

        var result = await provider.ExecuteWithLockAsync("res", TimeSpan.FromSeconds(5), ct =>
        {
            return Task.FromResult("ok");
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DistributedLockErrors.LockAlreadyHeld);
    }
}
