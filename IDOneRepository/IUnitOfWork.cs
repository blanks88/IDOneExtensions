using System.Data.Common;
using IDOneRepository.Data;
using IDOneRepository.Data.Entities;
using IDOneRepository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IDOneRepository;

public interface IUnitOfWork : IAsyncDisposable
{
    IRepository<PortfolioProduct> PortfolioProducts { get; }
    IRepository<CatalogsSatProduct> CatalogSatProducts { get; }
    IRepository<CatalogsMeasurement> CatalogMeasurements { get; }
    IRepository<Currency> Currencies { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // Transactions
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);

    // NEW: Raw SQL execution helpers
    Task<int> ExecuteSqlRawAsync(string sql, List<object>? parameters = null, CancellationToken ct = default);
    Task<int> ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken ct = default);

    // NEW: Safe access to the underlying ADO.NET connection (for COPY, etc.)
    DbConnection GetConnection();
    Task EnsureConnectionOpenAsync(CancellationToken ct = default);
}

public class UnitOfWork : IUnitOfWork
{
    private readonly IDOneDbContext _context;
    private IDbContextTransaction? _currentTransaction;

    public UnitOfWork(IDOneDbContext context)
    {
        _context = context;
        PortfolioProducts = new Repository<PortfolioProduct>(_context);
        CatalogSatProducts = new Repository<CatalogsSatProduct>(_context);
        CatalogMeasurements = new Repository<CatalogsMeasurement>(_context);
        Currencies = new Repository<Currency>(_context);
    }

    public IRepository<PortfolioProduct> PortfolioProducts { get; }
    public IRepository<CatalogsSatProduct> CatalogSatProducts { get; }
    public IRepository<CatalogsMeasurement> CatalogMeasurements { get; }
    public IRepository<Currency> Currencies { get; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction != null) return _currentTransaction;
        _currentTransaction = await _context.Database.BeginTransactionAsync(ct);
        return _currentTransaction;
    }

    public Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction == null) return Task.CompletedTask;
        _currentTransaction.Commit();
        _currentTransaction.Dispose();
        _currentTransaction = null;
        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction == null) return Task.CompletedTask;
        _currentTransaction.Rollback();
        _currentTransaction.Dispose();
        _currentTransaction = null;
        return Task.CompletedTask;
    }

    #region Raw SQL helpers

    public Task<int> ExecuteSqlRawAsync(string sql, List<object>? parameters = null,
        CancellationToken ct = default)
    {
        if (parameters is null || parameters.Count == 0)
            return _context.Database.ExecuteSqlRawAsync(sql, cancellationToken: ct);

        return _context.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), ct);
    }

    public Task<int> ExecuteSqlInterpolatedAsync(FormattableString sql, CancellationToken ct = default)
        => _context.Database.ExecuteSqlInterpolatedAsync(sql, ct);

    #endregion

    #region Connection helpers for bulk/COPY scenarios

    public DbConnection GetConnection() => _context.Database.GetDbConnection();

    public async Task EnsureConnectionOpenAsync(CancellationToken ct = default)
    {
        var conn = GetConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        if (_currentTransaction != null)
        {
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }

        _context.Dispose();
        return default;
    }
}