using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AI_Native_Runtime_Windows.Services
{
    /// <summary>
    /// Persists secrets (the Firebase ID/refresh token pair, the
    /// application installation's Ed25519 private key) in the Windows
    /// Credential Manager via P/Invoke against <c>advapi32.dll</c>'s
    /// <c>CredWriteW</c>/<c>CredReadW</c>/<c>CredDeleteW</c>.
    ///
    /// This is the ONLY place in this application that is allowed to
    /// persist a secret. Never write a token/key to
    /// <c>ApplicationData.Current.LocalSettings</c>, never to a file,
    /// never to a log — those are all readable by anything with
    /// filesystem/registry access to the user's profile, whereas
    /// Credential Manager entries are DPAPI-protected and scoped to the
    /// signed-in Windows user.
    ///
    /// Target-name convention mirrors CORE's own
    /// <c>WindowsCredentialSecretStore</c>
    /// (ainativeruntime_shared/src/secret_store/windows_credential.rs):
    /// every entry is a <c>CRED_TYPE_GENERIC</c> credential named
    /// <c>&lt;service&gt;/&lt;key&gt;</c>. This store uses a distinct
    /// service segment ("com.ainativeruntime.runtime.windows" rather than
    /// CORE's "com.ainativeruntime.runtime") deliberately — this process
    /// and the CORE daemon are different principals storing different
    /// keypairs (the application's installation key here vs. the node's
    /// identity key there) and must never collide in the same Credential
    /// Manager namespace even though both run as the same signed-in user.
    /// </summary>
    public sealed class CredentialStore
    {
        private const string Service = "com.ainativeruntime.runtime.windows";

        public static class Keys
        {
            /// <summary>The Firebase sign-in outcome, serialized JSON: {idToken, refreshToken, expiresAtUtc}.</summary>
            public const string FirebaseCredential = "firebase-credential";

            /// <summary>The application installation's Ed25519 private key seed, lowercase hex (32 bytes).</summary>
            public const string InstallationPrivateKey = "installation-private-key";

            /// <summary>The application installation's Ed25519 public key, lowercase hex (32 bytes) - cached alongside the private key so it need not be re-derived on every launch.</summary>
            public const string InstallationPublicKey = "installation-public-key";
        }

        public bool TryGet(string key, out string? value)
        {
            var targetName = TargetName(key);
            var ok = NativeMethods.CredRead(targetName, NativeMethods.CRED_TYPE_GENERIC, 0, out var credPtr);
            if (!ok)
            {
                value = null;
                var error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ERROR_NOT_FOUND)
                {
                    return false;
                }
                throw new InvalidOperationException($"CredReadW failed for '{targetName}' with Win32 error {error}.");
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credPtr);
                if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                {
                    value = string.Empty;
                    return true;
                }

                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                value = Encoding.UTF8.GetString(bytes);
                return true;
            }
            finally
            {
                NativeMethods.CredFree(credPtr);
            }
        }

        public void Put(string key, string value)
        {
            var targetName = TargetName(key);
            var bytes = Encoding.UTF8.GetBytes(value);
            var blobHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var credential = new NativeMethods.CREDENTIAL
                {
                    Type = NativeMethods.CRED_TYPE_GENERIC,
                    TargetName = targetName,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blobHandle.AddrOfPinnedObject(),
                    Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    Comment = null,
                    TargetAlias = null,
                    UserName = null,
                };

                var ok = NativeMethods.CredWrite(ref credential, 0);
                if (!ok)
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"CredWriteW failed for '{targetName}' with Win32 error {error}.");
                }
            }
            finally
            {
                blobHandle.Free();
            }
        }

        public void Delete(string key)
        {
            var targetName = TargetName(key);
            var ok = NativeMethods.CredDelete(targetName, NativeMethods.CRED_TYPE_GENERIC, 0);
            if (!ok)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ERROR_NOT_FOUND)
                {
                    // Already absent - deleting an absent credential is not an error,
                    // mirroring CORE's own WindowsCredentialSecretStore::delete.
                    return;
                }
                throw new InvalidOperationException($"CredDeleteW failed for '{targetName}' with Win32 error {error}.");
            }
        }

        private static string TargetName(string key) => $"{Service}/{key}";

        private static class NativeMethods
        {
            public const uint CRED_TYPE_GENERIC = 1;
            public const uint CRED_PERSIST_LOCAL_MACHINE = 2;
            public const int ERROR_NOT_FOUND = 1168;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct CREDENTIAL
            {
                public uint Flags;
                public uint Type;
                [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
                [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
                public long LastWritten;
                public uint CredentialBlobSize;
                public IntPtr CredentialBlob;
                public uint Persist;
                public uint AttributeCount;
                public IntPtr Attributes;
                [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
                [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
            }

            [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

            [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CredRead(string targetName, uint type, int flags, out IntPtr credentialPtr);

            [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CredDelete(string targetName, uint type, int flags);

            [DllImport("advapi32.dll", EntryPoint = "CredFree")]
            public static extern void CredFree(IntPtr credentialPtr);
        }
    }
}
