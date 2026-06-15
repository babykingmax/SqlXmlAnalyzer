using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Xunit;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Tests
{
    public class StatisticsUsageParserTests
    {
        private readonly XNamespace _ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void Parse_WithOptimizerStatsUsage_ReturnsCorrectList()
        {
            // Arrange
            string xmlStr = @"
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple>
          <QueryPlan>
            <OptimizerStatsUsage>
              <StatisticsInfo Database=""[AdventureWorks]"" Schema=""[Sales]"" Table=""[Customer]"" Statistics=""[AK_Customer_AccountNumber]"" LastUpdate=""2026-05-01T10:00:00"" ModificationCount=""1500"" SamplingPercent=""100"" />
              <StatisticsInfo Database=""[AdventureWorks]"" Schema=""[Sales]"" Table=""[Customer]"" Statistics=""[PK_Customer_CustomerID]"" LastUpdate=""2026-06-10T12:00:00"" ModificationCount=""0"" SamplingPercent=""10"" />
            </OptimizerStatsUsage>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            var doc = XDocument.Parse(xmlStr);

            // Act
            var result = StatisticsUsageParser.Parse(doc, _ns);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);

            var stat1 = result[0];
            Assert.Equal("[AdventureWorks]", stat1.Database);
            Assert.Equal("[Sales]", stat1.Schema);
            Assert.Equal("[Customer]", stat1.Table);
            Assert.Equal("[AK_Customer_AccountNumber]", stat1.Statistics);
            Assert.Equal(new DateTime(2026, 5, 1, 10, 0, 0), stat1.LastUpdate);
            Assert.Equal(1500, stat1.ModificationCount);
            Assert.Equal(100.0, stat1.SamplingPercent);
            Assert.True(stat1.IsStale || (DateTime.Now - stat1.LastUpdate!.Value).TotalDays > 30);
            Assert.False(stat1.IsLowSampling);

            var stat2 = result[1];
            Assert.Equal("[AdventureWorks]", stat2.Database);
            Assert.Equal("[Sales]", stat2.Schema);
            Assert.Equal("[Customer]", stat2.Table);
            Assert.Equal("[PK_Customer_CustomerID]", stat2.Statistics);
            Assert.Equal(new DateTime(2026, 6, 10, 12, 0, 0), stat2.LastUpdate);
            Assert.Equal(0, stat2.ModificationCount);
            Assert.Equal(10.0, stat2.SamplingPercent);
            Assert.True(stat2.IsLowSampling);
        }

        [Fact]
        public void Parse_WithoutOptimizerStatsUsage_ReturnsEmptyList()
        {
            // Arrange
            string xmlStr = @"
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2004/07/showplan"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple>
          <QueryPlan>
            <ParameterList>
              <ColumnReference Column=""@Param"" ParameterCompiledValue=""1"" />
            </ParameterList>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

            var doc = XDocument.Parse(xmlStr);

            // Act
            var result = StatisticsUsageParser.Parse(doc, _ns);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void Parse_WithNullDocument_ReturnsEmptyList()
        {
            // Act
            var result = StatisticsUsageParser.Parse(null!, _ns);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void StatisticsInfo_SeverityAndStatusText_CalculatesCorrectly()
        {
            // Test Info
            var infoStat = new StatisticsInfo
            {
                LastUpdate = DateTime.Now.AddDays(-10),
                ModificationCount = 500,
                SamplingPercent = 100
            };
            Assert.Equal("Info", infoStat.Severity);
            Assert.Equal("正常", infoStat.StatusText);

            // Test Warning: Age > 30
            var warnAgeStat = new StatisticsInfo
            {
                LastUpdate = DateTime.Now.AddDays(-40),
                ModificationCount = 0,
                SamplingPercent = 50
            };
            Assert.Equal("Warning", warnAgeStat.Severity);
            Assert.Equal("已过时", warnAgeStat.StatusText);

            // Test Warning: ModificationCount > 1000
            var warnModStat = new StatisticsInfo
            {
                LastUpdate = DateTime.Now.AddDays(-5),
                ModificationCount = 1500,
                SamplingPercent = 100
            };
            Assert.Equal("Warning", warnModStat.Severity);
            Assert.Equal("频繁变动", warnModStat.StatusText);

            // Test Warning: LowSampling < 20
            var warnSampStat = new StatisticsInfo
            {
                LastUpdate = DateTime.Now.AddDays(-5),
                ModificationCount = 0,
                SamplingPercent = 15
            };
            Assert.Equal("Warning", warnSampStat.Severity);
            Assert.Equal("低采样率", warnSampStat.StatusText);

            // Test Critical: Age > 90
            var critAgeStat = new StatisticsInfo
            {
                LastUpdate = DateTime.Now.AddDays(-100),
                ModificationCount = 0,
                SamplingPercent = 100
            };
            Assert.Equal("Critical", critAgeStat.Severity);
            Assert.Equal("严重过时", critAgeStat.StatusText);

            // Test Critical: ModificationCount > 10000
            var critModStat = new StatisticsInfo
            {
                LastUpdate = DateTime.Now.AddDays(-5),
                ModificationCount = 12000,
                SamplingPercent = 100
            };
            Assert.Equal("Critical", critModStat.Severity);
            Assert.Equal("超高变动", critModStat.StatusText);

            // Test Critical: SamplingPercent < 5
            var critSampStat = new StatisticsInfo
            {
                LastUpdate = DateTime.Now.AddDays(-5),
                ModificationCount = 0,
                SamplingPercent = 3
            };
            Assert.Equal("Critical", critSampStat.Severity);
            Assert.Equal("极低采样", critSampStat.StatusText);
        }
    }
}
