using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

public sealed class DataRetentionWorker(IServiceScopeFactory scopes, ILogger<DataRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeleteExpiredReports(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Data retention cleanup failed: {Type}", ex.GetType().Name);
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DeleteExpiredReports(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunityDb>();
        var cutoff = DateTimeOffset.UtcNow.AddMonths(-12);
        await db.Reports
            .Where(report => report.Resolved && report.ResolvedAt != null && report.ResolvedAt <= cutoff)
            .ExecuteDeleteAsync(stoppingToken);
    }
}
