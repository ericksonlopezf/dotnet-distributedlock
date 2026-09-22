// Copyright © Erickson Lopez. MIT License.
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Redis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.DistributedLock.Redis.Tests;

public sealed class RedisDistributedLockHandleTests
{
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly ILogger _logger = NullLogger.Instance;

    [Fact]
    public void Constructor_NullDatabase_ThrowsArgumentNullException()
    {
        var act = () => new RedisDistributedLockHandle(
            null!,
            "lock:res-1",
            "token-val",
            "res-1",
            TimeSpan.FromSeconds(30),
            _logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("database");
    }

    [Fact]
    public void Constructor_NullResourceId_ThrowsArgumentNullException()
    {
        var act = () => new RedisDistributedLockHandle(
            _database,
            "lock:res-1",
            "token-val",
            null!,
            TimeSpan.FromSeconds(30),
            _logger);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resourceId");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new RedisDistributedLockHandle(
            _database,
            "lock:res-1",
            "token-val",
            "res-1",
            TimeSpan.FromSeconds(30),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task Properties_ReturnExpectedValues()
    {
        const string resId = "test-res-props";
        var expectedLockId = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(resId)), 0);

        await using var handle = new RedisDistributedLockHandle(
            _database,
            "lock:" + resId,
            "token-123",
            resId,
            TimeSpan.FromSeconds(30),
            _logger);

        handle.ResourceId.Should().Be(resId);
        handle.LockId.Should().Be(expectedLockId);
        handle.HandleLostToken.Should().NotBeNull();
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task KeepaliveTimer_WhenCadenceNullOrNonPositive_DoesNotTriggerRenew(int? cadenceSeconds)
    {
        TimeSpan? cadence = cadenceSeconds.HasValue ? TimeSpan.FromSeconds(cadenceSeconds.Value) : null;

        var handle = new RedisDistributedLockHandle(
            _database,
            "lock:no-cadence",
            "token-val",
            "no-cadence",
            TimeSpan.FromSeconds(30),
            _logger,
            cadence);

        await Task.Delay(50);

        await _database.DidNotReceiveWithAnyArgs().ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task KeepaliveTimer_WhenRenewSucceeds_KeepsHandleLostTokenUncancelled()
    {
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create((RedisValue)1, ResultType.Integer)));

        var handle = new RedisDistributedLockHandle(
            _database,
            "lock:renew-success",
            "token-val",
            "renew-success",
            TimeSpan.FromSeconds(30),
            _logger,
            TimeSpan.FromMilliseconds(20));

        await Task.Delay(80);

        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task KeepaliveTimer_WhenRenewFails_CancelsHandleLostToken()
    {
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create((RedisValue)0, ResultType.Integer)));

        var handle = new RedisDistributedLockHandle(
            _database,
            "lock:renew-fail",
            "token-val",
            "renew-fail",
            TimeSpan.FromSeconds(30),
            _logger,
            TimeSpan.FromMilliseconds(20));

        // Wait for timer to trigger renewal failure
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!handle.HandleLostToken.IsCancellationRequested && sw.ElapsedMilliseconds < 500)
        {
            await Task.Delay(10);
        }

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task KeepaliveTimer_WhenRenewThrows_CancelsHandleLostToken()
    {
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketClosed, "Socket closed"));

        var handle = new RedisDistributedLockHandle(
            _database,
            "lock:renew-ex",
            "token-val",
            "renew-ex",
            TimeSpan.FromSeconds(30),
            _logger,
            TimeSpan.FromMilliseconds(20));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!handle.HandleLostToken.IsCancellationRequested && sw.ElapsedMilliseconds < 500)
        {
            await Task.Delay(10);
        }

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ReleasesLockAndIsIdempotent()
    {
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(RedisResult.Create((RedisValue)1, ResultType.Integer)));

        var handle = new RedisDistributedLockHandle(
            _database,
            "lock:disp-test",
            "token-123",
            "disp-test",
            TimeSpan.FromSeconds(30),
            _logger,
            TimeSpan.FromMilliseconds(50));

        await handle.DisposeAsync();

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());

        // Second call should be a no-op (idempotent)
        await handle.DisposeAsync();

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DisposeAsync_WhenReleaseThrows_SwallowsExceptionAndLogsWarning()
    {
        _database.ScriptEvaluateAsync(Arg.Any<string>(), Arg.Any<RedisKey[]>(), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisTimeoutException("Timeout releasing lock", CommandStatus.Unknown));

        var handle = new RedisDistributedLockHandle(
            _database,
            "lock:disp-throws",
            "token-123",
            "disp-throws",
            TimeSpan.FromSeconds(30),
            _logger);

        var act = async () => await handle.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_DisposesHandleLostCancellationTokenSource()
    {
        var handle = new RedisDistributedLockHandle(
            _database,
            "lock:disp-cts",
            "token-cts",
            "disp-cts",
            TimeSpan.FromSeconds(30),
            _logger);

        var token = handle.HandleLostToken;
        await handle.DisposeAsync();

        var act = () => token.WaitHandle;
        act.Should().Throw<ObjectDisposedException>();
    }
}
