using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphNodeClipboardServiceTests
    {
        [Fact]
        public void BuildNodeInfo_IncludesCoreNodeFields()
        {
            var service = new PlanGraphNodeClipboardService();

            string text = service.BuildNodeInfo(CreateInfo());

            text.Should().Contain("Node ID: 7");
            text.Should().Contain("Physical Op: Index Seek");
            text.Should().Contain("Logical Op: Inner Join");
            text.Should().Contain("Estimated Cost: 12.34 (26.0%)");
            text.Should().Contain("Estimated Rows: 1K");
            text.Should().Contain("Actual Rows: 987");
            text.Should().Contain("Estimated Data Size: 128 KB");
        }

        [Fact]
        public void BuildNodeInfo_WhenOptionalFieldsExist_IncludesOptionalFields()
        {
            var service = new PlanGraphNodeClipboardService();
            PlanGraphNodeClipboardInfo info = CreateInfo(
                objectDetails: "[Orders].[IX_Orders]",
                outputList: "[OrderId], [CustomerId]",
                seekPredicates: "[CustomerId]=@p0",
                predicate: "[Status]='Open'",
                warnings: "SpillToTempDb");

            string text = service.BuildNodeInfo(info);

            text.Should().Contain("Object: [Orders].[IX_Orders]");
            text.Should().Contain("Output List: [OrderId], [CustomerId]");
            text.Should().Contain("Seek Predicates: [CustomerId]=@p0");
            text.Should().Contain("Predicate: [Status]='Open'");
            text.Should().Contain("Warnings: SpillToTempDb");
        }

        [Fact]
        public void BuildNodeInfo_WhenOptionalFieldsAreEmpty_OmitsOptionalFields()
        {
            var service = new PlanGraphNodeClipboardService();

            string text = service.BuildNodeInfo(CreateInfo());

            text.Should().NotContain("Object:");
            text.Should().NotContain("Output List:");
            text.Should().NotContain("Seek Predicates:");
            text.Should().NotContain("Predicate:");
            text.Should().NotContain("Warnings:");
        }

        private static PlanGraphNodeClipboardInfo CreateInfo(
            string objectDetails = "",
            string outputList = "",
            string seekPredicates = "",
            string predicate = "",
            string warnings = "")
        {
            return new PlanGraphNodeClipboardInfo(
                "7",
                "Index Seek",
                "Inner Join",
                12.34,
                26,
                "1K",
                "987",
                "128 KB",
                objectDetails,
                outputList,
                seekPredicates,
                predicate,
                warnings);
        }
    }
}
