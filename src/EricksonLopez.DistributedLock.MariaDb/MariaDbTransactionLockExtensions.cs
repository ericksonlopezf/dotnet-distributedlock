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

namespace EricksonLopez.DistributedLock.MariaDb;

/// <summary>
/// Provides extension methods for acquiring MariaDB locks within active database transactions.
/// </summary>
public static class MariaDbTransactionLockExtensions
{
    /// <summary>
    /// Attempts to acquire an exclusive transaction-scoped lock on the specified MariaDB transaction immediately.
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
        var lockResource = NormalizeResourceKey(resourceId);

        if (connection is DbConnection dbConn)
        {
            await using var cmd = dbConn.CreateCommand();
            cmd.Transaction = (DbTransaction)transaction;
            cmd.CommandText = "SELECT GET_LOCK(@Resource, 0);";

            var p = cmd.CreateParameter();
            p.ParameterName = "@Resource";
            p.Value = lockResource;
            cmd.Parameters.Add(p);

            var res = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (res is not null && !Convert.IsDBNull(res) && Convert.ToInt32(res, CultureInfo.InvariantCulture) == 1)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Acquired transaction-scoped lock for resource '{ResourceId}'.", resourceId);
                }
                return Result<IDistributedLockHandle>.Success(new MariaDbTransactionLockHandle(resourceId, connection, lockResource, logger));
            }

            return DistributedLockErrors.LockAlreadyHeld;
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.Transaction = transaction;
        syncCmd.CommandText = "SELECT GET_LOCK(@Resource, 0);";

        var sp = syncCmd.CreateParameter();
        sp.ParameterName = "@Resource";
        sp.Value = lockResource;
        syncCmd.Parameters.Add(sp);

        var sres = syncCmd.ExecuteScalar();
        if (sres is not null && !Convert.IsDBNull(sres) && Convert.ToInt32(sres, CultureInfo.InvariantCulture) == 1)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Acquired transaction-scoped lock for resource '{ResourceId}'.", resourceId);
            }
            return Result<IDistributedLockHandle>.Success(new MariaDbTransactionLockHandle(resourceId, connection, lockResource, logger));
        }

        return DistributedLockErrors.LockAlreadyHeld;
    }

    /// <summary>
    /// Acquires an exclusive transaction-scoped lock on the specified MariaDB transaction, blocking until granted.
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
        var lockResource = NormalizeResourceKey(resourceId);
        const int infiniteTimeout = int.MaxValue;

        if (connection is DbConnection dbConn)
        {
            await using var cmd = dbConn.CreateCommand();
            cmd.Transaction = (DbTransaction)transaction;
            cmd.CommandText = "SELECT GET_LOCK(@Resource, @Timeout);";

            var pRes = cmd.CreateParameter();
            pRes.ParameterName = "@Resource";
            pRes.Value = lockResource;
            cmd.Parameters.Add(pRes);

            var pTimeout = cmd.CreateParameter();
            pTimeout.ParameterName = "@Timeout";
            pTimeout.Value = infiniteTimeout;
            cmd.Parameters.Add(pTimeout);

            var res = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (res is not null && !Convert.IsDBNull(res) && Convert.ToInt32(res, CultureInfo.InvariantCulture) == 1)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Acquired transaction-scoped lock for resource '{ResourceId}'.", resourceId);
                }
                return Result<IDistributedLockHandle>.Success(new MariaDbTransactionLockHandle(resourceId, connection, lockResource, logger));
            }

            return DistributedLockErrors.LockAlreadyHeld;
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.Transaction = transaction;
        syncCmd.CommandText = "SELECT GET_LOCK(@Resource, @Timeout);";

        var sp = syncCmd.CreateParameter();
        sp.ParameterName = "@Resource";
        sp.Value = lockResource;
        syncCmd.Parameters.Add(sp);

        var sTimeout = syncCmd.CreateParameter();
        sTimeout.ParameterName = "@Timeout";
        sTimeout.Value = infiniteTimeout;
        syncCmd.Parameters.Add(sTimeout);

        var sres = syncCmd.ExecuteScalar();
        if (sres is not null && !Convert.IsDBNull(sres) && Convert.ToInt32(sres, CultureInfo.InvariantCulture) == 1)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Acquired transaction-scoped lock for resource '{ResourceId}'.", resourceId);
            }
            return Result<IDistributedLockHandle>.Success(new MariaDbTransactionLockHandle(resourceId, connection, lockResource, logger));
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

    private static string NormalizeResourceKey(string resourceId)
    {
        if (resourceId.Length <= 64)
        {
            return resourceId;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(resourceId));
        return Convert.ToHexString(hash);
    }

    private sealed class MariaDbTransactionLockHandle : IDistributedLockHandle
    {
        private readonly IDbConnection _connection;
        private readonly string _lockResource;
        private readonly ILogger _logger;
        private int _isDisposed;

        public MariaDbTransactionLockHandle(string resourceId, IDbConnection connection, string lockResource, ILogger logger)
        {
            ResourceId = resourceId;
            LockId = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(resourceId)), 0);
            _connection = connection;
            _lockResource = lockResource;
            _logger = logger;
        }

        public string ResourceId { get; }
        public long LockId { get; }
        public CancellationToken HandleLostToken => CancellationToken.None;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    if (_connection is DbConnection dbConn)
                    {
                        await using var cmd = dbConn.CreateCommand();
                        cmd.CommandText = "SELECT RELEASE_LOCK(@Resource);";
                        var p = cmd.CreateParameter();
                        p.ParameterName = "@Resource";
                        p.Value = _lockResource;
                        cmd.Parameters.Add(p);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        using var cmd = _connection.CreateCommand();
                        cmd.CommandText = "SELECT RELEASE_LOCK(@Resource);";
                        var p = cmd.CreateParameter();
                        p.ParameterName = "@Resource";
                        p.Value = _lockResource;
                        cmd.Parameters.Add(p);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing MariaDB lock for resource '{ResourceId}'.", ResourceId);
            }
        }
    }
}

