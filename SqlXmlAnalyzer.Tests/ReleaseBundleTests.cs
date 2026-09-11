using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Release;

namespace SqlXmlAnalyzer.Tests;

public sealed class ReleaseBundleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP30-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "backup");
    private string Target => Path.Combine(_root, "restored");

    private string CreateBundle()
    {
        Directory.CreateDirectory(Path.Combine(Source, "data"));
        File.WriteAllBytes(Path.Combine(Source, "data", "original.sql"), Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("SELECT N'原件';\r\n")).ToArray());
        File.WriteAllText(Path.Combine(Source, "app.exe"), "old-application-bytes");
        return ReleaseBundle.Seal(Source, "baseline-123", "recovery");
    }

    [Fact]
    public void Restore_VerifiesBytesAndPreservesBackupAndBom()
    {
        string hash = CreateBundle();
        var before = File.ReadAllBytes(Path.Combine(Source, "data", "original.sql"));
        ReleaseBundle.Restore(Source, hash, Target);
        ReleaseBundle.Verify(Target, hash).Kind.Should().Be("recovery");
        File.ReadAllBytes(Path.Combine(Target, "data", "original.sql")).Should().Equal(before);
        File.ReadAllBytes(Path.Combine(Source, "data", "original.sql")).Should().Equal(before);
        ReleaseBundle.Hash(Path.Combine(Source, ReleaseBundle.ManifestName)).Should().Be(hash);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("manifest")]
    public void Restore_RejectsChangedBackupBeforeCreatingTarget(string change)
    {
        string hash = CreateBundle();
        string app = Path.Combine(Source, "app.exe");
        if (change == "changed") File.WriteAllText(app, "new-application-bytes"); // Same length.
        if (change == "missing") File.Delete(app);
        if (change == "extra") File.WriteAllText(Path.Combine(Source, "extra.dll"), "unexpected");
        if (change == "manifest") File.AppendAllText(Path.Combine(Source, ReleaseBundle.ManifestName), " ");
        Action restore = () => ReleaseBundle.Restore(Source, hash, Target);
        restore.Should().Throw<InvalidDataException>();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public void Restore_DoesNotReplaceExistingInstallation()
    {
        string hash = CreateBundle();
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "app.exe"), "current-installation");
        Action restore = () => ReleaseBundle.Restore(Source, hash, Target);
        restore.Should().Throw<IOException>();
        File.ReadAllText(Path.Combine(Target, "app.exe")).Should().Be("current-installation");
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/outside")]
    [InlineData("data\\outside")]
    [InlineData("data//file")]
    [InlineData("data/file.")]
    [InlineData("data/CON.txt")]
    [InlineData("bundle-manifest.json")]
    public void Verify_RejectsUnsafeManifestPathsEvenWithMatchingManifestHash(string path)
    {
        CreateBundle();
        var manifest = new ReleaseManifest(1, "test", "recovery", DateTime.UtcNow, false,
            new[] { new ReleaseFile(path, 0, new string('A', 64)) });
        string file = Path.Combine(Source, ReleaseBundle.ManifestName);
        File.WriteAllText(file, JsonSerializer.Serialize(manifest));
        Action verify = () => ReleaseBundle.Verify(Source, ReleaseBundle.Hash(file));
        verify.Should().Throw<InvalidDataException>().WithMessage("RELEASE_PATH_INVALID");
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("future")]
    [InlineData("approved")]
    [InlineData("negative")]
    [InlineData("hash")]
    public void Verify_RejectsInvalidManifestMetadata(string change)
    {
        string hash = CreateBundle();
        var manifest = ReleaseBundle.Verify(Source, hash);
        manifest = change switch
        {
            "duplicate" => manifest with { Files = manifest.Files.Append(manifest.Files[0] with { Path = manifest.Files[0].Path.ToUpperInvariant() }).ToArray() },
            "future" => manifest with { SchemaVersion = 99 },
            "approved" => manifest with { ApprovedForDistribution = true },
            "negative" => manifest with { Files = new[] { manifest.Files[0] with { Bytes = -1 } } },
            _ => manifest with { Files = new[] { manifest.Files[0] with { Sha256 = "invalid" } } }
        };
        string file = Path.Combine(Source, ReleaseBundle.ManifestName);
        File.WriteAllText(file, JsonSerializer.Serialize(manifest));
        Action verify = () => ReleaseBundle.Verify(Source, ReleaseBundle.Hash(file));
        verify.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Restore_RejectsNestedTargetAndCancellation()
    {
        string hash = CreateBundle();
        Action nested = () => ReleaseBundle.Restore(Source, hash, Path.Combine(Source, "child"));
        nested.Should().Throw<InvalidDataException>();
        Action cancelled = () => ReleaseBundle.Restore(Source, hash, Target, new CancellationToken(true));
        cancelled.Should().Throw<OperationCanceledException>();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public void Verify_RejectsJunctionWithoutTraversingItsFiles()
    {
        string hash = CreateBundle();
        string link = Path.Combine(Source, "linked");
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "private.txt"), "outside");
        string script = Path.Combine(_root, "junction.ps1");
        File.WriteAllText(script, "param($Link, $Target)\n$ErrorActionPreference='Stop'\nNew-Item -ItemType Junction -Path $Link -Target $Target | Out-Null");
        var start = new System.Diagnostics.ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "-NoProfile", "-File", script, "-Link", link, "-Target", Target }) start.ArgumentList.Add(argument);
        using (var process = System.Diagnostics.Process.Start(start)!)
        {
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Junction setup timed out."); }
            process.ExitCode.Should().Be(0);
        }
        try
        {
            Action verify = () => ReleaseBundle.Verify(Source, hash);
            verify.Should().Throw<InvalidDataException>().WithMessage("RELEASE_REPARSE_POINT");
            File.ReadAllText(Path.Combine(Target, "private.txt")).Should().Be("outside");
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void Seal_RejectsExistingManifestWithoutOverwritingIt()
    {
        string hash = CreateBundle();
        Action seal = () => ReleaseBundle.Seal(Source, "new", "candidate");
        seal.Should().Throw<IOException>();
        ReleaseBundle.Hash(Path.Combine(Source, ReleaseBundle.ManifestName)).Should().Be(hash);
    }

    [Fact]
    public void Boundary_ExpectedFailuresAndCancellationRespectLogPolicy()
    {
        string logs = Path.Combine(_root, "logs");
        ReleaseOperation.Run(() => throw new OperationCanceledException(), logs).Should().Be(130);
        ReleaseOperation.Run(() => throw new IOException("synthetic IO failure"), logs).Should().Be(1);
        string log = File.ReadAllText(Path.Combine(logs, "release.log"));
        log.Should().Contain("[ERROR]").And.NotContain("[CRITICAL]");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        Directory.Exists(Path.Combine(logs, "dumps")).Should().BeFalse();
    }

    [Fact]
    public void Boundary_UnknownWrappedFailureCreatesRealDumpAndReturnsFailure()
    {
        string logs = Path.Combine(_root, "logs");
        var error = new InvalidOperationException("IMP30 synthetic unknown failure");
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), Path.Combine(logs, "dumps"));
        ReleaseOperation.Run(() => throw new System.Reflection.TargetInvocationException(error), logs, reporter).Should().Be(1);
        var diagnostic = reporter.Report(error, "verification");
        diagnostic.DumpCreated.Should().BeTrue(diagnostic.Summary);
        MinidumpValidator.Validate(diagnostic.DumpPath!);
        File.ReadAllText(Path.Combine(logs, "release.log")).Should().Contain("[CRITICAL]");
    }

    public void Dispose() { Logger.Shutdown(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
