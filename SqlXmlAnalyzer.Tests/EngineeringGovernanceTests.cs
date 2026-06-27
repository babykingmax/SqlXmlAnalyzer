using System;
using System.IO;
using System.Linq;
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

        [Fact]
        public void CiWorkflow_RunsDependencyVulnerabilityScan()
        {
            string workflow = ReadRepositoryFile(".github", "workflows", "ci.yml");

            workflow.Should().Contain("dotnet list SqlXmlAnalyzer.sln package --vulnerable --include-transitive");
            workflow.Should().Contain("has the following vulnerable packages");
            workflow.Should().Contain("具有下列易受攻击的包");
            workflow.Should().Contain("Vulnerable NuGet packages were found");
        }

        [Fact]
        public void CiWorkflow_FailsWhenTestsProduceArtifacts()
        {
            string workflow = ReadRepositoryFile(".github", "workflows", "ci.yml");

            workflow.Should().Contain("git status --porcelain --untracked-files=all");
            workflow.Should().Contain("Working tree changed during CI");
        }

        [Fact]
        public void GitIgnore_ExcludesLocalGeneratedReviewReports()
        {
            string gitIgnore = ReadRepositoryFile(".gitignore");

            gitIgnore.Should().Contain("SqlXmlAnalyzer_Review_Report_*.docx");
        }

        [Fact]
        public void DirectoryPackages_PinsKnownVulnerableTransitiveDependenciesToSafeVersions()
        {
            string packageVersions = ReadRepositoryFile("Directory.Packages.props");

            packageVersions.Should().Contain("<PackageVersion Include=\"Azure.Identity\" Version=\"1.21.0\" />");
            packageVersions.Should().Contain("<PackageVersion Include=\"Microsoft.Identity.Client\" Version=\"4.85.2\" />");
            packageVersions.Should().Contain("<PackageVersion Include=\"Microsoft.Identity.Client.Extensions.Msal\" Version=\"4.85.2\" />");
            packageVersions.Should().Contain("<PackageVersion Include=\"System.Formats.Asn1\" Version=\"10.0.9\" />");
        }

        private static string ReadRepositoryFile(params string[] relativePathParts)
        {
            string repositoryRoot = FindRepositoryRoot();
            return File.ReadAllText(Path.Combine(new[] { repositoryRoot }.Concat(relativePathParts).ToArray()));
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SqlXmlAnalyzer.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");
        }
    }
}
