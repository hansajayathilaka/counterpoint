using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Counterpoint.Infrastructure.Security;

/// <summary>
/// The slice of the Windows Credential Manager API that Counterpoint needs: write one generic
/// credential, read it back, free what the OS allocated.
/// </summary>
/// <remarks>
/// This code cannot run on the Linux development host. It must still compile there, hence the
/// platform attributes rather than conditional compilation.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsCredentialManager
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    /// <summary>Returns the stored blob, or null when no such credential exists.</summary>
    internal static byte[]? TryRead(string targetName)
    {
        var handle = IntPtr.Zero;
        try
        {
            if (!NativeMethods.CredReadW(targetName, CredTypeGeneric, 0, out handle))
            {
                return null;
            }

            var credential = Marshal.PtrToStructure<NativeMethods.Credential>(handle);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var blob = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return blob;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.CredFree(handle);
            }
        }
    }

    /// <summary>Stores <paramref name="blob"/> as a generic credential, replacing any existing one.</summary>
    internal static void Write(string targetName, string userName, byte[] blob)
    {
        var targetNamePointer = IntPtr.Zero;
        var userNamePointer = IntPtr.Zero;
        var blobPointer = IntPtr.Zero;

        try
        {
            targetNamePointer = Marshal.StringToCoTaskMemUni(targetName);
            userNamePointer = Marshal.StringToCoTaskMemUni(userName);
            blobPointer = Marshal.AllocHGlobal(blob.Length);
            Marshal.Copy(blob, 0, blobPointer, blob.Length);

            var credential = new NativeMethods.Credential
            {
                Type = CredTypeGeneric,
                TargetName = targetNamePointer,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredPersistLocalMachine,
                UserName = userNamePointer,
            };

            if (!NativeMethods.CredWriteW(ref credential, 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows Credential Manager refused to store the Counterpoint database key.");
            }
        }
        finally
        {
            // The OS copies everything during CredWriteW, so all three buffers are ours to free
            // whether the call succeeded or not.
            if (blobPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(blobPointer);
            }

            if (userNamePointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(userNamePointer);
            }

            if (targetNamePointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(targetNamePointer);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWriteW(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredReadW(
            string targetName,
            uint type,
            uint reservedFlag,
            out IntPtr credentialPointer);

        [DllImport("advapi32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern void CredFree(IntPtr buffer);

        /// <summary>Mirrors <c>CREDENTIALW</c> from wincred.h. Field order and types are load-bearing.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Credential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }
    }
}
