using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class HtmlReportGeneratorTests
    {
        [Fact]
        public void GenerateReport_WithMaliciousDynamicContent_EncodesAllText()
        {
            const string script = "<script>alert(1)</script>";
            const string image = "<img src=x onerror=alert(1)>";
            const string quotedName = "plan & table <dbo.\"Users\">";
            var sections = new[]
            {
                new HtmlReportSection(
                    $"Section {script}",
                    new List<HtmlReportItem>
                    {
                        new(
                            $"Heading {image}",
                            $"Description {script}\n{image}",
                            $"Cause {quotedName}",
                            $"Recommendation {script}",
                            $"High\" onmouseover=\"{script}")
                    })
            };

            string html = HtmlReportGenerator.GenerateReport(
                $@"C:\reports\{quotedName}.sqlplan",
                "ExecutionPlan",
                $"Summary {script}",
                $"graph TD\nA[\"{image}\"]",
                sections);

            html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
            html.Should().Contain("&lt;img src=x onerror=alert(1)&gt;");
            html.Should().Contain("plan &amp; table &lt;dbo.&quot;Users&quot;&gt;");
            html.Should().NotContain("<script>alert(1)</script>");
            html.Should().NotContain("<img src=x onerror=alert(1)>");
            html.Should().NotContain("onmouseover=\"<script>");
        }

        [Fact]
        public void GenerateReport_WithMultilineFields_EncodesBeforeAddingLineBreaks()
        {
            var sections = new[]
            {
                new HtmlReportSection(
                    "Details",
                    new[]
                    {
                        new HtmlReportItem(
                            "Issue",
                            "first <tag>\r\nsecond & value",
                            string.Empty,
                            string.Empty,
                            "Warning")
                    })
            };

            string html = HtmlReportGenerator.GenerateReport(
                "test.sqlplan",
                "ExecutionPlan",
                "summary",
                string.Empty,
                sections);

            html.Should().Contain("first &lt;tag&gt;<br/>second &amp; value");
            html.Should().NotContain("first <tag>");
        }

        [Fact]
        public void GenerateReport_WithMermaid_AddsRestrictiveCspAndStrictConfiguration()
        {
            string html = HtmlReportGenerator.GenerateReport(
                "test.xdl",
                "Deadlock",
                "summary",
                "graph TD\nA-->B",
                null);

            html.Should().Contain("Content-Security-Policy");
            html.Should().Contain("default-src &#39;none&#39;");
            html.Should().Contain("script-src &#39;nonce-");
            html.Should().Contain("https://cdn.jsdelivr.net");
            html.Should().Contain("securityLevel: 'strict'");
            html.Should().Contain("htmlLabels: false");
            html.Should().NotContain("unsafe-eval");
        }
    }
}
