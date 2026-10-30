// See https://aka.ms/new-console-template for more information

using IDOneRepository;
using IDOneRepository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Syscom;
using Syscom.Load;
using Serilog;
using Serilog.Events;

using var httpClient = new HttpClient();
var client = new SyscomClient(httpClient,
    new SyscomClientOptions("s478EpzmgpnoaIw7Q3YVAdLpaEAF3h8L",
        "RPFBY1PHDosiHRpy5K5CGQTxmwufB1ZrJMKx5JRn")
);
var unitOfWork = new UnitOfWork(BuildContext());

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(LogEventLevel.Debug)
    .WriteTo.Console()
    .CreateLogger();

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders();
    builder.AddSerilog(Log.Logger, true);
});
var worker = new SyscomWorker(loggerFactory, client, unitOfWork);

using var cts = new CancellationTokenSource();
var cancellationLock = new object();
var shuttingDown = false;
ConsoleCancelEventHandler cancelKeyPress = (_, e) =>
{
    lock (cancellationLock)
    {
        if (shuttingDown) return;
        // Prevent the process from terminating immediately while work is active.
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
Console.CancelKeyPress += cancelKeyPress;
AppDomain.CurrentDomain.ProcessExit += processExit;

try
{
    await worker.RunAsync(cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    // graceful shutdown
}
finally
{
    lock (cancellationLock)
    {
        // Drain active callbacks and make already-dispatched callbacks harmless
        // before the using declaration disposes the token source.
        shuttingDown = true;
        Console.CancelKeyPress -= cancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= processExit;
    }
    (Log.Logger as IDisposable)?.Dispose();
}

return;

static IDOneDbContext BuildContext()
{
    //const string cs = "User ID=blanks88;Host=localhost;Port=5432;Database=one_development;Include Error Detail=true";
    const string cs = "User ID=ucdisfaoob8pnn;Password=p630924e316b7a4b8a0824397c3c1dfec037c1eaf6a700219ff5b4f16b8aaf948;Host=ec2-32-195-227-65.compute-1.amazonaws.com;Port=5432;Database=dfcb63f4c9513c;Include Error Detail=true";
    var builder = new DbContextOptionsBuilder<IDOneDbContext>();
    builder.UseNpgsql(cs);
    return new IDOneDbContext(builder.Options);
}