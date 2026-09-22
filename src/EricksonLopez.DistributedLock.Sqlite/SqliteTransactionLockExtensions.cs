// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.Sqlite;

/// <summary>
/// Provides extension methods for acquiring transaction-bound SQLite locks within active database transactions.
/// </summary>
public static class SqliteTransactionLockExtensions
{
    /// <summary>
    /// Attempts to acquire an exclusive lock bound directly to an active <see cref="IDbTransaction"/>.
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
        var ownerId = Guid.NewGuid().ToString("N");

        if (connection is DbConnection dbConn)
        {
            await using var initCmd = dbConn.CreateCommand();
            initCmd.Transaction = (DbTransaction)transaction;
            initCmd.CommandText = "CREATE TABLE IF NOT EXISTS __distributed_locks (resource_id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, acquired_at TEXT NOT NULL);";
            await initCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await using var cmd = dbConn.CreateCommand();
                cmd.Transaction = (DbTransaction)transaction;
                cmd.CommandText = "INSERT INTO __distributed_locks (resource_id, owner_id, acquired_at) VALUES (@ResourceId, @OwnerId, datetime('now'));";

                var pRes = cmd.CreateParameter();
                pRes.ParameterName = "@ResourceId";
                pRes.Value = resourceId;
                cmd.Parameters.Add(pRes);

                var pOwner = cmd.CreateParameter();
                pOwner.ParameterName = "@OwnerId";
                pOwner.Value = ownerId;
                cmd.Parameters.Add(pOwner);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Acquired transaction-scoped SQLite lock for resource '{ResourceId}'.", resourceId);
                return Result<IDistributedLockHandle>.Success(new SqliteTransactionLockHandle(resourceId, connection, ownerId, logger));
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                return DistributedLockErrors.LockAlreadyHeld;
            }
        }

        using var syncInit = connection.CreateCommand();
        syncInit.Transaction = transaction;
        syncInit.CommandText = "CREATE TABLE IF NOT EXISTS __distributed_locks (resource_id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, acquired_at TEXT NOT NULL);";
        syncInit.ExecuteNonQuery();

        try
        {
            using var syncCmd = connection.CreateCommand();
            syncCmd.Transaction = transaction;
            syncCmd.CommandText = "INSERT INTO __distributed_locks (resource_id, owner_id, acquired_at) VALUES (@ResourceId, @OwnerId, datetime('now'));";

            var spRes = syncCmd.CreateParameter();
            spRes.ParameterName = "@ResourceId";
            spRes.Value = resourceId;
            syncCmd.Parameters.Add(spRes);

            var spOwner = syncCmd.CreateParameter();
            spOwner.ParameterName = "@OwnerId";
            spOwner.Value = ownerId;
            syncCmd.Parameters.Add(spOwner);

            syncCmd.ExecuteNonQuery();
            logger.LogInformation("Acquired transaction-scoped SQLite lock for resource '{ResourceId}'.", resourceId);
            return Result<IDistributedLockHandle>.Success(new SqliteTransactionLockHandle(resourceId, connection, ownerId, logger));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return DistributedLockErrors.LockAlreadyHeld;
        }
    }

    /// <summary>
    /// Acquires an exclusive lock bound directly to an active <see cref="IDbTransaction"/>, waiting until the lock is granted.
    /// </summary>
    /// <param name="transaction">The active database transaction to which the lock will be bound.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    /// <exception cref="InvalidOperationException">The connection associated with <paramref name="transaction"/> is <see langword="null"/></exception>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="cancellationToken"/></exception>
    public static async Task<Result<IDistributedLockHandle>> AcquireInTransactionAsync(
        this IDbTransaction transaction,
        string resourceId,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attempt = await TryAcquireInTransactionAsync(transaction, resourceId, logger, cancellationToken).ConfigureAwait(false);
            if (attempt.IsSuccess)
            {
                return attempt;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Attempts to acquire an exclusive lock bound directly to an active <see cref="DbTransaction"/>.
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
    /// Acquires an exclusive lock bound directly to an active <see cref="DbTransaction"/>, waiting until the lock is granted.
    /// </summary>
    /// <param name="transaction">The active database transaction to which the lock will be bound.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a successful <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="transaction"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    /// <exception cref="InvalidOperationException">The connection associated with <paramref name="transaction"/> is <see langword="null"/></exception>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="cancellationToken"/></exception>
    public static Task<Result<IDistributedLockHandle>> AcquireInTransactionAsync(
        this DbTransaction transaction,
        string resourceId,
        ILogger logger,
        CancellationToken cancellationToken = default)
        => AcquireInTransactionAsync((IDbTransaction)transaction, resourceId, logger, cancellationToken);

    private sealed class SqliteTransactionLockHandle : IDistributedLockHandle
    {
        private readonly string _resourceId;
        private readonly IDbConnection _connection;
        private readonly string _ownerId;
        private readonly ILogger _logger;
        private int _isDisposed;

        public SqliteTransactionLockHandle(string resourceId, IDbConnection connection, string ownerId, ILogger logger)
        {
            _resourceId = resourceId;
            LockId = BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(resourceId)), 0);
            _connection = connection;
            _ownerId = ownerId;
            _logger = logger;
        }

        public string ResourceId => _resourceId;
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
                        cmd.CommandText = "DELETE FROM __distributed_locks WHERE resource_id = @ResourceId AND owner_id = @OwnerId;";
                        var p = cmd.CreateParameter();
                        p.ParameterName = "@ResourceId";
                        p.Value = _resourceId;
                        cmd.Parameters.Add(p);

                        var po = cmd.CreateParameter();
                        po.ParameterName = "@OwnerId";
                        po.Value = _ownerId;
                        cmd.Parameters.Add(po);

                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        using var cmd = _connection.CreateCommand();
                        cmd.CommandText = "DELETE FROM __distributed_locks WHERE resource_id = @ResourceId AND owner_id = @OwnerId;";
                        var p = cmd.CreateParameter();
                        p.ParameterName = "@ResourceId";
                        p.Value = _resourceId;
                        cmd.Parameters.Add(p);

                        var po = cmd.CreateParameter();
                        po.ParameterName = "@OwnerId";
                        po.Value = _ownerId;
                        cmd.Parameters.Add(po);

                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing SQLite transaction lock for resource '{ResourceId}'.", _resourceId);
            }
        }
    }
}
