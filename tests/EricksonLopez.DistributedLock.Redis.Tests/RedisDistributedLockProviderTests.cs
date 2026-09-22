// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Redis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.DistributedLock.Redis.Tests;

public sealed class RedisDistributedLockProviderTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly NullLogger<RedisDistributedLockProvider> _logger = NullLogger<RedisDistributedLockProvider>.Instance;

    public RedisDistributedLockProviderTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullMultiplexer_ThrowsArgumentNullException()
    {
        var act = () => new RedisDistributedLockProvider(null!, _logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("multiplexer");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new RedisDistributedLockProvider(_multiplexer, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithOptionsWrapper_InitializesCorrectly()
    {
        var options = Options.Create(new RedisLockOptions { KeyPrefix = "custom-wrap:" });
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger, options);
        provider.Should().NotBeNull();
    }

    [Fact]
    public async Task Constructor_WithCustomOptions_UsesConfiguredValues()
    {
        RedisKey capturedKey = default;
        TimeSpan? capturedExpiry = null;

        _database.StringSetAsync(
            Arg.Do<RedisKey>(k => capturedKey = k),
            Arg.Any<RedisValue>(),
            Arg.Do<TimeSpan?>(e => capturedExpiry = e),
            Arg.Any<When>()).Returns(Task.FromResult(true));

        var options = new RedisLockOptions
        {
            KeyPrefix = "app-test:",
            DefaultExpiry = TimeSpan.FromSeconds(75)
        };

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger, options);
        var result = await provider.TryAcquireHandleAsync("order-99");

        result.IsSuccess.Should().BeTrue();
        capturedKey.ToString().Should().Be("app-test:order-99");
        capturedExpiry.Should().Be(TimeSpan.FromSeconds(75));

        await result.Value.DisposeAsync();
    }

    #endregion

    #region Argument Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.TryAcquireAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireAsync_WithTimeout_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.TryAcquireAsync(resourceId!, TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_NegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.TryAcquireAsync("res-1", TimeSpan.FromSeconds(-1));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("timeout");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AcquireAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.AcquireAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireHandleAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.TryAcquireHandleAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireHandleAsync_WithTimeout_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.TryAcquireHandleAsync(resourceId!, TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_NegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.TryAcquireHandleAsync("res-neg", TimeSpan.FromMilliseconds(-10));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("timeout");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AcquireHandleAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.AcquireHandleAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Pre-cancellation Tests

    [Fact]
    public async Task TryAcquireAsync_PreCanceled_ReturnsCanceledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireAsync("res-precancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_PreCanceled_ReturnsCanceledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireAsync("res-precancel-timeout", TimeSpan.FromSeconds(5), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_PreCanceled_ReturnsCanceledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireHandleAsync("res-precancel-handle", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_PreCanceled_ReturnsCanceledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireHandleAsync("res-precancel-handle-timeout", TimeSpan.FromSeconds(5), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireAsync_PreCanceled_ReturnsCanceledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.AcquireAsync("res-precancel-acq", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_PreCanceled_ReturnsCanceledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.AcquireHandleAsync("res-precancel-acq-handle", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    #endregion

    #region Acquisition & Error Flow Tests

    [Fact]
    public async Task TryAcquireHandleAsync_WhenSuccessful_ReturnsHandleWithCorrectParameters()
    {
        RedisKey capturedKey = default;
        RedisValue capturedToken = default;
        When capturedWhen = default;

        _database.StringSetAsync(
            Arg.Do<RedisKey>(k => capturedKey = k),
            Arg.Do<RedisValue>(v => capturedToken = v),
            Arg.Any<TimeSpan?>(),
            Arg.Do<When>(w => capturedWhen = w)).Returns(Task.FromResult(true));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireHandleAsync("invoice-100");

        result.IsSuccess.Should().BeTrue();
        capturedKey.ToString().Should().Be("lock:invoice-100");
        capturedToken.ToString()!.Length.Should().Be(32);
        capturedWhen.Should().Be(When.NotExists);

        result.Value.ResourceId.Should().Be("invoice-100");
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WhenAlreadyHeld_ReturnsLockAlreadyHeld()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(false));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireHandleAsync("already-held-res");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WhenOperationCanceledExceptionThrown_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();

        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns<Task<bool>>(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireHandleAsync("cancel-during-call", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WhenUnexpectedExceptionThrown_Rethrows()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).ThrowsAsync(new RedisServerException("CLUSTERDOWN The cluster is down"));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var act = () => provider.TryAcquireHandleAsync("cluster-down-res");

        await act.Should().ThrowAsync<RedisServerException>().WithMessage("*CLUSTERDOWN*");
    }

    [Fact]
    public async Task TryAcquireAsync_WhenSuccessful_ReturnsDisposableResult()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(true));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireAsync("disp-succ-res");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenFailed_ReturnsFailureResult()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(false));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireAsync("disp-fail-res");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    #endregion

    #region Timeout & Retry Tests

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_ImmediateSuccess_ReturnsWithoutWaiting()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(true));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireHandleAsync("immediate-timeout-succ", TimeSpan.FromSeconds(5));

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_RetriesAndSucceeds()
    {
        var attempts = 0;
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(_ =>
        {
            attempts++;
            return Task.FromResult(attempts >= 2);
        });

        var provider = new RedisDistributedLockProvider(
            _multiplexer,
            _logger,
            new RedisLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(10),
                BackoffJitter = false
            });

        var result = await provider.TryAcquireHandleAsync("retry-succ-res", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_NeverSucceeds_ReturnsTimeout()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(false));

        var provider = new RedisDistributedLockProvider(
            _multiplexer,
            _logger,
            new RedisLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(10),
                BackoffJitter = true
            });

        var result = await provider.TryAcquireHandleAsync("never-succ-res", TimeSpan.FromMilliseconds(60));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_ZeroTimeout_ReturnsTimeoutImmediately()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(false));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireHandleAsync("zero-timeout-res", TimeSpan.Zero);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_CanceledDuringPollingDelay_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(_ =>
        {
            attempts++;
            cts.Cancel();
            return Task.FromResult(false);
        });

        var provider = new RedisDistributedLockProvider(
            _multiplexer,
            _logger,
            new RedisLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(50),
                BackoffJitter = false
            });

        var result = await provider.TryAcquireHandleAsync("cancel-delay-res", TimeSpan.FromSeconds(5), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_ZeroRetryInterval_UsesDefaultFiftyMs()
    {
        var attempts = 0;
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(_ =>
        {
            attempts++;
            return Task.FromResult(attempts >= 2);
        });

        var provider = new RedisDistributedLockProvider(
            _multiplexer,
            _logger,
            new RedisLockOptions
            {
                RetryInterval = TimeSpan.Zero,
                BackoffJitter = false
            });

        var result = await provider.TryAcquireHandleAsync("zero-retry-interval", TimeSpan.FromSeconds(1));
        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_WhenSuccessful_ReturnsDisposable()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(true));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireAsync("disp-timeout-succ", TimeSpan.FromSeconds(5));

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_WhenFails_ReturnsFailureResult()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(false));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.TryAcquireAsync("disp-timeout-fail", TimeSpan.FromMilliseconds(20));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    #endregion

    #region Blocking Acquire Tests

    [Fact]
    public async Task AcquireHandleAsync_WhenSuccessful_ReturnsHandle()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(true));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.AcquireHandleAsync("acq-handle-succ");

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireHandleAsync_RetriesUntilSuccess()
    {
        var attempts = 0;
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(_ =>
        {
            attempts++;
            return Task.FromResult(attempts >= 2);
        });

        var provider = new RedisDistributedLockProvider(
            _multiplexer,
            _logger,
            new RedisLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(10),
                BackoffJitter = false
            });

        var result = await provider.AcquireHandleAsync("acq-handle-retry");

        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireHandleAsync_CanceledDuringDelay_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();

        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(_ =>
        {
            cts.Cancel();
            return Task.FromResult(false);
        });

        var provider = new RedisDistributedLockProvider(
            _multiplexer,
            _logger,
            new RedisLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(50),
                BackoffJitter = false
            });

        var result = await provider.AcquireHandleAsync("acq-cancel-during-delay", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_ZeroRetryInterval_UsesDefaultFiftyMs()
    {
        var attempts = 0;
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(_ =>
        {
            attempts++;
            return Task.FromResult(attempts >= 2);
        });

        var provider = new RedisDistributedLockProvider(
            _multiplexer,
            _logger,
            new RedisLockOptions
            {
                RetryInterval = TimeSpan.Zero,
                BackoffJitter = true
            });

        var result = await provider.AcquireHandleAsync("acq-zero-interval");

        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_WhenSuccessful_ReturnsDisposable()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(true));

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.AcquireAsync("acq-async-succ");

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_WhenCanceled_ReturnsFailureResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.AcquireAsync("acq-async-precancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    #endregion

    #region IDistributedLockProvider Default Methods Tests

    [Fact]
    public async Task DefaultInterfaceMethods_AcquireAsyncWithTimeout_WhenSuccessful_ReturnsDisposable()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(true));

        IDistributedLockProvider provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.AcquireAsync("def-acq-timeout", TimeSpan.FromSeconds(5));

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task DefaultInterfaceMethods_AcquireHandleAsyncWithTimeout_WhenSuccessful_ReturnsHandle()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(true));

        IDistributedLockProvider provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.AcquireHandleAsync("def-acq-handle-timeout", TimeSpan.FromSeconds(5));

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task DefaultInterfaceMethods_AcquireHandleAsyncWithTimeout_WhenFails_ReturnsFailureResult()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<When>()).Returns(Task.FromResult(false));

        IDistributedLockProvider provider = new RedisDistributedLockProvider(_multiplexer, _logger);
        var result = await provider.AcquireHandleAsync("def-acq-handle-timeout-fail", TimeSpan.FromMilliseconds(10));

        result.IsFailure.Should().BeTrue();
    }

    #endregion
}
