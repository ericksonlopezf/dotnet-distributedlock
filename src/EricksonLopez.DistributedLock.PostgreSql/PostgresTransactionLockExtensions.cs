// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.PostgreSql;

/// <summary>
/// Provides extension methods for acquiring transaction-bound PostgreSQL advisory locks attached to existing database transactions.
/// </summary>
public static class PostgresTransactionLockExtensions
{
    /// <summary>
    /// Attempts to acquire a PostgreSQL advisory lock bound directly to an active database transaction.
    /// The lock is automatically released when the transaction is committed or rolled back.
    /// </summary>
    /// <param name="transaction">The active database transaction to which the lock will be bound.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, an error result containing
    /// <see cref="DistributedLockErrors.LockAlreadyHeld"/> or <see cref="DistributedLockErrors.Canceled"/>.
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

        var connection = transaction.Connection ?? throw new InvalidOperationException("The transaction connection is null or closed.");
        var lockId = PostgresDistributedLockProvider.GenerateLockId(resourceId);

        try
        {
            if (connection is DbConnection dbConn && transaction is DbTransaction dbTran)
            {
                await using var cmd = dbConn.CreateCommand();
                cmd.Transaction = dbTran;
                cmd.CommandText = "SELECT pg_try_advisory_xact_lock(@LockId);";
                var param = cmd.CreateParameter();
                param.ParameterName = "@LockId";
                param.Value = lockId;
                cmd.Parameters.Add(param);

                var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var acquired = scalar is true || (scalar is bool b && b);

                if (acquired)
                {
                    logger.LogInformation("Successfully acquired transaction-bound advisory lock for resource '{ResourceId}'.", resourceId);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                    return Result<IDistributedLockHandle>.Success(new NoOpAsyncDisposable(resourceId, lockId));
                }

                logger.LogWarning("Failed to acquire transaction-bound advisory lock for resource '{ResourceId}'. Already held.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "already_held");
                return DistributedLockErrors.LockAlreadyHeld;
            }

            using var syncCmd = connection.CreateCommand();
            syncCmd.Transaction = transaction;
            syncCmd.CommandText = "SELECT pg_try_advisory_xact_lock(@LockId);";
            var syncParam = syncCmd.CreateParameter();
            syncParam.ParameterName = "@LockId";
            syncParam.Value = lockId;
            syncCmd.Parameters.Add(syncParam);

            var syncScalar = syncCmd.ExecuteScalar();
            var syncAcquired = syncScalar is true || (syncScalar is bool syncB && syncB);

            if (syncAcquired)
            {
                logger.LogInformation("Successfully acquired transaction-bound advisory lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                return Result<IDistributedLockHandle>.Success(new NoOpAsyncDisposable(resourceId, lockId));
            }

            logger.LogWarning("Failed to acquire transaction-bound advisory lock for resource '{ResourceId}'. Already held.", resourceId);
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "already_held");
            return DistributedLockErrors.LockAlreadyHeld;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
            return DistributedLockErrors.Canceled;
        }
    }

    /// <summary>
    /// Acquires a PostgreSQL advisory lock bound directly to an active database transaction, waiting until the lock is granted.
    /// The lock is automatically released when the transaction is committed or rolled back.
    /// </summary>
    /// <param name="transaction">The active database transaction to which the lock will be bound.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, an error result containing
    /// <see cref="DistributedLockErrors.Canceled"/>.
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

        var connection = transaction.Connection ?? throw new InvalidOperationException("The transaction connection is null or closed.");
        var lockId = PostgresDistributedLockProvider.GenerateLockId(resourceId);

        try
        {
            if (connection is DbConnection dbConn && transaction is DbTransaction dbTran)
            {
                await using var cmd = dbConn.CreateCommand();
                cmd.Transaction = dbTran;
                cmd.CommandText = "SELECT pg_advisory_xact_lock(@LockId);";
                var param = cmd.CreateParameter();
                param.ParameterName = "@LockId";
                param.Value = lockId;
                cmd.Parameters.Add(param);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                logger.LogInformation("Successfully acquired blocking transaction-bound advisory lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                return Result<IDistributedLockHandle>.Success(new NoOpAsyncDisposable(resourceId, lockId));
            }

            using var syncCmd = connection.CreateCommand();
            syncCmd.Transaction = transaction;
            syncCmd.CommandText = "SELECT pg_advisory_xact_lock(@LockId);";
            var syncParam = syncCmd.CreateParameter();
            syncParam.ParameterName = "@LockId";
            syncParam.Value = lockId;
            syncCmd.Parameters.Add(syncParam);

            syncCmd.ExecuteNonQuery();

            logger.LogInformation("Successfully acquired blocking transaction-bound advisory lock for resource '{ResourceId}'.", resourceId);
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
            return Result<IDistributedLockHandle>.Success(new NoOpAsyncDisposable(resourceId, lockId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
            return DistributedLockErrors.Canceled;
        }
    }
}
