namespace ProxyEdu.Server.Services;

/// <summary>
/// Background service that periodically cleans up old log entries
/// based on the configured retention period.
/// </summary>
public class LogCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LogCleanupService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    public LogCleanupService(
        IServiceProvider serviceProvider,
        ILogger<LogCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Log cleanup service started. Running every {Interval}", CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
                await CleanupOldLogsAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during log cleanup cycle");
            }
        }
    }

    private async Task CleanupOldLogsAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();

                var settings = db.GetSettings();
                var cutoff = DateTime.UtcNow.AddDays(-settings.MaxLogRetentionDays);

                var deleted = db.Logs.DeleteMany(l => l.Timestamp < cutoff);

                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Log cleanup: removed {Count} records older than {Days} days",
                        deleted,
                        settings.MaxLogRetentionDays);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup old logs");
            }
        });
    }
}
