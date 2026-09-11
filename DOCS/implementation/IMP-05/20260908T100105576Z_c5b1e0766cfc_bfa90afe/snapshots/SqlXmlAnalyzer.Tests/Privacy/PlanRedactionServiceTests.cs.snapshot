using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Privacy;

namespace SqlXmlAnalyzer.Tests.Privacy;

public sealed class PlanRedactionServiceTests
{
    internal const string Marker = "SECRET_IMP05_9A51";
    internal static XDocument Fixture() => SafeXmlHelper.ParseSafe($"""
        <?xml version="1.0"?>
        <?secret SECRET_IMP05_9A51?>
        <!-- SECRET_IMP05_9A51 -->
        <ShowPlanXML xmlns="{PlanRedactionService.ShowPlanNamespace}" Version="1.571" Build="16.0.1000">
          <BatchSequence><Batch><Statements><StmtSimple StatementText="SELECT '{Marker}'" StatementId="1">
            <QueryPlan><RelOp NodeId="0" PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="42" Parallel="false">
              <TableScan><Object Server="{Marker}_Server" Database="[{Marker}_Database]" Schema="[{Marker}_Schema]" Table="[{Marker}_Table]" Alias="{Marker}_Alias" Index="{Marker}_Index" />
              <DefinedValues><DefinedValue><ColumnReference Column="@{Marker}_Param" ParameterCompiledValue="'{Marker}_Compile'" ParameterRuntimeValue="'{Marker}_Runtime'" /></DefinedValue></DefinedValues>
              <Predicate><ScalarOperator ScalarString="{Marker}_Expression"><Const ConstValue="'{Marker}_Literal'" /></ScalarOperator></Predicate>
              </TableScan>
            </RelOp><ScalarOperator><![CDATA[{Marker}_CDATA]]></ScalarOperator><ScalarOperator>{Marker}_Text</ScalarOperator>
            </QueryPlan></StmtSimple></Statements></Batch></BatchSequence>
        </ShowPlanXML>
        """);

    [Fact]
    public void Redact_AllSensitiveSurfacesAreRemoved_AndSourceIsUnchanged()
    {
        var input = Fixture();
        string before = input.ToString(SaveOptions.DisableFormatting);
        var result = new PlanRedactionService().Redact(input);
        result.CanExport.Should().BeTrue(result.Summary);
        result.Xml.Should().NotContain(Marker);
        result.Summary.Should().NotContain(Marker);
        input.ToString(SaveOptions.DisableFormatting).Should().Be(before);
        var parsed = SafeXmlHelper.ParseSafe(result.Xml!);
        var op = parsed.Descendants().Single(e => e.Name.LocalName == "RelOp");
        op.Attribute("EstimateRows")!.Value.Should().Be("42");
        op.Attribute("PhysicalOp")!.Value.Should().Be("Table Scan");
        op.Attribute("Parallel")!.Value.Should().Be("false");
        result.MaskedCounts.Should().ContainKeys("Constant", "Server", "Alias", "Column", "ParameterValue", "TextOrCData", "CommentOrProcessingInstruction");
    }

    [Fact]
    public void Redact_EveryKnownFreeStringField_RemovesItsMarker()
    {
        foreach (var field in ShowPlanFieldCatalog.Attributes.Where(f => f.Value.Kind == ShowPlanFieldCatalog.FieldKind.Text))
        {
            var input = new XDocument(new XElement(XName.Get("ShowPlanXML", PlanRedactionService.ShowPlanNamespace), new XAttribute(field.Key, Marker + field.Key)));
            var result = new PlanRedactionService().Redact(input);
            result.CanExport.Should().BeTrue(field.Key);
            result.Xml.Should().NotContain(Marker, field.Key);
        }
    }

