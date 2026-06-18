using Xunit;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SqlXmlAnalyzer.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void TestDummy()
        {
            var sb = new StringBuilder();
            var typesToReflect = new[]
            {
                typeof(BinaryExpression),
                typeof(ParenthesisExpression)
            };

            foreach (var type in typesToReflect)
            {
                sb.AppendLine($"====================================");
                sb.AppendLine($"TYPE: {type.FullName}");
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    sb.AppendLine($"  PROPERTY: {prop.PropertyType.FullName} {prop.Name}");
                }
            }
            File.WriteAllText(@"E:\SqlXmlAnalyzer\table_hints.txt", sb.ToString());
            Assert.True(true);
        }
    }
}
