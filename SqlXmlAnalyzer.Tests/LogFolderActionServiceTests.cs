using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class LogFolderActionServiceTests
    {
        [Fact]
        public void BuildOpenLogsFolder_WhenLogDirectoryExists_ReturnsReady()
        {
            string baseDirectory = @"C:\Tools\SqlXmlAnalyzer";
            string expectedPath = Path.Combine(baseDirectory, "log");
            var service = new LogFolderActionService(
                () => baseDirectory,
                path => path == expectedPath);

            LogFolderActionResult result = service.BuildOpenLogsFolder();

            result.Status.Should().Be(LogFolderActionStatus.Ready);
            result.FolderPath.Should().Be(expectedPath);
            result.UserMessage.Should().BeEmpty();
        }

        [Fact]
        public void BuildOpenLogsFolder_WhenLogDirectoryIsMissing_ReturnsMissingDirectory()
        {
            string baseDirectory = @"C:\Tools\SqlXmlAnalyzer";
            string expectedPath = Path.Combine(baseDirectory, "log");
            var service = new LogFolderActionService(
                () => baseDirectory,
                _ => false);

            LogFolderActionResult result = service.BuildOpenLogsFolder();

            result.Status.Should().Be(LogFolderActionStatus.MissingDirectory);
            result.FolderPath.Should().Be(expectedPath);
            result.UserMessage.Should().Be("The log folder has not been created yet.");
        }
    }
}
