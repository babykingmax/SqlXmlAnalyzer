using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class TuningSessionUiActionServiceTests
    {
        [Fact]
        public async Task OpenSelectedHistorySnapshotAsync_WhenSelectionIsMissing_DoesNotAnalyze()
        {
            var viewModel = new MainViewModel();
            var service = new TuningSessionUiActionService(
                new TuningSessionActionService(new FakeFileDialogService()),
                viewModel);
            using var sessions = new AnalysisSessionCoordinator();
            bool analyzed = false;

            await service.OpenSelectedHistorySnapshotAsync(
                selectedItem: new object(),
                sessions,
                (_, _, _, _) =>
                {
                    analyzed = true;
                    return Task.CompletedTask;
                });

            analyzed.Should().BeFalse();
            viewModel.CurrentPlanFilePath.Should().BeNull();
        }

        [Fact]
        public async Task OpenSelectedHistorySnapshotAsync_WhenSelectionIsSnapshot_UpdatesPathAndAnalyzes()
        {
            var viewModel = new MainViewModel();
            var service = new TuningSessionUiActionService(
                new TuningSessionActionService(new FakeFileDialogService()),
                viewModel);
            using var sessions = new AnalysisSessionCoordinator();
            XDocument document = new(new XElement("ShowPlanXML"));
            var snapshot = new PlanSnapshot
            {
                FilePath = "C:\\Plans\\captured.sqlplan",
                Document = document
            };
            XDocument? analyzedDocument = null;
            string? analyzedPath = null;
            long requestId = 0;
            CancellationToken token = CancellationToken.None;

            await service.OpenSelectedHistorySnapshotAsync(
                snapshot,
                sessions,
                (doc, path, id, cancellationToken) =>
                {
                    analyzedDocument = doc;
                    analyzedPath = path;
                    requestId = id;
                    token = cancellationToken;
                    return Task.CompletedTask;
                });

            viewModel.CurrentPlanFilePath.Should().Be(snapshot.FilePath);
            analyzedDocument.Should().BeSameAs(document);
            analyzedPath.Should().Be(snapshot.FilePath);
            requestId.Should().Be(1);
            token.CanBeCanceled.Should().BeTrue();
        }

        private sealed class FakeFileDialogService : IFileDialogService
        {
            public string? ShowOpenFile(FileDialogRequest request)
            {
                return null;
            }

            public string? ShowSaveFile(FileDialogRequest request)
            {
                return null;
            }
        }
    }
}
