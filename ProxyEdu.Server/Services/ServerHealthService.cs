using ProxyEdu.Shared.Models;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ProxyEdu.Server.Services;

public class ServerHealthService
{
    private readonly Process _currentProcess;
    private readonly DateTime _startTime;

    public ServerHealthService()
    {
        _currentProcess = Process.GetCurrentProcess();
        _startTime = DateTime.UtcNow;
    }

    public ServerHealthStats GetHealthStats()
    {
        _currentProcess.Refresh();

        var cpuUsage = GetCpuUsage();
        var memoryInfo = GetMemoryInfo();
        var networkInfo = GetNetworkInfo();
        var connectionInfo = GetConnectionInfo();

        var stats = new ServerHealthStats
        {
            CpuUsagePercent = cpuUsage,
            MemoryUsageMB = memoryInfo.usedMB,
            MemoryTotalMB = memoryInfo.totalMB,
            MemoryUsagePercent = memoryInfo.percent,
            ActiveConnections = connectionInfo.activeConnections,
            TotalRequestsProcessed = connectionInfo.totalRequests,
            NetworkBytesSent = networkInfo.bytesSent,
            NetworkBytesReceived = networkInfo.bytesReceived,
            UptimeSeconds = (long)(DateTime.UtcNow - _startTime).TotalSeconds,
            Timestamp = DateTime.UtcNow,
            ProcessInfo = new ProcessInfo
            {
                Id = _currentProcess.Id,
                ThreadCount = _currentProcess.Threads.Count,
                HandleCount = _currentProcess.HandleCount,
                WorkingSetMB = _currentProcess.WorkingSet64 / (1024 * 1024),
                PrivateMemoryMB = _currentProcess.PrivateMemorySize64 / (1024 * 1024)
            }
        };

        // Calculate alerts
        stats.Alerts = GenerateAlerts(stats);

        return stats;
    }

    private double GetCpuUsage()
    {
        // Alternative: Calculate based on process CPU time
        try
        {
            var cpuTime = _currentProcess.TotalProcessorTime.TotalMilliseconds;
            var elapsedTime = DateTime.UtcNow - _startTime;
            if (elapsedTime.TotalMilliseconds > 0 && Environment.ProcessorCount > 0)
            {
                var cpuUsage = (cpuTime / (elapsedTime.TotalMilliseconds * Environment.ProcessorCount)) * 100;
                return Math.Min(100, Math.Round(cpuUsage, 1));
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private (long totalMB, long usedMB, double percent) GetMemoryInfo()
    {
        try
        {
            // Get memory using Windows API
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                var totalMB = (long)(memStatus.ullTotalPhys / (1024 * 1024));
                var availableMB = (long)(memStatus.ullAvailPhys / (1024 * 1024));
                var usedMB = totalMB - availableMB;
                var percent = totalMB > 0 ? Math.Round((double)usedMB / totalMB * 100, 1) : 0;
                return (totalMB, usedMB, percent);
            }
            
            // Fallback: Use process memory
            _currentProcess.Refresh();
            var processMemoryMB = _currentProcess.WorkingSet64 / (1024 * 1024);
            return (processMemoryMB * 10, processMemoryMB, 50);
        }
        catch
        {
            // Fallback: Use process memory
            _currentProcess.Refresh();
            var processMemoryMB = _currentProcess.WorkingSet64 / (1024 * 1024);
            return (processMemoryMB * 10, processMemoryMB, 50);
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    private (long bytesSent, long bytesReceived) GetNetworkInfo()
    {
        try
        {
            long totalSent = 0;
            long totalReceived = 0;

            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            foreach (var ni in interfaces)
            {
                var stats = ni.GetIPv4Statistics();
                totalSent += stats.BytesSent;
                totalReceived += stats.BytesReceived;
            }

            return (totalSent, totalReceived);
        }
        catch
        {
            return (0, 0);
        }
    }

    private (int activeConnections, long totalRequests) GetConnectionInfo()
    {
        try
        {
            // Count established TCP connections
            var tcpConnections = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Count(c => c.State == TcpState.Established);

            return (tcpConnections, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    private List<ServerAlert> GenerateAlerts(ServerHealthStats stats)
    {
        var alerts = new List<ServerAlert>();

        // CPU Alert
        if (stats.CpuUsagePercent >= 90)
        {
            alerts.Add(new ServerAlert
            {
                Type = AlertType.Critical,
                Message = "CPU em carga crítica! (>90%)",
                Value = stats.CpuUsagePercent,
                Threshold = 90,
                Timestamp = DateTime.UtcNow
            });
        }
        else if (stats.CpuUsagePercent >= 80)
        {
            alerts.Add(new ServerAlert
            {
                Type = AlertType.Warning,
                Message = "CPU com carga alta (>80%)",
                Value = stats.CpuUsagePercent,
                Threshold = 80,
                Timestamp = DateTime.UtcNow
            });
        }

        // Memory Alert
        if (stats.MemoryUsagePercent >= 90)
        {
            alerts.Add(new ServerAlert
            {
                Type = AlertType.Critical,
                Message = "Memória em nível crítico! (>90%)",
                Value = stats.MemoryUsagePercent,
                Threshold = 90,
                Timestamp = DateTime.UtcNow
            });
        }
        else if (stats.MemoryUsagePercent >= 85)
        {
            alerts.Add(new ServerAlert
            {
                Type = AlertType.Warning,
                Message = "Memória com uso elevado (>85%)",
                Value = stats.MemoryUsagePercent,
                Threshold = 85,
                Timestamp = DateTime.UtcNow
            });
        }

        // Connections Alert
        if (stats.ActiveConnections >= 500)
        {
            alerts.Add(new ServerAlert
            {
                Type = AlertType.Warning,
                Message = "Alto número de conexões simultâneas",
                Value = stats.ActiveConnections,
                Threshold = 500,
                Timestamp = DateTime.UtcNow
            });
        }

        return alerts;
    }
}