    [Theory]
    [InlineData("UnknownAttribute")]
    [InlineData("UnknownElement")]
    [InlineData("UnknownNamespace")]
    [InlineData("InvalidNumber")]
    [InlineData("InvalidEnum")]
    [InlineData("InvalidBoolean")]
    public void Redact_UnknownOrMalformedContent_BlocksEveryPayloadWithoutLeakingNames(string mode)
    {
        var input = Fixture();
        switch (mode)
        {
            case "UnknownAttribute": input.Root!.SetAttributeValue(Marker, Marker); break;
            case "UnknownElement": input.Root!.Add(new XElement(input.Root.Name.Namespace + Marker, Marker)); break;
            case "UnknownNamespace": input.Root!.Add(new XElement(XName.Get("Object", "urn:" + Marker), Marker)); break;
            case "InvalidNumber": input.Root!.SetAttributeValue("EstimateRows", Marker); break;
            case "InvalidEnum": input.Root!.SetAttributeValue("PhysicalOp", Marker); break;
            case "InvalidBoolean": input.Root!.SetAttributeValue("Parallel", Marker); break;
        }
        string before = input.ToString();
        var result = new PlanRedactionService().Redact(input);
        result.Status.Should().Be(PlanRedactionStatus.Blocked);
        result.Xml.Should().BeNull();
        result.UnsupportedCounts.Should().NotBeEmpty();
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain(Marker);
        input.ToString().Should().Be(before);
    }

    [Fact]
    public void Redact_MappingIsOrdinalStableAndIncludesDboParametersAndExprNames()
    {
        var input = SafeXmlHelper.ParseSafe($"<ShowPlanXML xmlns='{PlanRedactionService.ShowPlanNamespace}'><Object Table='[A]]B]' Schema='dbo' /><Object Table='A]B' /><Object Table='a]b' /><ColumnReference Column='@secret'/><ColumnReference Column='ExprSecret'/></ShowPlanXML>");
        var result = new PlanRedactionService().Redact(input);
        var output = SafeXmlHelper.ParseSafe(result.Xml!);
        var tables = output.Descendants().Attributes("Table").Select(a => a.Value).ToArray();
        tables[0].Should().Be("[" + tables[1] + "]");
        tables[1].Should().NotBe(tables[2]);
        result.Xml.Should().NotContain("dbo").And.NotContain("@secret").And.NotContain("ExprSecret");
        var again = new PlanRedactionService().Redact(input);
        again.Xml.Should().Be(result.Xml);
    }

    [Fact]
    public void Redact_NamespacePrefixesAndDeclarationDoNotLeak()
    {
        var input = SafeXmlHelper.ParseSafe($"<SECRET_IMP05_9A51:ShowPlanXML xmlns:SECRET_IMP05_9A51='{PlanRedactionService.ShowPlanNamespace}'/>");
        new PlanRedactionService().Redact(input).Xml.Should().NotContain(Marker);
    }

    [Fact]
    public void Redact_UnknownFailure_CapturesDumpAndNeverReturnsPartialXml()
    {
        var diagnostics = new RecordingDiagnostics();
        var input = Fixture();
        string before = input.ToString();
        var service = new PlanRedactionService(diagnostics, _ => throw new InvalidOperationException("synthetic failure"));
        var result = service.Redact(input);
        result.Status.Should().Be(PlanRedactionStatus.Failed);
        result.Xml.Should().BeNull();
        result.Diagnostic!.DumpCreated.Should().BeTrue();
        diagnostics.Calls.Should().Be(1);
        input.ToString().Should().Be(before);
    }

    [Fact]
    public void Redact_ExpectedFailureOrCancellation_DoesNotCaptureDump()
    {
        var diagnostics = new RecordingDiagnostics();
        var service = new PlanRedactionService(diagnostics, _ => throw new IOException(Marker));
        service.Redact(Fixture()).Summary.Should().NotContain(Marker);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        service.Redact(Fixture(), cts.Token).Status.Should().Be(PlanRedactionStatus.Cancelled);
        diagnostics.Calls.Should().Be(0);
    }

    internal sealed class RecordingDiagnostics : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Calls++;
            return new("synthetic.dmp", "synthetic.json", null);
        }
    }
}
