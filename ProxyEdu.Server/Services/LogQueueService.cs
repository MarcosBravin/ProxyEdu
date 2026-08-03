using System.Collections.Concurrent;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Services;

/// <summary>
/// Serviço de fila de logs em memória.
/// Acumula logs em um buffer e escreve em lote a cada N segundos,
/// evitando sobrecarga no LiteDB a cada requisição.
/// </summary>
public class LogQueueService : BackgroundService
{
    private readonly ConcurrentQueue<AccessLog> _queue = new();
    private readonly DatabaseService _db;
    private readonly ILogger<LogQueueService> _logger;
    private readonly RuntimeDiagnostics _diagnostics;
    private readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(5);
    private const int MaxBatchSize = 500;
    private const int MaxQueueSize = 10_000;
    private int _queueSize;

    // Contadores para diagnóstico
    private long _totalQueued;
    private long _totalFlushed;
    private long _totalDiscarded;

    public LogQueueService(DatabaseService db, ILogger<LogQueueService> logger, RuntimeDiagnostics diagnostics)
    {
        _db = db;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Enfileira um log para escrita assíncrona.
    /// Thread-safe e lock-free.
    /// </summary>
    public void Enqueue(AccessLog log)
    {
        if (log == null) return;

        _queue.Enqueue(log);
        Interlocked.Increment(ref _queueSize);
        Interlocked.Increment(ref _totalQueued);

        // Se a fila crescer demais, descarta logs mais antigos
        // para evitar memory leak em cenários de pico extremo
        while (Volatile.Read(ref _queueSize) > MaxQueueSize && _queue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _queueSize);
            Interlocked.Increment(ref _totalDiscarded);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "LogQueueService iniciado. Intervalo de flush: {Interval}s",
            _flushInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_flushInterval, stoppingToken);
                await FlushBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar fila de logs");
            }
        }

        // Flush final ao desligar
        await FlushBatchAsync(CancellationToken.None);
    }

    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        if (_queue.IsEmpty)
            return;

        var batch = new List<AccessLog>(Math.Min(_queue.Count, MaxBatchSize));

        // Coleta lote atual
        while (batch.Count < MaxBatchSize && _queue.TryDequeue(out var log))
        {
            Interlocked.Decrement(ref _queueSize);
            batch.Add(log);
        }

        if (batch.Count == 0)
            return;

        try
        {
            // Usar Task.Run para não bloquear o loop principal
            // LiteDB não é totalmente async, mas escrevemos em lote
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Task.Run(() =>
            {
                _db.Logs.InsertBulk(batch);
                Interlocked.Add(ref _totalFlushed, batch.Count);
            }, cancellationToken);
            _diagnostics.RecordLiteWrite(stopwatch.Elapsed, batch.Count);

            _logger.LogDebug(
                "LogQueue: {Count} logs escritos (fila: {QueueSize}, total: {Total})",
                batch.Count,
                Volatile.Read(ref _queueSize),
                _totalFlushed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao escrever lote de {Count} logs", batch.Count);

            // Re-enfileirar logs que falharam (exceto se fila estiver enorme)
            foreach (var log in batch)
            {
                if (Volatile.Read(ref _queueSize) < MaxQueueSize)
                {
                    _queue.Enqueue(log);
                    Interlocked.Increment(ref _queueSize);
                }
            }
        }
    }

    /// <summary>
    /// Estatísticas da fila para diagnóstico no dashboard
    /// </summary>
    public LogQueueStats GetStats()
    {
        return new LogQueueStats
        {
            QueueSize = Volatile.Read(ref _queueSize),
            TotalQueued = Interlocked.Read(ref _totalQueued),
            TotalFlushed = Interlocked.Read(ref _totalFlushed),
            TotalDiscarded = Interlocked.Read(ref _totalDiscarded),
            MaxBatchSize = MaxBatchSize,
            FlushIntervalSeconds = (int)_flushInterval.TotalSeconds
        };
    }
}

public class LogQueueStats
{
    public int QueueSize { get; set; }
    public long TotalQueued { get; set; }
    public long TotalFlushed { get; set; }
    public long TotalDiscarded { get; set; }
    public int MaxBatchSize { get; set; }
    public int FlushIntervalSeconds { get; set; }
}

