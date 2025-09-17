using IDOneRepository.Data;
using IDOneRepository.Data.Entities;
using IDOneRepository.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace IDOneRepository.UnitOfWork;

public interface IUnitOfWork : IAsyncDisposable
{
    IRepository<PortfolioProduct> PortfolioProducts { get; }
    IRepository<CatalogsSatProduct> CatalogSatProducts { get; }
    IRepository<CatalogsMeasurement> CatalogMeasurements { get; }
    IRepository<Currency> Currencies { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
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

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

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