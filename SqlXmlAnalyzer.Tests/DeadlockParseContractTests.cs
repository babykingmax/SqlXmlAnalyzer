using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Parsers;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class DeadlockParseContractTests
    {
        [Theory]
        [InlineData("<deadlock><resource-list><keylock /></resource-list></deadlock>", "process-list")]
        [InlineData("<deadlock><process-list><process id=\"p1\" /></process-list></deadlock>", "resource-list")]
        [InlineData("<deadlock><process-list><process /></process-list><resource-list><keylock /></resource-list></deadlock>", "有效 id")]
        [InlineData("<deadlock><process-list><process id=\"p1\" /></process-list><resource-list /></deadlock>", "有效的锁资源")]
        public void TryParseDeadlockXml_MissingRequiredStructure_ReturnsFailure(
            string xml,
            string expectedError)
        {
            var result = DeadlockXmlParser.TryParseDeadlockXml(XDocument.Parse(xml));

            result.IsSuccess.Should().BeFalse();
            result.Value.Should().BeNull();
            result.Errors.Should().Contain(message => message.Contains(expectedError));
        }

        [Fact]
        public void TryParseDeadlockXml_MissingOptionalFields_ReturnsWarningsAndCompleteValue()
        {
            var doc = XDocument.Parse("""
                <deadlock>
                  <process-list>
                    <process id="p1" />
                  </process-list>
                  <resource-list>
                    <keylock>
                      <owner-list><owner id="p1" /></owner-list>
                      <waiter-list><waiter id="unknown" /></waiter-list>
                    </keylock>
                  </resource-list>
                </deadlock>
                """);

            var result = DeadlockXmlParser.TryParseDeadlockXml(doc);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Processes.Should().ContainSingle();
            result.Value.Resources.Should().ContainSingle();
            result.Warnings.Should().Contain(message => message.Contains("spid"));
            result.Warnings.Should().Contain(message => message.Contains("victimProcess"));
            result.Warnings.Should().Contain(message => message.Contains("未知进程"));
        }

        [Fact]
        public void TimelineParseResult_InvalidDeadlock_DoesNotReturnPartialTimeline()
        {
            var parser = new DeadlockTimelineParser();

            var result = parser.ParseResult(
                "<deadlock><process-list><process id=\"p1\" /></process-list></deadlock>");

            result.IsSuccess.Should().BeFalse();
            result.Value.Should().BeNull();
            result.Errors.Should().Contain(message => message.Contains("resource-list"));
        }

        [Fact]
        public void TimelineParseResult_ValidDeadlock_ReturnsTimeline()
        {
            const string xml = """
                <deadlock>
                  <victim-list><victimProcess id="p1" /></victim-list>
                  <process-list>
                    <process id="p1" spid="51" />
                    <process id="p2" spid="52" />
                  </process-list>
                  <resource-list>
                    <keylock>
                      <owner-list><owner id="p1" mode="X" /></owner-list>
                      <waiter-list><waiter id="p2" mode="S" /></waiter-list>
                    </keylock>
                    <keylock>
                      <owner-list><owner id="p2" mode="X" /></owner-list>
                      <waiter-list><waiter id="p1" mode="S" /></waiter-list>
                    </keylock>
                  </resource-list>
                </deadlock>
                """;
            var parser = new DeadlockTimelineParser();

            var result = parser.ParseResult(xml);

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Events.Should().HaveCount(5);
        }
    }
}
