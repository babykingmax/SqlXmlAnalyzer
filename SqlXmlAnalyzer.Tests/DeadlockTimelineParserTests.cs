using System.IO;
using System.Linq;
using Xunit;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Tests
{
    public class DeadlockTimelineParserTests
    {
        private const string SampleDeadlockXml = @"<deadlock>
  <victim-list>
    <victimProcess id=""process123""/>
  </victim-list>
  <process-list>
    <process id=""process123"" spid=""58"" status=""suspended""></process>
    <process id=""process456"" spid=""62"" status=""suspended""></process>
  </process-list>
  <resource-list>
    <keylock id=""lock1"">
      <owner-list>
        <owner id=""process123"" mode=""X""/>
      </owner-list>
      <waiter-list>
        <waiter id=""process456"" mode=""S"" requestType=""wait""/>
      </waiter-list>
    </keylock>
    <keylock id=""lock2"">
      <owner-list>
        <owner id=""process456"" mode=""X""/>
      </owner-list>
      <waiter-list>
        <waiter id=""process123"" mode=""S"" requestType=""wait""/>
      </waiter-list>
    </keylock>
  </resource-list>
</deadlock>";

        [Fact]
        public void Parse_ShouldGenerateCorrectTimelineAndCycles()
        {
            var parser = new DeadlockTimelineParser();
            var parsed = parser.Parse(SampleDeadlockXml);

            parsed.Should().NotBeNull();
            
            // 2 Grant events, 2 Request events, 1 Victim event = 5 total events
            parsed.Events.Count.Should().Be(5);

            // Verify Cycle Detection (both processes should be in cycle)
            parsed.Processes["process123"].IsInCycle.Should().BeTrue();
            parsed.Processes["process456"].IsInCycle.Should().BeTrue();

            parsed.Resources["res_0"].IsInCycle.Should().BeTrue();
            parsed.Resources["res_1"].IsInCycle.Should().BeTrue();

            // Victim should be correctly marked
            var victimEvent = parsed.Events.FirstOrDefault(e => e.Type == "Victim");
            victimEvent.Should().NotBeNull();
            victimEvent?.ProcessId.Should().Be("process123");
        }
    }
}
