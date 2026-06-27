using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record PlanSwapResult(
        PlanSnapshot? PlanA,
        PlanSnapshot? PlanB);

    public sealed class TuningSessionActionService
    {
        private const string SessionFilter = "SqlXmlAnalyzer tuning session (*.pesession)|*.pesession";
        private readonly IFileDialogService _fileDialogService;

        public TuningSessionActionService(IFileDialogService? fileDialogService = null)
        {
            _fileDialogService = fileDialogService ?? new WpfFileDialogService();
        }

        public string? ChooseSaveSessionPath()
        {
            return _fileDialogService.ShowSaveFile(
                new FileDialogRequest(
                    SessionFilter,
                    "Save current tuning session",
                    ".pesession",
                    "Tuning_Session.pesession"));
        }

        public string? ChooseLoadSessionPath()
        {
            return _fileDialogService.ShowOpenFile(
                new FileDialogRequest(
                    SessionFilter,
                    "Open tuning session",
                    ".pesession"));
        }

        public PlanSwapResult SwapPlans(
            PlanSnapshot? planA,
            PlanSnapshot? planB)
        {
            return new PlanSwapResult(planB, planA);
        }
    }
}
