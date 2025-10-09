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
Console.CancelKeyPress += (_, e) =>
{
    // Prevent the process from terminating immediately.
    e.Cancel = true;
    cts.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    // Ensure we try to cancel ongoing work on SIGTERM (e.g., Docker stop)
    if (!cts.IsCancellationRequested)
        cts.Cancel();
};

try
{
    await worker.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // graceful shutdown
}
finally
{
    (Log.Logger as IDisposable)?.Dispose();
}

return;

static IDOneDbContext BuildContext()
{
    //const string cs = "User ID=blanks88;Host=localhost;Port=5432;Database=one_development;Include Error Detail=true";
    const string cs = "User ID=ucq2a6a2im22q7;Password=pae331ddf86eea08f7b4a031ac422058b0c76e3952d8a293761667d09cb8cd437;Host=ec2-3-211-36-220.compute-1.amazonaws.com;Port=5432;Database=dfcb63f4c9513c;Include Error Detail=true";
    var builder = new DbContextOptionsBuilder<IDOneDbContext>();
    builder.UseNpgsql(cs);
    return new IDOneDbContext(builder.Options);
}