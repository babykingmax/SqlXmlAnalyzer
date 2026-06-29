using System;
using System.Windows.Controls;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Services
{
    internal sealed record PlanAnalysisUiResult(
        string QueryText,
        string RefactoredSql);

    internal sealed class PlanAnalysisUiActionService
    {
        private readonly Core.ViewModels.MainViewModel _viewModel;
        private readonly Core.Services.PlanTreeService _planTreeService;
        private readonly Core.Services.PlanOperatorTreeViewRenderer _operatorTreeViewRenderer;
        private readonly TextBox _planXmlTextBox;
        private readonly TextBox _planStatementTextBox;
        private readonly TextBox _planWarningsTextBox;
        private readonly TreeView _planOperatorTree;
        private readonly TreeView _planVisualTree;
        private readonly PlanGraphControl _planGraphControl;
        private readonly TabControl _mainTabControl;
        private readonly TabControl _planGraphTabControl;

        public PlanAnalysisUiActionService(
            Core.ViewModels.MainViewModel viewModel,
            Core.Services.PlanTreeService planTreeService,
            Core.Services.PlanOperatorTreeViewRenderer operatorTreeViewRenderer,
            TextBox planXmlTextBox,
            TextBox planStatementTextBox,
            TextBox planWarningsTextBox,
            TreeView planOperatorTree,
            TreeView planVisualTree,
            PlanGraphControl planGraphControl,
            TabControl mainTabControl,
            TabControl planGraphTabControl)
        {
            _viewModel = viewModel
                ?? throw new ArgumentNullException(nameof(viewModel));
            _planTreeService = planTreeService
                ?? throw new ArgumentNullException(nameof(planTreeService));
            _operatorTreeViewRenderer = operatorTreeViewRenderer
                ?? throw new ArgumentNullException(nameof(operatorTreeViewRenderer));
            _planXmlTextBox = planXmlTextBox
                ?? throw new ArgumentNullException(nameof(planXmlTextBox));
            _planStatementTextBox = planStatementTextBox
                ?? throw new ArgumentNullException(nameof(planStatementTextBox));
            _planWarningsTextBox = planWarningsTextBox
                ?? throw new ArgumentNullException(nameof(planWarningsTextBox));
            _planOperatorTree = planOperatorTree
                ?? throw new ArgumentNullException(nameof(planOperatorTree));
            _planVisualTree = planVisualTree
                ?? throw new ArgumentNullException(nameof(planVisualTree));
            _planGraphControl = planGraphControl
                ?? throw new ArgumentNullException(nameof(planGraphControl));
            _mainTabControl = mainTabControl
                ?? throw new ArgumentNullException(nameof(mainTabControl));
            _planGraphTabControl = planGraphTabControl
                ?? throw new ArgumentNullException(nameof(planGraphTabControl));
        }

        public PlanAnalysisUiResult Apply(Core.Services.PlanDocumentResult documentResult)
        {
            ArgumentNullException.ThrowIfNull(documentResult);

            XDocument document = documentResult.Document;
            XNamespace showplanNamespace = documentResult.ShowplanNamespace;
            Core.Services.PlanAnalysisOutput analysis = documentResult.Analysis;

            _viewModel.CurrentPlanDoc = document;
            _viewModel.ActivateWorkspace(Core.ViewModels.WorkspaceMode.ExecutionPlan);
            _viewModel.MissingIndexes.Clear();
            foreach (MissingIndexSuggestion missingIndex in analysis.MissingIndexes)
            {
                _viewModel.MissingIndexes.Add(missingIndex);
            }

            _planXmlTextBox.Text = analysis.DocumentText;
            _planStatementTextBox.Text = analysis.QueryText.Length > 800
                ? analysis.QueryText.Substring(0, 800) + "..."
                : analysis.QueryText;
            _planWarningsTextBox.Text = analysis.WarningsText;

            _planVisualTree.ItemsSource =
                _planTreeService.BuildVisualTree(document, showplanNamespace);

            Core.Services.PlanOperatorTreeNode? root =
                _planTreeService.BuildOperatorTree(document, showplanNamespace);
            _planOperatorTree.Items.Clear();
            if (root != null)
            {
                _planOperatorTree.Items.Add(_operatorTreeViewRenderer.Render(root));
            }

            try
            {
                _planGraphControl.LoadFromExecutionPlan(document, showplanNamespace);
            }
            catch (Exception ex)
            {
                Logger.LogException("Load Nodify Graph", ex);
            }

            _mainTabControl.SelectedIndex = 1;
            _planGraphTabControl.SelectedIndex = 1;

            return new PlanAnalysisUiResult(
                analysis.QueryText,
                analysis.RefactoredSql);
        }
    }
}
