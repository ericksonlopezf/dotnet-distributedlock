// Copyright © Erickson Lopez. MIT License.
using System;
using System.Globalization;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.SqlServer;

/// <summary>
/// Provides extension methods for acquiring transaction-bound SQL Server application locks within active database transactions.
/// </summary>
public static class SqlServerTransactionLockExtensions
{
    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock bound to an active <see cref="IDbTransaction"/>.
    /// </summary>
    /// <param name="transaction">The active database transaction to which the lock will be bound.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, an error result containing
    /// <see cref="DistributedLockErrors.LockAlreadyHeld"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    /// <exception cref="InvalidOperationException">The connection associated with <paramref name="transaction"/> is <see langword="null"/></exception>
    public static async Task<Result<IDistributedLockHandle>> TryAcquireInTransactionAsync(
        this IDbTransaction transaction,
        string resourceId,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(logger);

        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction connection cannot be null.");
        var lockResource = NormalizeResourceKey(resourceId);

        if (connection is DbConnection dbConn)
        {
            await using var cmd = dbConn.CreateCommand();
            cmd.Transaction = (DbTransaction)transaction;
            cmd.CommandText = """
                DECLARE @res INT;
                EXEC @res = sys.sp_getapplock 
                    @Resource = @Resource, 
                    @LockMode = 'Exclusive', 
                    @LockOwner = 'Transaction', 
                    @LockTimeout = 0;
                SELECT @res;
                """;

            var pRes = cmd.CreateParameter();
            pRes.ParameterName = "@Resource";
            pRes.Value = lockResource;
            cmd.Parameters.Add(pRes);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var code = result is int i ? i : Convert.ToInt32(result, CultureInfo.InvariantCulture);

            if (code >= 0)
            {
                logger.LogInformation("Acquired transaction-scoped lock for resource '{ResourceId}'.", resourceId);
                return Result<IDistributedLockHandle>.Success(new SqlServerTransactionLockHandle(resourceId));
            }

            logger.LogWarning("Failed to acquire transaction lock for resource '{ResourceId}'. Return code: {Code}.", resourceId, code);
            return DistributedLockErrors.LockAlreadyHeld;
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.Transaction = transaction;
        syncCmd.CommandText = """
            DECLARE @res INT;
            EXEC @res = sys.sp_getapplock 
                @Resource = @Resource, 
                @LockMode = 'Exclusive', 
                @LockOwner = 'Transaction', 
                @LockTimeout = 0;
            SELECT @res;
            """;

        var p = syncCmd.CreateParameter();
        p.ParameterName = "@Resource";
        p.Value = lockResource;
        syncCmd.Parameters.Add(p);

        var syncRes = syncCmd.ExecuteScalar();
        var syncCode = syncRes is int sc ? sc : Convert.ToInt32(syncRes, CultureInfo.InvariantCulture);

        if (syncCode >= 0)
        {
            logger.LogInformation("Acquired transaction-scoped lock for resource '{ResourceId}'.", resourceId);
            return Result<IDistributedLockHandle>.Success(new SqlServerTransactionLockHandle(resourceId));
        }

        return DistributedLockErrors.LockAlreadyHeld;
    }

    /// <summary>
    /// Acquires an exclusive transaction-scoped lock bound to an active <see cref="IDbTransaction"/>, waiting until the lock is granted.
    /// </summary>
    /// <param name="transaction">The active database transaction to which the lock will be bound.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, an error result containing
    /// <see cref="DistributedLockErrors.LockAlreadyHeld"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    /// <exception cref="InvalidOperationException">The connection associated with <paramref name="transaction"/> is <see langword="null"/></exception>
    public static async Task<Result<IDistributedLockHandle>> AcquireInTransactionAsync(
        this IDbTransaction transaction,
        string resourceId,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(logger);

        var connection = transaction.Connection ?? throw new InvalidOperationException("Transaction connection cannot be null.");
        var lockResource = NormalizeResourceKey(resourceId);

        if (connection is DbConnection dbConn)
        {
            await using var cmd = dbConn.CreateCommand();
            cmd.Transaction = (DbTransaction)transaction;
            cmd.CommandText = """
                DECLARE @res INT;
                EXEC @res = sys.sp_getapplock 
                    @Resource = @Resource, 
                    @LockMode = 'Exclusive', 
                    @LockOwner = 'Transaction', 
                    @LockTimeout = -1;
                SELECT @res;
                """;

            var pRes = cmd.CreateParameter();
            pRes.ParameterName = "@Resource";
            pRes.Value = lockResource;
            cmd.Parameters.Add(pRes);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var code = result is int i ? i : Convert.ToInt32(result, CultureInfo.InvariantCulture);

            if (code >= 0)
            {
                logger.LogInformation("Acquired blocking transaction-scoped lock for resource '{ResourceId}'.", resourceId);
                return Result<IDistributedLockHandle>.Success(new SqlServerTransactionLockHandle(resourceId));
            }

            return DistributedLockErrors.LockAlreadyHeld;
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.Transaction = transaction;
        syncCmd.CommandText = """
            DECLARE @res INT;
            EXEC @res = sys.sp_getapplock 
                @Resource = @Resource, 
                @LockMode = 'Exclusive', 
                @LockOwner = 'Transaction', 
                @LockTimeout = -1;
            SELECT @res;
            """;

        var p = syncCmd.CreateParameter();
        p.ParameterName = "@Resource";
        p.Value = lockResource;
        syncCmd.Parameters.Add(p);

        var syncRes = syncCmd.ExecuteScalar();
        var syncCode = syncRes is int sc ? sc : Convert.ToInt32(syncRes, CultureInfo.InvariantCulture);

        if (syncCode >= 0)
        {
            logger.LogInformation("Acquired blocking transaction-scoped lock for resource '{ResourceId}'.", resourceId);
            return Result<IDistributedLockHandle>.Success(new SqlServerTransactionLockHandle(resourceId));
        }

        return DistributedLockErrors.LockAlreadyHeld;
    }

    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock bound to an active <see cref="DbTransaction"/>.
    /// </summary>
    /// <param name="transaction">The active database transaction to which the lock will be bound.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, an error result containing
    /// <see cref="DistributedLockErrors.LockAlreadyHeld"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    /// <exception cref="InvalidOperationException">The connection associated with <paramref name="transaction"/> is <see langword="null"/></exception>
    public static Task<Result<IDistributedLockHandle>> TryAcquireInTransactionAsync(
        this DbTransaction transaction,
        string resourceId,
        ILogger logger,
        CancellationToken cancellationToken = default)
        => TryAcquireInTransactionAsync((IDbTransaction)transaction, resourceId, logger, cancellationToken);

    /// <summary>
    /// Acquires an exclusive transaction-scoped lock bound to an active <see cref="DbTransaction"/>, waiting until the lock is granted.
    /// </summary>
    /// <param name="transaction">The active database transaction to which the lock will be bound.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, an error result containing
    /// <see cref="DistributedLockErrors.LockAlreadyHeld"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    /// <exception cref="InvalidOperationException">The connection associated with <paramref name="transaction"/> is <see langword="null"/></exception>
    public static Task<Result<IDistributedLockHandle>> AcquireInTransactionAsync(
        this DbTransaction transaction,
        string resourceId,
        ILogger logger,
        CancellationToken cancellationToken = default)
        => AcquireInTransactionAsync((IDbTransaction)transaction, resourceId, logger, cancellationToken);

    private static string NormalizeResourceKey(string resourceId)
    {
        if (resourceId.Length <= 255)
        {
            return resourceId;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resourceId));
        return Convert.ToHexString(hash);
    }

    private sealed class SqlServerTransactionLockHandle : IDistributedLockHandle
    {
        public SqlServerTransactionLockHandle(string resourceId)
        {
            ResourceId = resourceId;
            LockId = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(resourceId)), 0);
        }

        public string ResourceId { get; }
        public long LockId { get; }
        public CancellationToken HandleLostToken => CancellationToken.None;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

