using System.ComponentModel;
using System.Runtime.InteropServices;
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

        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(ProtectData(bytes));
    }

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(protectedValue);
            return Encoding.UTF8.GetString(UnprotectData(bytes));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
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
                Marshal.FreeHGlobal(input.DataPointer);
            }

            if (output.DataPointer != IntPtr.Zero)
            {
                LocalFree(output.DataPointer);
            }
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
