using System;
using Microsoft.Win32;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum FileAssociationRegistrationStatus
    {
        Registered,
        MissingApplicationPath,
        MissingRegistryRoot
    }

    public sealed record FileAssociationRegistrationResult(
        FileAssociationRegistrationStatus Status);

    public interface IFileAssociationRegistry
    {
        bool TrySetValue(
            string keyPath,
            string valueName,
            string value);
    }

    public sealed class FileAssociationRegistrationService
    {
        private readonly IFileAssociationRegistry _registry;
        private readonly Func<string?> _applicationPathProvider;

        public FileAssociationRegistrationService(
            IFileAssociationRegistry? registry = null,
            Func<string?>? applicationPathProvider = null)
        {
            _registry = registry ?? new CurrentUserClassesRegistry();
            _applicationPathProvider = applicationPathProvider ?? GetCurrentApplicationPath;
        }

        public FileAssociationRegistrationResult RegisterCurrentUserAssociations()
        {
            string? applicationPath = _applicationPathProvider();

            if (string.IsNullOrWhiteSpace(applicationPath))
            {
                return new FileAssociationRegistrationResult(
                    FileAssociationRegistrationStatus.MissingApplicationPath);
            }

            bool registeredSqlPlan = RegisterAssociation(
                ".sqlplan",
                "SqlXmlAnalyzer.sqlplan",
                "SQL Server 执行计划文件 (.sqlplan)",
                applicationPath);
            bool registeredXdl = RegisterAssociation(
                ".xdl",
                "SqlXmlAnalyzer.xdl",
                "SQL Server 死锁文件 (.xdl)",
                applicationPath);

            return registeredSqlPlan && registeredXdl
                ? new FileAssociationRegistrationResult(
                    FileAssociationRegistrationStatus.Registered)
                : new FileAssociationRegistrationResult(
                    FileAssociationRegistrationStatus.MissingRegistryRoot);
        }

        private bool RegisterAssociation(
            string extension,
            string programId,
            string description,
            string applicationPath)
        {
            string command = $"\"{applicationPath}\" \"%1\"";

            return
                _registry.TrySetValue(extension, "", programId)
                && _registry.TrySetValue(programId, "", description)
                && _registry.TrySetValue(programId, "FriendlyTypeName", description)
                && _registry.TrySetValue(
                    $@"{programId}\shell\open\command",
                    "",
                    command);
        }

        private static string? GetCurrentApplicationPath()
        {
            return System.Diagnostics.Process
                .GetCurrentProcess()
                .MainModule
                ?.FileName;
        }

        private sealed class CurrentUserClassesRegistry : IFileAssociationRegistry
        {
            public bool TrySetValue(
                string keyPath,
                string valueName,
                string value)
            {
                using RegistryKey? classesKey =
                    Registry.CurrentUser.OpenSubKey(@"Software\Classes", true);

                if (classesKey == null)
                {
                    return false;
                }

                using RegistryKey? key = classesKey.CreateSubKey(keyPath);
                if (key == null)
                {
                    return false;
                }

                key.SetValue(valueName, value);
                return true;
            }
        }
    }
}
