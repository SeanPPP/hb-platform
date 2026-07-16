using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Hbpos.Client.Wpf.Services;

public interface IDeviceAuthorizationProtector
{
    string? Protect(string? value);

    string? Unprotect(string? protectedValue);
}

public sealed class WindowsDpapiDeviceAuthorizationProtector : IDeviceAuthorizationProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(value);
        byte[]? protectedBytes = null;
        try
        {
            protectedBytes = ProtectData(plaintextBytes);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            // 关键逻辑：字符串转换完成后，托管临时明文和密文副本均不再保留。
            CryptographicOperations.ZeroMemory(plaintextBytes);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
    }

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        byte[]? protectedBytes = null;
        byte[]? plaintextBytes = null;
        try
        {
            protectedBytes = Convert.FromBase64String(protectedValue);
            plaintextBytes = UnprotectData(protectedBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        finally
        {
            // 返回值是独立字符串；DPAPI 解密数组与输入副本都可立即清零。
            if (plaintextBytes is not null)
            {
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }

            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }
        }
    }

    private static byte[] ProtectData(byte[] data)
    {
        return CryptTransform(data, protect: true);
    }

    private static byte[] UnprotectData(byte[] data)
    {
        return CryptTransform(data, protect: false);
    }

    private static byte[] CryptTransform(byte[] data, bool protect)
    {
        var input = CreateBlob(data);
        var output = new DataBlob();

        try
        {
            var ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output);

            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var result = new byte[output.DataLength];
            Marshal.Copy(output.DataPointer, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.DataPointer != IntPtr.Zero)
            {
                try
                {
                    ZeroUnmanagedMemory(input.DataPointer, input.DataLength);
                }
                finally
                {
                    Marshal.FreeHGlobal(input.DataPointer);
                }
            }

            if (output.DataPointer != IntPtr.Zero)
            {
                try
                {
                    ZeroUnmanagedMemory(output.DataPointer, output.DataLength);
                }
                finally
                {
                    LocalFree(output.DataPointer);
                }
            }
        }
    }

    private static void ZeroUnmanagedMemory(IntPtr pointer, int length)
    {
        for (var index = 0; index < length; index++)
        {
            Marshal.WriteByte(pointer, index, 0);
        }
    }

    private static DataBlob CreateBlob(byte[] data)
    {
        var pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new DataBlob(data.Length, pointer);
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob(int dataLength, IntPtr dataPointer)
    {
        public int DataLength = dataLength;

        public IntPtr DataPointer = dataPointer;
    }
}
