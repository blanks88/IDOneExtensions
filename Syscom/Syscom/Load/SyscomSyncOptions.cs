namespace Syscom.Load;

public sealed class SyscomSyncOptions
{
    public TimeSpan MaximumDuration { get; init; } = TimeSpan.FromMinutes(30);
    public int RetentionDays { get; init; } = 30;
    public long CompanyId { get; init; } = 1;
    public int PageSize { get; init; } = 500;
    public int BufferedPages { get; init; } = 2;
}
