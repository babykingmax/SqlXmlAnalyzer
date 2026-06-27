using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class FileAssociationRegistrationServiceTests
    {
        [Fact]
        public void RegisterCurrentUserAssociations_WritesSqlPlanAndXdlAssociations()
        {
            var registry = new FakeFileAssociationRegistry();
            var service = new FileAssociationRegistrationService(
                registry,
                () => "C:\\Tools\\SqlXmlAnalyzer.exe");

            FileAssociationRegistrationResult result =
                service.RegisterCurrentUserAssociations();

            result.Status.Should().Be(FileAssociationRegistrationStatus.Registered);
            registry.Values[(".sqlplan", "")].Should().Be("SqlXmlAnalyzer.sqlplan");
            registry.Values[("SqlXmlAnalyzer.sqlplan", "")].Should().Be("SQL Server 执行计划文件 (.sqlplan)");
            registry.Values[("SqlXmlAnalyzer.sqlplan", "FriendlyTypeName")].Should().Be("SQL Server 执行计划文件 (.sqlplan)");
            registry.Values[(@"SqlXmlAnalyzer.sqlplan\shell\open\command", "")].Should().Be("\"C:\\Tools\\SqlXmlAnalyzer.exe\" \"%1\"");
            registry.Values[(".xdl", "")].Should().Be("SqlXmlAnalyzer.xdl");
            registry.Values[("SqlXmlAnalyzer.xdl", "")].Should().Be("SQL Server 死锁文件 (.xdl)");
            registry.Values[("SqlXmlAnalyzer.xdl", "FriendlyTypeName")].Should().Be("SQL Server 死锁文件 (.xdl)");
            registry.Values[(@"SqlXmlAnalyzer.xdl\shell\open\command", "")].Should().Be("\"C:\\Tools\\SqlXmlAnalyzer.exe\" \"%1\"");
        }

        [Fact]
        public void RegisterCurrentUserAssociations_WhenApplicationPathIsMissing_DoesNotWriteRegistry()
        {
            var registry = new FakeFileAssociationRegistry();
            var service = new FileAssociationRegistrationService(
                registry,
                () => "");

            FileAssociationRegistrationResult result =
                service.RegisterCurrentUserAssociations();

            result.Status.Should().Be(FileAssociationRegistrationStatus.MissingApplicationPath);
            registry.Values.Should().BeEmpty();
        }

        [Fact]
        public void RegisterCurrentUserAssociations_WhenRegistryRejectsWrite_ReturnsMissingRegistryRoot()
        {
            var registry = new FakeFileAssociationRegistry(canWrite: false);
            var service = new FileAssociationRegistrationService(
                registry,
                () => "C:\\Tools\\SqlXmlAnalyzer.exe");

            FileAssociationRegistrationResult result =
                service.RegisterCurrentUserAssociations();

            result.Status.Should().Be(FileAssociationRegistrationStatus.MissingRegistryRoot);
            registry.Values.Should().BeEmpty();
        }

        private sealed class FakeFileAssociationRegistry : IFileAssociationRegistry
        {
            private readonly bool _canWrite;

            public FakeFileAssociationRegistry(bool canWrite = true)
            {
                _canWrite = canWrite;
            }

            public Dictionary<(string KeyPath, string ValueName), string> Values { get; } =
                new();

            public bool TrySetValue(
                string keyPath,
                string valueName,
                string value)
            {
                if (!_canWrite)
                {
                    return false;
                }

                Values[(keyPath, valueName)] = value;
                return true;
            }
        }
    }
}
