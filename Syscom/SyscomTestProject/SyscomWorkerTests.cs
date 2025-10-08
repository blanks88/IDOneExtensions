using IDOneRepository;
using IDOneRepository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Syscom;
using Syscom.Load;

namespace SyscomTestProject;

public class SyscomWorkerTests
{
    private readonly SyscomClient _client = new(new HttpClient(),
        new SyscomClientOptions("s478EpzmgpnoaIw7Q3YVAdLpaEAF3h8L",
            "RPFBY1PHDosiHRpy5K5CGQTxmwufB1ZrJMKx5JRn")
    );
    private readonly IUnitOfWork _unitOfWork = new UnitOfWork(BuildContext());
    
    [Fact]
    public async Task RunWorker_SyncsProducts()
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        var worker = new SyscomWorker(loggerFactory, _client, _unitOfWork);

        // act
        await worker.RunAsync(CancellationToken.None);
    }


    private static IDOneDbContext BuildContext()
    {
        // const string cs = "User ID=blanks88;Host=localhost;Port=5432;Database=one_development;Include Error Detail=true";
        const string cs = "User ID=ucq2a6a2im22q7;Password=pae331ddf86eea08f7b4a031ac422058b0c76e3952d8a293761667d09cb8cd437;Host=ec2-3-211-36-220.compute-1.amazonaws.com;Port=5432;Database=dfcb63f4c9513c;Include Error Detail=true";
        var builder = new DbContextOptionsBuilder<IDOneDbContext>();
        builder.UseNpgsql(cs);
        return new IDOneDbContext(builder.Options);
    }   
}