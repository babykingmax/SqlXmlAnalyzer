using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Privacy;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Refactoring;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class ReviewWorkspaceHardeningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP21-Hardening-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("CurrentScore", true)]
    [InlineData("CurrentScore", false)]
    [InlineData("Error", true)]
    [InlineData("Error", false)]
    [InlineData("MaxDop", true)]
    [InlineData("MaxDop", false)]
    public void FailedSubscriber_DoesNotLeaveWpfBindingsWithOldScriptsOrExportEnabled(string failingProperty, bool subscribeFirst)
    {
        PlanIdentityHardeningTests.RunOnStaThread(() =>
        {
            var reporter = new Reporter();
            var model = new IndexSandboxViewModel(ReviewWorkspaceTests.Suggestion(), unexpectedErrors: reporter);
            PropertyChangedEventHandler failing = (_, e) =>
            {
                if (e.PropertyName == failingProperty) throw new InvalidOperationException("synthetic binding subscriber failure");
            };
            if (subscribeFirst) model.PropertyChanged += failing;
            var create = BindText(model, nameof(model.CreateIndexStatement));
            var rollback = BindText(model, nameof(model.RollbackStatement));
            var name = BindText(model, nameof(model.CompiledIndexName));
            var error = BindText(model, nameof(model.Error));
            var status = BindText(model, nameof(model.OutputStatus));
            var copy = new Button();
            BindingOperations.SetBinding(copy, UIElement.IsEnabledProperty, new Binding(nameof(model.CanExport)) { Source = model });
            if (!subscribeFirst) model.PropertyChanged += failing;
            create.Text.Should().NotBeEmpty(); rollback.Text.Should().NotBeEmpty(); copy.IsEnabled.Should().BeTrue();

            Action edit = () => model.MaxDop = "2";
            edit.Should().NotThrow();
            create.Text.Should().BeEmpty(); rollback.Text.Should().BeEmpty(); name.Text.Should().BeEmpty();
            copy.IsEnabled.Should().BeFalse();
            error.Text.Should().Contain("synthetic binding subscriber failure").And.Contain("DUMP 生成失败");
            status.Text.Should().Contain("旧脚本已清除");
            reporter.Operations.Should().Contain("IndexSandboxViewModel.Recalculate");

            model.PropertyChanged -= failing;
            model.MaxDop = "3";
            create.Text.Should().Contain("MAXDOP = 3"); rollback.Text.Should().NotBeEmpty(); name.Text.Should().NotBeEmpty();
            error.Text.Should().BeEmpty(); copy.IsEnabled.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RewriteExport_LeavesSqlExactAndShowsRawPrivacyNotice(bool save)
    {
        Directory.CreateDirectory(_directory);
        var model = new RewriteReviewViewModel(RewriteProposalTests.Sql, RewriteProposalTests.Run().Review!);
        model.Items[0].IsSelected = true;
        string? copied = null;
        if (save)
        {
            string path = Path.Combine(_directory, "candidate.sql");
            model.SaveCandidateNew(path);
            File.ReadAllBytes(path).Should().Equal(new System.Text.UTF8Encoding(false, true).GetBytes(model.PreviewSql));
        }
        else
        {
            model.CopyCandidate(sql => copied = sql);
            copied.Should().Be(model.PreviewSql);
        }
        model.OutputStatus.Should().Contain(OutputPrivacy.RawNotice);
        model.OutputStatus.Should().NotContain("尚未应用");
    }

    [Fact]
    public void IndexCopy_LeavesExecutableDdlExactAndShowsRawPrivacyNotice()
    {
        var model = new IndexSandboxViewModel(ReviewWorkspaceTests.Suggestion());
        string? copied = null;
        model.CopyScript(sql => copied = sql);
        copied.Should().Be(model.CreateIndexStatement).And.NotContain(OutputPrivacy.RawNotice);
        model.OutputStatus.Should().Contain(OutputPrivacy.RawNotice);
    }

    private static TextBox BindText(object source, string property)
    {
        var control = new TextBox();
        BindingOperations.SetBinding(control, TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.OneWay });
        return control;
    }

    private sealed class Reporter : IUnexpectedErrorReporter
    {
        public List<string> Operations { get; } = [];
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Operations.Add(operation);
            return new(null, null, "synthetic DUMP writer failure");
        }
    }

    public void Dispose()
    {
        if (!Path.GetFullPath(_directory).StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP21-Hardening-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
