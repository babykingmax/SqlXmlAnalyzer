using System;
using System.Collections.Generic;
using Xunit;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;

namespace SqlXmlAnalyzer.Tests
{
    public class StatisticsHistogramParserTests
    {
        [Fact]
        public void Parse_NumericKeys_ReturnsCorrectStepsAndTypes()
        {
            // Arrange
            string tsv = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\r\n" +
                         "10\t0\t1\t0\t1\r\n" +
                         "50\t39\t2\t10\t3.9\r\n" +
                         "100\t49\t5\t5\t9.8\r\n";

            // Act
            var steps = StatisticsHistogramParser.Parse(tsv, out HistogramKeyType keyType);

            // Assert
            Assert.NotNull(steps);
            Assert.Equal(3, steps.Count);
            Assert.Equal(HistogramKeyType.Numeric, keyType);

            Assert.Equal("10", steps[0].RangeHiKey);
            Assert.Equal(10.0, steps[0].RangeHiKeyNumeric);
            Assert.Equal(1.0, steps[0].EqRows);

            Assert.Equal("50", steps[1].RangeHiKey);
            Assert.Equal(50.0, steps[1].RangeHiKeyNumeric);
            Assert.Equal(3.9, steps[1].AvgRangeRows);
        }

        [Fact]
        public void Estimate_NumericExactMatch_ReturnsEqRows()
        {
            // Arrange
            string tsv = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\r\n" +
                         "10\t0\t1\t0\t1\r\n" +
                         "50\t39\t2\t10\t3.9\r\n" +
                         "100\t49\t5\t5\t9.8\r\n";
            var steps = StatisticsHistogramParser.Parse(tsv, out HistogramKeyType keyType);

            // Act
            StatisticsHistogramParser.EstimateValue("50", steps!, keyType, out double estimatedRows, out double numericPos, out string matchType);

            // Assert
            Assert.Equal(2.0, estimatedRows);
            Assert.Equal(50.0, numericPos);
            Assert.Contains("精确匹配", matchType);
        }

        [Fact]
        public void Estimate_NumericRangeMatch_ReturnsAvgRangeRows()
        {
            // Arrange
            string tsv = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\r\n" +
                         "10\t0\t1\t0\t1\r\n" +
                         "50\t39\t2\t10\t3.9\r\n" +
                         "100\t49\t5\t5\t9.8\r\n";
            var steps = StatisticsHistogramParser.Parse(tsv, out HistogramKeyType keyType);

            // Act
            StatisticsHistogramParser.EstimateValue("30", steps!, keyType, out double estimatedRows, out double numericPos, out string matchType);

            // Assert
            Assert.Equal(3.9, estimatedRows);
            Assert.Equal(30.0, numericPos);
            Assert.Contains("落入区间", matchType);
        }

        [Fact]
        public void Parse_DateTimeKeys_ReturnsDateTimeType()
        {
            // Arrange
            string tsv = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\r\n" +
                         "2026-01-01\t0\t1\t0\t1\r\n" +
                         "2026-06-01\t150\t2\t10\t15\r\n";

            // Act
            var steps = StatisticsHistogramParser.Parse(tsv, out HistogramKeyType keyType);

            // Assert
            Assert.NotNull(steps);
            Assert.Equal(HistogramKeyType.DateTime, keyType);
            Assert.Equal(new DateTime(2026, 1, 1).Ticks, steps[0].RangeHiKeyNumeric);
        }

        [Fact]
        public void Estimate_DateTimeRangeMatch_ReturnsAvgRangeRows()
        {
            // Arrange
            string tsv = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\r\n" +
                         "2026-01-01\t0\t1\t0\t1\r\n" +
                         "2026-06-01\t150\t2\t10\t15\r\n";
            var steps = StatisticsHistogramParser.Parse(tsv, out HistogramKeyType keyType);

            // Act
            StatisticsHistogramParser.EstimateValue("2026-03-01", steps!, keyType, out double estimatedRows, out _, out string matchType);

            // Assert
            Assert.Equal(15.0, estimatedRows);
            Assert.Contains("落入区间", matchType);
        }

        [Fact]
        public void Parse_StringKeys_ReturnsStringType()
        {
            // Arrange
            string tsv = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\r\n" +
                         "Apple\t0\t1\t0\t1\r\n" +
                         "Banana\t10\t2\t2\t5\r\n" +
                         "Cherry\t20\t3\t5\t4\r\n";

            // Act
            var steps = StatisticsHistogramParser.Parse(tsv, out HistogramKeyType keyType);

            // Assert
            Assert.NotNull(steps);
            Assert.Equal(HistogramKeyType.String, keyType);
            Assert.Equal(1.0, steps[1].RangeHiKeyNumeric); // index based
        }

        [Fact]
        public void Estimate_StringExactMatch_ReturnsEqRows()
        {
            // Arrange
            string tsv = "RANGE_HI_KEY\tRANGE_ROWS\tEQ_ROWS\tDISTINCT_RANGE_ROWS\tAVG_RANGE_ROWS\r\n" +
                         "Apple\t0\t1\t0\t1\r\n" +
                         "Banana\t10\t2\t2\t5\r\n";
            var steps = StatisticsHistogramParser.Parse(tsv, out HistogramKeyType keyType);

            // Act
            StatisticsHistogramParser.EstimateValue("Banana", steps!, keyType, out double estimatedRows, out _, out string matchType);

            // Assert
            Assert.Equal(2.0, estimatedRows);
            Assert.Contains("精确匹配", matchType);
        }

        [Fact]
        public void Parse_MalformedTsv_ReturnsNull()
        {
            // Arrange
            string tsv = "Some random text that is not a statistics table";

            // Act
            var steps = StatisticsHistogramParser.Parse(tsv, out _);

            // Assert
            Assert.Null(steps);
        }
    }
}
