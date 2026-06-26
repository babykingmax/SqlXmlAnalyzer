using Xunit;
using System;
using System.Reflection;
using Microsoft.SqlServer.TransactSql.ScriptDom;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SqlXmlAnalyzer.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void ScriptDomExpressions_ExposeExpectedPublicProperties()
        {
            var typesToReflect = new[]
            {
                typeof(BinaryExpression),
                typeof(ParenthesisExpression)
            };

            foreach (var type in typesToReflect)
            {
                PropertyInfo[] properties = type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                Assert.NotEmpty(properties);
            }
        }
    }
}
