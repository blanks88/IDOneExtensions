using IDOneRepository;
using IDOneRepository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using Serilog;
using Serilog.Events;
using Syscom;
using Syscom.Load;

SyscomAppConfiguration configuration;
try
{
    configuration = SyscomAppConfiguration.FromEnvironment();
}
catch (SyscomAppConfigurationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(LogEventLevel.Information)
    .WriteTo.Console()
    .CreateLogger();

try
{
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.ClearProviders();
        builder.AddSerilog(Log.Logger, dispose: false);
    });
    using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    var clientOptions = new SyscomClientOptions(configuration.ClientId, configuration.ClientSecret)
    {
        BaseUrl = configuration.BaseUrl,
        OAuthTokenPath = configuration.OAuthTokenPath,
        UseFormEncodedTokenRequest = configuration.UseFormEncodedTokenRequest,
        RequestTimeout = configuration.RequestTimeout
    };
    var client = new SyscomClient(httpClient, clientOptions);
    var dbOptions = new DbContextOptionsBuilder<IDOneDbContext>()
        .UseNpgsql(configuration.ConnectionString)
        .Options;
    await using var unitOfWork = new UnitOfWork(new IDOneDbContext(dbOptions));
    var worker = new SyscomWorker(loggerFactory, client, unitOfWork, configuration.ToSyncOptions());

    using var cts = new CancellationTokenSource();
    var cancellationLock = new object();
    var shuttingDown = false;
    ConsoleCancelEventHandler cancelKeyPress = (_, e) =>
    {
        lock (cancellationLock)
        {
            if (shuttingDown) return;
            e.Cancel = true;
            cts.Cancel();
        }
    };
    EventHandler processExit = (_, _) =>
    {
        lock (cancellationLock)
        {
            if (!shuttingDown && !cts.IsCancellationRequested)
                cts.Cancel();
        }
    };
    PosixSignalRegistration? sigtermRegistration = null;
    if (!OperatingSystem.IsWindows())
    {
        sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            lock (cancellationLock)
            {
                if (shuttingDown) return;
                context.Cancel = true;
                cts.Cancel();
            }
        });
    }
    Console.CancelKeyPress += cancelKeyPress;
    AppDomain.CurrentDomain.ProcessExit += processExit;

    try
    {
        await worker.RunAsync(cts.Token);
        return 0;
    }
    catch (SyscomSyncAlreadyRunningException)
    {
        Console.Error.WriteLine("SYSCOM synchronization is already running.");
        return 4;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("SYSCOM synchronization was cancelled.");
        return 3;
    }
    catch (Exception ex)
    {
        // Provider exceptions may contain connection strings, responses or request bodies.
        Console.Error.WriteLine($"SYSCOM synchronization failed ({ex.GetType().Name}).");
        return 1;
    }
    finally
    {
        lock (cancellationLock)
        {
            // Drain active callbacks before disposing the cancellation source.
            shuttingDown = true;
            Console.CancelKeyPress -= cancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= processExit;
        }
        sigtermRegistration?.Dispose();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SYSCOM startup failed ({ex.GetType().Name}).");
    return 1;
}
finally
{
    (Log.Logger as IDisposable)?.Dispose();
}
