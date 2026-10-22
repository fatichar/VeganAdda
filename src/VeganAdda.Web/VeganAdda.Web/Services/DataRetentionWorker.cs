using Microsoft.EntityFrameworkCore;
using VeganAdda.Web.Data;

namespace VeganAdda.Web.Services;

public sealed class DataRetentionWorker(IServiceScopeFactory scopes, ILogger<DataRetentionWorker> logger, IConfiguration config) : BackgroundService
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
        while (!stoppingToken.IsCancellationRequested)
        {
            var expired = await db.Reports
                .Where(report => report.Resolved && report.ResolvedAt != null && report.ResolvedAt <= cutoff)
                .OrderBy(report => report.Id).Take(100)
                .Select(report => new { report.Id, report.Screenshot }).ToListAsync(stoppingToken);
            if (expired.Count == 0) break;
            var ids = expired.Select(report => report.Id).ToArray();
            await db.Reports.Where(report => ids.Contains(report.Id)).ExecuteDeleteAsync(stoppingToken);
            foreach (var screenshot in expired.Select(report => report.Screenshot).OfType<string>().Distinct())
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(screenshot, @"^/report-media/[a-f0-9]{32}\.jpg$")
                    || await db.Reports.AnyAsync(report => report.Screenshot == screenshot, stoppingToken)) continue;
                var file = Path.Combine(config["Storage:MediaPath"] ?? "uploads", "reports", screenshot[14..]);
                File.Delete(file);
                var directory = Path.GetDirectoryName(file)!;
                if (Directory.Exists(directory))
                    foreach (var marker in Directory.EnumerateFiles(directory, Path.GetFileName(file) + ".owner-*")) File.Delete(marker);
            }
        }
    }
}
