using System.Windows;
using System.Collections;
using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class DeadlockParallelGeometryTests
{
    public static TheoryData<double, double, double> Directions => new()
    {
        { 300, 200, -16 }, { 300, 200, 16 }, { -300, 200, -16 }, { -300, 200, 16 },
        { 300, -200, -16 }, { 300, -200, 16 }, { -300, -200, -16 }, { -300, -200, 16 },
        { 300, -20, -16 }, { 300, -20, 16 }, { -30, 200, -16 }, { -30, 200, 16 },
        { 300, 0.000001 - 20, 16 }, { 0.000001 - 30, 200, -16 }
    };

    [Theory]
    [MemberData(nameof(Directions))]
    public void CalculateConnectionPoints_ParallelEdgesTouchBothRectanglesWithTheRequiredGap(double x, double y, double offset)
    {
        var positions = new Dictionary<string, Point> { ["res_1"] = new(0, 0), ["proc_id_b"] = new(x, y) };
        var service = new DeadlockGraphGeometryService();
        var points = service.CalculateConnectionPoints(positions, "res_1", "proc_id_b", offset);
        var direction = new Vector(x + 30, y + 20);
        direction.Normalize();
        AssertOnBoundary(points.From - direction * 3, new Rect(0, 0, 160, 50));
        AssertOnBoundary(points.To + direction * 3, new Rect(x, y, 220, 90));
        Vector.CrossProduct(points.To - points.From, direction).Should().BeApproximately(0, 1e-8);
        Vector.CrossProduct(direction, points.From - new Point(80, 25)).Should().BeApproximately(offset, 1e-8);
        new Rect(0, 0, 160, 50).Contains(points.From).Should().BeFalse();
        new Rect(x, y, 220, 90).Contains(points.To).Should().BeFalse();
        var reverse = service.CalculateConnectionPoints(positions, "proc_id_b", "res_1", -offset);
        (reverse.From - points.To).Length.Should().BeApproximately(0, 1e-8);
        (reverse.To - points.From).Length.Should().BeApproximately(0, 1e-8);
    }

    private static void AssertOnBoundary(Point point, Rect bounds)
    {
        point.X.Should().BeInRange(bounds.Left - 1e-8, bounds.Right + 1e-8);
        point.Y.Should().BeInRange(bounds.Top - 1e-8, bounds.Bottom + 1e-8);
        new[] { Math.Abs(point.X - bounds.Left), Math.Abs(point.X - bounds.Right),
            Math.Abs(point.Y - bounds.Top), Math.Abs(point.Y - bounds.Bottom) }.Min().Should().BeLessThan(1e-8);
    }

    [Theory]
    [InlineData(-1000000)]
    [InlineData(1000000)]
    public void CalculateConnectionPoints_ExcessiveOffsetsStillIntersectTheNode(double offset)
    {
        var points = new DeadlockGraphGeometryService().CalculateConnectionPoints(
            new Dictionary<string, Point> { ["res_1"] = new(0, 0), ["p"] = new(300, 200) }, "res_1", "p", offset);
        var direction = new Vector(330, 220);
        direction.Normalize();
        AssertOnBoundary(points.From - direction * 3, new Rect(0, 0, 160, 50));
        AssertOnBoundary(points.To + direction * 3, new Rect(300, 200, 220, 90));
    }

    [Fact]
    public void CalculateConnectionPoints_CoincidentCentersHaveDeterministicFiniteEndpoints()
    {
        var positions = new Dictionary<string, Point> { ["res_1"] = new(0, 0), ["p"] = new(-30, -20) };
        var points = new DeadlockGraphGeometryService().CalculateConnectionPoints(positions, "res_1", "p", 16);
        AssertOnBoundary(points.From - new Vector(3, 0), new Rect(0, 0, 160, 50));
        AssertOnBoundary(points.To + new Vector(3, 0), new Rect(-30, -20, 220, 90));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    public void CalculateConnectionPoints_InvalidNumbersAreExpectedErrorsWithoutDump(double coordinate, double offset)
    {
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        var service = new DeadlockGraphGeometryService(reporter);
        var positions = new Dictionary<string, Point> { ["p"] = new(coordinate, 0) };
        var calculate = () => service.CalculateConnectionPoints(positions, "p", "res_1", offset);
        calculate.Should().Throw<InvalidDataException>().WithMessage("DEADLOCK_GEOMETRY_INVALID*");
        reporter.Operations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalculateConnectionPoints_UnexpectedFailureCapturesDumpAndPreservesOriginalException(bool failDump)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-Geometry-" + Guid.NewGuid().ToString("N"));
        var reporter = new UnexpectedErrorReporter(failDump ? new FailingDumpWriter() : new WindowsMiniDumpWriter(), directory);
        var failure = new InvalidOperationException("synthetic geometry failure");
        try
        {
            var calculate = () => new DeadlockGraphGeometryService(reporter).CalculateConnectionPoints(new FaultingPositions(failure), "p", "res_1");
            calculate.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            var report = reporter.Report(failure, "must reuse captured incident");
            report.DumpCreated.Should().Be(!failDump);
            if (!failDump) MinidumpValidator.Validate(report.DumpPath!);
            else report.Failure.Should().Contain("synthetic dump failure");
            File.ReadAllText(report.MetadataPath!).Should().Contain("DeadlockGraphGeometryService.CalculateConnectionPoints").And.Contain("synthetic geometry failure");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class FailingDumpWriter : ICrashDumpWriter
    {
        public void Write(string path) => throw new IOException("synthetic dump failure");
    }

    [Fact]
    public void GeometryDiagnostics_UseBuildSpecificLogLevels()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-GeometryLogs-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "geometry.log");
        Logger.Shutdown();
        try
        {
            Logger.Initialize(customLogFilePath: path);
            var service = new DeadlockGraphGeometryService(new UnexpectedErrorReporter(new FailingDumpWriter(), directory));
            service.CalculateConnectionPoints(new Dictionary<string, Point>(), "p", "res_1", 1000);
            var invalid = () => service.CalculateConnectionPoints(new Dictionary<string, Point>(), "p", "res_1", double.NaN);
            invalid.Should().Throw<InvalidDataException>();
            var failed = () => service.CalculateConnectionPoints(new FaultingPositions(new InvalidOperationException("synthetic fault")), "p", "res_1");
            failed.Should().Throw<InvalidOperationException>();
            Logger.Flush();
            Logger.Shutdown();
            string log = File.ReadAllText(path);
            log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("[INFO]");
#if DEBUG
            log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]");
#endif
        }
        finally
        {
            Logger.Shutdown();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class FaultingPositions(Exception failure) : IReadOnlyDictionary<string, Point>
    {
        public Point this[string key] => throw failure;
        public IEnumerable<string> Keys => throw failure;
        public IEnumerable<Point> Values => throw failure;
        public int Count => throw failure;
        public bool ContainsKey(string key) => throw failure;
        public bool TryGetValue(string key, out Point value) => throw failure;
        public IEnumerator<KeyValuePair<string, Point>> GetEnumerator() => throw failure;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
