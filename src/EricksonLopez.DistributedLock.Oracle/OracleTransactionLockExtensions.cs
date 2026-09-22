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

namespace EricksonLopez.DistributedLock.Oracle;

/// <summary>
/// Provides extension methods for acquiring Oracle DBMS_LOCK locks within active database transactions.
/// </summary>
public static class OracleTransactionLockExtensions
{
    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on the specified Oracle transaction immediately.
    /// </summary>
    /// <param name="transaction">The active database transaction to bind the lock to.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if the lock was acquired;
    /// otherwise, a failure with <see cref="DistributedLockErrors.LockAlreadyHeld"/>.
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
        var lockName = NormalizeLockName(resourceId);

        if (connection is DbConnection dbConn)
        {
            await using var cmd = dbConn.CreateCommand();
            cmd.Transaction = (DbTransaction)transaction;
            cmd.CommandText = """
                DECLARE
                    v_handle VARCHAR2(128);
                BEGIN
                    DBMS_LOCK.ALLOCATE_UNIQUE(:LockName, v_handle);
                    :OutStatus := DBMS_LOCK.REQUEST(v_handle, 6, 0, TRUE);
                END;
                """;

            var pName = cmd.CreateParameter();
            pName.ParameterName = "LockName";
            pName.Value = lockName;
            cmd.Parameters.Add(pName);

            var pOutStatus = cmd.CreateParameter();
            pOutStatus.ParameterName = "OutStatus";
            pOutStatus.Direction = ParameterDirection.Output;
            pOutStatus.DbType = DbType.Int32;
            cmd.Parameters.Add(pOutStatus);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var status = pOutStatus.Value is int s ? s : Convert.ToInt32(pOutStatus.Value, CultureInfo.InvariantCulture);

            if (status == 0)
            {
                logger.LogInformation("Acquired transaction-scoped Oracle lock for resource '{ResourceId}'.", resourceId);
                return Result<IDistributedLockHandle>.Success(new OracleTransactionLockHandle(resourceId));
            }

            logger.LogWarning("Failed to acquire transaction lock for resource '{ResourceId}'. Status: {Status}.", resourceId, status);
            return DistributedLockErrors.LockAlreadyHeld;
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.Transaction = transaction;
        syncCmd.CommandText = """
            DECLARE
                v_handle VARCHAR2(128);
            BEGIN
                DBMS_LOCK.ALLOCATE_UNIQUE(:LockName, v_handle);
                :OutStatus := DBMS_LOCK.REQUEST(v_handle, 6, 0, TRUE);
            END;
            """;

        var spName = syncCmd.CreateParameter();
        spName.ParameterName = "LockName";
        spName.Value = lockName;
        syncCmd.Parameters.Add(spName);

        var spOutStatus = syncCmd.CreateParameter();
        spOutStatus.ParameterName = "OutStatus";
        spOutStatus.Direction = ParameterDirection.Output;
        spOutStatus.DbType = DbType.Int32;
        syncCmd.Parameters.Add(spOutStatus);

        syncCmd.ExecuteNonQuery();
        var syncStatus = spOutStatus.Value is int ss ? ss : Convert.ToInt32(spOutStatus.Value, CultureInfo.InvariantCulture);

        if (syncStatus == 0)
        {
            logger.LogInformation("Acquired transaction-scoped Oracle lock for resource '{ResourceId}'.", resourceId);
            return Result<IDistributedLockHandle>.Success(new OracleTransactionLockHandle(resourceId));
        }

        return DistributedLockErrors.LockAlreadyHeld;
    }

    /// <summary>
    /// Acquires an exclusive transaction-scoped lock on the specified Oracle transaction, blocking until granted.
    /// </summary>
    /// <param name="transaction">The active database transaction to bind the lock to.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, the acquisition error.
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
        var lockName = NormalizeLockName(resourceId);

        const int maxWaitSeconds = 32767;

