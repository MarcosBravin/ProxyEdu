using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace ProxyEdu.Server.Services;

/// <summary>Persiste a senha da CA com DPAPI de máquina quando ela não foi fornecida pela implantação.</summary>
public static class CertificatePasswordStore
{
    private const string PasswordFileName = "proxyedu-ca-password.bin";
    private const string RootPfxFileName = "proxyedu-root-ca.pfx";

    public static string Resolve(string certificateDirectory, IConfiguration configuration)
    {
        var configured = configuration["Certificate:Password"]
            ?? Environment.GetEnvironmentVariable("PROXYEDU_CERT_PASSWORD");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        Directory.CreateDirectory(certificateDirectory);
        var path = Path.Combine(certificateDirectory, PasswordFileName);
        if (File.Exists(path))
        {
            var protectedBytes = File.ReadAllBytes(path);
            return System.Text.Encoding.UTF8.GetString(Unprotect(protectedBytes));
        }

        // Nunca gere uma senha nova para um PFX existente: isso faria o serviço
        // aparentar saudável enquanto perde a capacidade de carregar a CA anterior.
        if (File.Exists(Path.Combine(certificateDirectory, RootPfxFileName)))
        {
            throw new InvalidOperationException(
                "Foi encontrado um PFX de CA sem a senha DPAPI correspondente. Configure PROXYEDU_CERT_PASSWORD para migrar a instalação ou restaure proxyedu-ca-password.bin do backup.");
        }

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var bytes = System.Text.Encoding.UTF8.GetBytes(password);
        var protectedPassword = Protect(bytes);
        File.WriteAllBytes(path, protectedPassword);
        return password;
    }

    private static byte[] Protect(byte[] value) => Crypt(value, protect: true);
    private static byte[] Unprotect(byte[] value) => Crypt(value, protect: false);

    private static byte[] Crypt(byte[] value, bool protect)
    {
        var input = new DataBlob(value);
        try
        {
            var success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectLocalMachine, out var output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out output);
            if (!success) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.Count];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { input.Dispose(); }
    }

    private const int CryptProtectLocalMachine = 0x4;

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob : IDisposable
    {
        public int Count;
        public IntPtr Data;
        public DataBlob(byte[] value)
        {
            Count = value.Length;
            Data = Marshal.AllocHGlobal(value.Length);
            Marshal.Copy(value, 0, Data, value.Length);
        }
        public void Dispose()
        {
            if (Data != IntPtr.Zero) Marshal.FreeHGlobal(Data);
            Data = IntPtr.Zero;
            Count = 0;
        }
    }
}
