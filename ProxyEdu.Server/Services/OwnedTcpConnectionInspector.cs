using System.Runtime.InteropServices;

namespace ProxyEdu.Server.Services;

/// <summary>Consulta a tabela TCP do Windows com PID proprietário; não usa conexões globais do sistema.</summary>
public sealed class OwnedTcpConnectionInspector
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;

    public OwnedTcpConnectionSnapshot GetSnapshot(int processId, int proxyPort)
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            var size = 0;
            var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
            if (result != 122 || size <= 0) return OwnedTcpConnectionSnapshot.Unavailable;

            buffer = Marshal.AllocHGlobal(size);
            result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
            if (result != 0) return OwnedTcpConnectionSnapshot.Unavailable;

            var rows = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var proxyEstablished = 0;
            var proxyFinWait2 = 0;
            var proxyCloseWait = 0;
            var ownedEstablished = 0;
            var ownedFinWait2 = 0;
            var ownedCloseWait = 0;

            for (var index = 0; index < rows; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(buffer, sizeof(int) + index * rowSize));
                if (row.OwningPid != (uint)processId) continue;

                if (row.State == TcpStateEstablished) ownedEstablished++;
                else if (row.State == TcpStateFinWait2) ownedFinWait2++;
                else if (row.State == TcpStateCloseWait) ownedCloseWait++;

                if (GetPort(row.LocalPort) != proxyPort) continue;
                if (row.State == TcpStateEstablished) proxyEstablished++;
                else if (row.State == TcpStateFinWait2) proxyFinWait2++;
                else if (row.State == TcpStateCloseWait) proxyCloseWait++;
            }

            return new OwnedTcpConnectionSnapshot(true, proxyEstablished, proxyFinWait2, proxyCloseWait,
                ownedEstablished, ownedFinWait2, ownedCloseWait);
        }
        catch
        {
            return OwnedTcpConnectionSnapshot.Unavailable;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetPort(uint port) => (ushort)System.Net.IPAddress.NetworkToHostOrder((short)(port >> 16));

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int ipVersion, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    private const uint TcpStateEstablished = 5;
    private const uint TcpStateFinWait2 = 6;
    private const uint TcpStateCloseWait = 8;
}

public readonly record struct OwnedTcpConnectionSnapshot(
    bool IsAvailable,
    int ProxyPortEstablished,
    int ProxyPortFinWait2,
    int ProxyPortCloseWait,
    int ProcessEstablished,
    int ProcessFinWait2,
    int ProcessCloseWait)
{
    public static OwnedTcpConnectionSnapshot Unavailable => new(false, -1, -1, -1, -1, -1, -1);
}