        if (connection is DbConnection dbConn)
        {
            await using var cmd = dbConn.CreateCommand();
            cmd.Transaction = (DbTransaction)transaction;
            cmd.CommandText = """
                DECLARE
                    v_handle VARCHAR2(128);
                BEGIN
                    DBMS_LOCK.ALLOCATE_UNIQUE(:LockName, v_handle);
                    :OutStatus := DBMS_LOCK.REQUEST(v_handle, 6, :Timeout, TRUE);
                END;
                """;

            var pName = cmd.CreateParameter();
            pName.ParameterName = "LockName";
            pName.Value = lockName;
            cmd.Parameters.Add(pName);

            var pTimeout = cmd.CreateParameter();
            pTimeout.ParameterName = "Timeout";
            pTimeout.Value = maxWaitSeconds;
            cmd.Parameters.Add(pTimeout);

            var pOutStatus = cmd.CreateParameter();
            pOutStatus.ParameterName = "OutStatus";
            pOutStatus.Direction = ParameterDirection.Output;
            pOutStatus.DbType = DbType.Int32;
            cmd.Parameters.Add(pOutStatus);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            var status = pOutStatus.Value is int s ? s : Convert.ToInt32(pOutStatus.Value, CultureInfo.InvariantCulture);

            if (status == 0)
            {
                logger.LogInformation("Acquired blocking transaction-scoped Oracle lock for resource '{ResourceId}'.", resourceId);
                return Result<IDistributedLockHandle>.Success(new OracleTransactionLockHandle(resourceId));
            }

            return DistributedLockErrors.LockAlreadyHeld;
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.Transaction = transaction;
        syncCmd.CommandText = """
            DECLARE
                v_handle VARCHAR2(128);
            BEGIN
                DBMS_LOCK.ALLOCATE_UNIQUE(:LockName, v_handle);
                :OutStatus := DBMS_LOCK.REQUEST(v_handle, 6, :Timeout, TRUE);
            END;
            """;

        var spName = syncCmd.CreateParameter();
        spName.ParameterName = "LockName";
        spName.Value = lockName;
        syncCmd.Parameters.Add(spName);

        var spTimeout = syncCmd.CreateParameter();
        spTimeout.ParameterName = "Timeout";
        spTimeout.Value = maxWaitSeconds;
        syncCmd.Parameters.Add(spTimeout);

        var spOutStatus = syncCmd.CreateParameter();
        spOutStatus.ParameterName = "OutStatus";
        spOutStatus.Direction = ParameterDirection.Output;
        spOutStatus.DbType = DbType.Int32;
        syncCmd.Parameters.Add(spOutStatus);

        syncCmd.ExecuteNonQuery();
        var syncStatus = spOutStatus.Value is int ss ? ss : Convert.ToInt32(spOutStatus.Value, CultureInfo.InvariantCulture);

        if (syncStatus == 0)
        {
            logger.LogInformation("Acquired blocking transaction-scoped Oracle lock for resource '{ResourceId}'.", resourceId);
            return Result<IDistributedLockHandle>.Success(new OracleTransactionLockHandle(resourceId));
        }

        return DistributedLockErrors.LockAlreadyHeld;
    }

    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on the specified <see cref="DbTransaction"/> immediately.
    /// </summary>
    /// <param name="transaction">The active database transaction to bind the lock to.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if the lock was acquired;
    /// otherwise, a failure with <see cref="DistributedLockErrors.LockAlreadyHeld"/>.
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
    /// Acquires an exclusive transaction-scoped lock on the specified <see cref="DbTransaction"/>, blocking until granted.
    /// </summary>
    /// <param name="transaction">The active database transaction to bind the lock to.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, the acquisition error.
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

    private static string NormalizeLockName(string resourceId)
    {
        if (resourceId.Length <= 128)
        {
            return resourceId;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resourceId));
        return Convert.ToHexString(hash);
    }

    private sealed class OracleTransactionLockHandle : IDistributedLockHandle
    {
        public OracleTransactionLockHandle(string resourceId)
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

