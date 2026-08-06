using System.Collections.Concurrent;
using ProxyEdu.Shared.Models;

namespace ProxyEdu.Server.Services;

/// <summary>
/// Buffer de atualizações de alunos em memória.
/// Em vez de escrever no LiteDB a cada requisição HTTP,
/// acumula mudanças em ConcurrentDictionary e persiste em lote a cada N segundos.
/// </summary>
public class StudentUpdateBuffer : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private const int MaxBatchSize = 100;

    private readonly DatabaseService _db;
    private readonly ILogger<StudentUpdateBuffer> _logger;
    private readonly RuntimeDiagnostics _diagnostics;

    // Buffer de alunos com dados atualizados (lock-free)
    private readonly ConcurrentDictionary<string, BufferedStudent> _buffer = new(StringComparer.OrdinalIgnoreCase);

    // Contadores para diagnóstico
    private long _totalUpdatesReceived;
    private long _totalUpdatesFlushed;

    public StudentUpdateBuffer(DatabaseService db, ILogger<StudentUpdateBuffer> logger, RuntimeDiagnostics diagnostics)
    {
        _db = db;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Acumula uma atualização de aluno no buffer.
    /// Thread-safe e lock-free.
    /// </summary>
    public void BufferUpdate(StudentInfo student)
    {
        if (student == null || string.IsNullOrWhiteSpace(student.Id))
            return;

        var key = student.Id;

        // Atualiza ou cria entrada no buffer
        _buffer.AddOrUpdate(key,
            addValue: new BufferedStudent
            {
                Student = student,
                LastActivity = DateTime.UtcNow,
                TouchCount = 1
            },
            updateValueFactory: (_, existing) =>
            {
                // Preserva o Student do banco, mas atualiza campos voláteis
                var target = existing.Student;

                // Copia apenas campos que mudam frequentemente
                target.LastSeen = student.LastSeen;
                target.CurrentUrl = student.CurrentUrl;
                target.TotalRequests = student.TotalRequests;
                target.BytesTransferred = student.BytesTransferred;
                target.BlockedRequests = student.BlockedRequests;
                target.IsConnected = student.IsConnected;

                return new BufferedStudent
                {
                    Student = target,
                    LastActivity = DateTime.UtcNow,
                    TouchCount = existing.TouchCount + 1
                };
            });

        Interlocked.Increment(ref _totalUpdatesReceived);
    }

    /// <summary>
    /// Obtém um aluno do buffer (se existir) ou do banco.
    /// </summary>
    public StudentInfo? GetStudent(string studentId)
    {
        if (_buffer.TryGetValue(studentId, out var buffered))
        {
            return buffered.Student;
        }
        return _db.Students.FindById(studentId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "StudentUpdateBuffer iniciado. Intervalo de flush: {Interval}s",
            FlushInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, stoppingToken);
                await FlushBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao processar buffer de alunos");
            }
        }

        // Flush final ao desligar
        await FlushBatchAsync(CancellationToken.None);
    }

    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        if (_buffer.IsEmpty)
            return;

        // Coleta lote atual
        var batch = new List<BufferedStudent>(Math.Min(_buffer.Count, MaxBatchSize));
        var keysToRemove = new List<string>();

        foreach (var kvp in _buffer)
        {
            if (batch.Count >= MaxBatchSize)
                break;

            batch.Add(kvp.Value);
            keysToRemove.Add(kvp.Key);
        }

        if (batch.Count == 0)
            return;

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await Task.Run(() =>
            {
                foreach (var buffered in batch)
                {
                    // O snapshot do buffer pode ser anterior a uma ação do professor
                    // (bloqueio, liberação total ou acesso temporário). Persiste somente
                    // os campos de atividade para não desfazer essas ações de controle.
                    var current = _db.Students.FindById(buffered.Student.Id);
                    if (current == null)
                        continue;

                    current.LastSeen = buffered.Student.LastSeen;
                    current.CurrentUrl = buffered.Student.CurrentUrl;
                    current.TotalRequests = buffered.Student.TotalRequests;
                    current.BytesTransferred = buffered.Student.BytesTransferred;
                    current.BlockedRequests = buffered.Student.BlockedRequests;
                    current.IsConnected = buffered.Student.IsConnected;
                    _db.Students.Update(current);
                    Interlocked.Increment(ref _totalUpdatesFlushed);
                }
            }, cancellationToken);
            _diagnostics.RecordLiteWrite(stopwatch.Elapsed, batch.Count);

            // Remove apenas itens que foram persistidos com sucesso
            foreach (var key in keysToRemove)
            {
                _buffer.TryRemove(key, out _);
            }

            _logger.LogDebug(
                "StudentUpdateBuffer: {Count} alunos persistidos (buffer: {BufferSize}, total: {Total})",
                batch.Count,
                _buffer.Count,
                _totalUpdatesFlushed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao persistir lote de {Count} alunos", batch.Count);
        }
    }

    public StudentBufferStats GetStats()
    {
        return new StudentBufferStats
        {
            BufferSize = _buffer.Count,
            TotalUpdatesReceived = Interlocked.Read(ref _totalUpdatesReceived),
            TotalUpdatesFlushed = Interlocked.Read(ref _totalUpdatesFlushed),
            FlushIntervalSeconds = (int)FlushInterval.TotalSeconds
        };
    }

    private class BufferedStudent
    {
        public StudentInfo Student { get; set; } = null!;
        public DateTime LastActivity { get; set; }
        public int TouchCount { get; set; }
    }
}

public class StudentBufferStats
{
    public int BufferSize { get; set; }
    public long TotalUpdatesReceived { get; set; }
    public long TotalUpdatesFlushed { get; set; }
    public int FlushIntervalSeconds { get; set; }
}

