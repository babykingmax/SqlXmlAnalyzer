using System.Security.AccessControl;
using System.Security.Principal;

namespace SqlXmlAnalyzer.Core.Diagnostics;

public static class DiagnosticFileAccess
{
    public static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var user = WindowsIdentity.GetCurrent().User ?? throw new IOException("Cannot identify the diagnostic file owner.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(user);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            // Parent directories have no dump bytes; the incident directory is private from creation.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            new DirectoryInfo(path).Create(security);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
