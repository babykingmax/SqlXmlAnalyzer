using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Refactoring;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class EngineeringGovernanceTests
    {
        [Fact]
        public void ProductVersion_ComesFromAssemblyMetadata()
        {
            ProductInfo.Version.Should().NotBeNullOrWhiteSpace();
            ProductInfo.Version.Should().NotBe("1.0.0");
        }

        [Fact]
        public void MermaidHtml_EncodesInputAndUsesRestrictiveConfiguration()
        {
            const string malicious = "<img src=x onerror=alert(1)>";
            const string nonce = "test-nonce";

            string html = BrowserLauncher.CreateMermaidHtml(malicious, nonce);

            html.Should().Contain("&lt;img src=x onerror=alert(1)&gt;");
            html.Should().NotContain(malicious);
            html.Should().Contain("securityLevel: 'strict'");
            html.Should().Contain("htmlLabels: false");
            html.Should().Contain($"'nonce-{nonce}'");
            html.Should().Contain($"nonce=\"{nonce}\"");
        }

        [Fact]
        public void RefactoringArchitecture_ExposesSinglePublicEngineImplementation()
        {
            var publicImplementations = typeof(SqlRefactoringEngine).Assembly
                .GetExportedTypes()
                .Where(type =>
                    typeof(IRefactoringEngine).IsAssignableFrom(type)
                    && !type.IsInterface
                    && !type.IsAbstract)
                .ToList();
            Type? legacyEngine = typeof(ProductInfo).Assembly.GetType(
                "SqlXmlAnalyzer.Core.Refactoring.SqlRefactorEngine");

            publicImplementations.Should().ContainSingle()
                .Which.Should().Be(typeof(SqlRefactoringEngine));
            legacyEngine.Should().NotBeNull();
            legacyEngine!.IsNotPublic.Should().BeTrue();
        }
    }
}
