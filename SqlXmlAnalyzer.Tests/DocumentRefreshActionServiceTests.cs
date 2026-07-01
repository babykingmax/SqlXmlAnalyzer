using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DocumentRefreshActionServiceTests
    {
        [Fact]
        public void BuildDeadlockRefresh_WhenFilePathIsMissing_ReturnsMissingFile()
        {
            var service = new DocumentRefreshActionService();

            DocumentRefreshActionResult result = service.BuildDeadlockRefresh("");

            result.Status.Should().Be(DocumentRefreshActionStatus.MissingFile);
            result.FilePath.Should().BeEmpty();
            result.UserMessage.Should().Be("No loaded deadlock file is available to refresh.");
        }

        [Fact]
        public void BuildDeadlockRefresh_WhenFilePathExists_ReturnsReady()
        {
            var service = new DocumentRefreshActionService();

            DocumentRefreshActionResult result = service.BuildDeadlockRefresh("C:\\Temp\\deadlock.xdl");

            result.Status.Should().Be(DocumentRefreshActionStatus.Ready);
            result.FilePath.Should().Be("C:\\Temp\\deadlock.xdl");
            result.UserMessage.Should().BeEmpty();
        }

        [Fact]
        public void BuildPlanRefresh_WhenFilePathIsMissing_ReturnsMissingFile()
        {
            var service = new DocumentRefreshActionService();

            DocumentRefreshActionResult result = service.BuildPlanRefresh(null);

            result.Status.Should().Be(DocumentRefreshActionStatus.MissingFile);
            result.FilePath.Should().BeEmpty();
            result.UserMessage.Should().Be("No loaded execution plan file is available to refresh.");
        }

        [Fact]
        public void BuildPlanRefresh_WhenFilePathExists_ReturnsReady()
        {
            var service = new DocumentRefreshActionService();

            DocumentRefreshActionResult result = service.BuildPlanRefresh("C:\\Temp\\plan.sqlplan");

            result.Status.Should().Be(DocumentRefreshActionStatus.Ready);
            result.FilePath.Should().Be("C:\\Temp\\plan.sqlplan");
            result.UserMessage.Should().BeEmpty();
        }
    }
}
