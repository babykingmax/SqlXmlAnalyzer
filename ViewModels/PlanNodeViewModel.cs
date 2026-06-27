using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    public class PlanNodeViewModel : INotifyPropertyChanged
    {
        private DiagramViewMode _viewMode = DiagramViewMode.CostPercent;
        public DiagramViewMode ViewMode
        {
            get => _viewMode;
            set
            {
                _viewMode = value;
                OnPropertyChanged(nameof(ViewMode));
                OnPropertyChanged(nameof(PrimaryDisplayValue));
            }
        }

        private PlanColorMode _colorMode = PlanColorMode.TotalCost;
        public PlanColorMode ColorMode
        {
            get => _colorMode;
            set
            {
                _colorMode = value;
                OnPropertyChanged(nameof(ColorMode));
                OnPropertyChanged(nameof(ActivePercent));
                OnPropertyChanged(nameof(DynamicBackgroundBrush));
                OnPropertyChanged(nameof(DynamicBorderBrush));
                OnPropertyChanged(nameof(DynamicBorderThickness));
                OnPropertyChanged(nameof(PrimaryDisplayValue));
                OnPropertyChanged(nameof(CostBadgeBrush));
                OnPropertyChanged(nameof(CostBadgeForeground));
            }
        }

        public double ActivePercent
        {
            get
            {
                return ColorMode switch
                {
                    PlanColorMode.TotalCost => CostPercent,
                    PlanColorMode.CpuCost => CpuPercent,
                    PlanColorMode.IoCost => IoPercent,
                    _ => CostPercent
                };
            }
        }

        public double AvgRowSizeNum { get; set; }
        public double EstimatedCPUCostNum { get; set; }
        public double EstimatedIOCostNum { get; set; }
        public double CpuPercent { get; set; }
        public double IoPercent { get; set; }

        public string NodeId { get; set; } = "?";
        public string PhysicalOp { get; set; } = "Unknown";
        public string LogicalOp { get; set; } = "";
        public double Cost { get; set; }
        public double OwnCost { get; set; }
        public double SubtreeCost { get; set; }
        public int CostPercent { get; set; }
        public string EstRows { get; set; } = "0";
        public double EstRowsNum { get; set; }
        public string ActualRows { get; set; } = "";
        public double ActualRowsNum { get; set; }
        public string ObjectDetails { get; set; } = "";

        public double X { get; set; }
        public double Y { get; set; }
        public double SubtreeWidth { get; set; }
        public Geometry? IconGeometry { get; set; }
        public Brush? IconBrush { get; set; }

        public string OperatorType { get; set; } = "Other";
        public bool IsParallel { get; set; }
        public string Warnings { get; set; } = "";
        private static readonly Core.Services.PlanGraphCostVisualService CostVisualService = new();
        private static readonly Core.Services.PlanGraphNodeDisplayService NodeDisplayService = new();
        private static readonly Core.Services.PlanGraphOperatorVisualService OperatorVisualService = new();
        private static readonly Core.Services.PlanGraphRowSkewService RowSkewService = new();

        private SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion? _associatedSuggestion;
        public SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion? AssociatedSuggestion
        {
            get => _associatedSuggestion;
            set
            {
                if (_associatedSuggestion != value)
                {
                    _associatedSuggestion = value;
                    OnPropertyChanged(nameof(AssociatedSuggestion));
                    OnPropertyChanged(nameof(HasIndexRecommendation));
                    OnPropertyChanged(nameof(MissingIndexOverlayVisible));
                    OnPropertyChanged(nameof(MissingIndexTooltip));
                }
            }
        }

        public bool HasIndexRecommendation => _associatedSuggestion != null;
        public string MissingIndexOverlayVisible => HasIndexRecommendation ? "Visible" : "Collapsed";
        public string MissingIndexTooltip => _associatedSuggestion != null
            ? $"\u5305\u542b\u7d22\u5f15\u63a8\u8350:\n{_associatedSuggestion.CreateIndexStatement}\n\n\u70b9\u51fb\u5728\u6b64\u8868\u4e0a\u6253\u5f00\u7d22\u5f15\u4f18\u5316\u6c99\u76d2\u6a21\u62df\u3002"
            : string.Empty;

        public XElement? RawElement { get; set; }

        public double ActualRecost { get; set; }
        public string ExecutionMode { get; set; } = "Row";
        public string ActualRowsRead { get; set; } = "";
        public string EstimatedRowsToBeRead { get; set; } = "";
        public string EstimatedIOCost { get; set; } = "";
        public string EstimatedCPUCost { get; set; } = "";
        public string ActualExecutions { get; set; } = "";
        public string EstimatedExecutions { get; set; } = "";
        public string EstimatedOperatorCost { get; set; } = "";
        public string EstimatedSubtreeCostStr { get; set; } = "";
        public string EstimatedRowSize { get; set; } = "";
        public string ActualDataSize { get; set; } = "";
        public string EstimatedDataSize { get; set; } = "";
        public string ActualRebinds { get; set; } = "0";
        public string ActualRewinds { get; set; } = "0";
        public string Ordered { get; set; } = "False";
        public string DatabaseName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string IndexName { get; set; } = "";
        public string SeekPredicates { get; set; } = "";
        public string Predicate { get; set; } = "";
        public string OutputList { get; set; } = "";

        public string Partitioned { get; set; } = "False";
        public string PartitionCount { get; set; } = "";
        public string PartitionRange { get; set; } = "";

        public bool IsFullPartitionScan => NodeDisplayService.IsFullPartitionScan(Partitioned, PartitionCount, PartitionRange);
        public string PartitionRangeColor => NodeDisplayService.GetPartitionRangeColor(Partitioned, PartitionCount, PartitionRange);
        public string PartitionLabelColor => NodeDisplayService.GetPartitionLabelColor(Partitioned, PartitionCount, PartitionRange);

        public string HasSeekPredicates => NodeDisplayService.GetTextVisibility(SeekPredicates);
        public string HasPredicate => NodeDisplayService.GetTextVisibility(Predicate);
        public string HasOutputList => NodeDisplayService.GetTextVisibility(OutputList);
        public string HasPartitionInfo => NodeDisplayService.GetPartitionInfoVisibility(Partitioned);

        public string NodeSeverity { get; set; } = "Info";
        public string NodeSeverityColor => NodeDisplayService.GetNodeSeverityColor(NodeSeverity);
        public string NodeSeverityBorderThickness => NodeDisplayService.GetNodeSeverityBorderThickness(NodeSeverity);

        private bool _isCollapsed;
        public bool IsCollapsed
        {
            get => _isCollapsed;
            set
            {
                _isCollapsed = value;
                OnPropertyChanged(nameof(IsCollapsed));
                OnPropertyChanged(nameof(CollapseButtonText));
            }
        }

        public string CollapseButtonText => IsCollapsed ? "+" : "-";

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }

        private bool _hasChildren;
        public bool HasChildren
        {
            get => _hasChildren;
            set
            {
                _hasChildren = value;
                OnPropertyChanged(nameof(HasChildren));
                OnPropertyChanged(nameof(CollapseButtonVisibility));
            }
        }

        public string CollapseButtonVisibility => NodeDisplayService.GetBooleanVisibility(HasChildren);

        private Point _location;
        public Point Location { get => _location; set { _location = value; OnPropertyChanged(nameof(Location)); } }

        public string LogicalOpSuffix => string.IsNullOrEmpty(LogicalOp) || LogicalOp == PhysicalOp ? "" : $"({LogicalOp})";

        public string PrimaryDisplayValue
        {
            get
            {
                return ViewMode switch
                {
                    DiagramViewMode.CostPercent => ColorMode switch
                    {
                        PlanColorMode.TotalCost => $"Cost: {CostPercent}%",
                        PlanColorMode.CpuCost => $"CPU: {CpuPercent:F1}%",
                        PlanColorMode.IoCost => $"I/O: {IoPercent:F1}%",
                        _ => $"Cost: {CostPercent}%"
                    },
                    DiagramViewMode.CpuIo => $"C: {EstimatedCPUCost}\nI: {EstimatedIOCost}",
                    DiagramViewMode.Rows => $"R: {(ActualRowsNum > 0 ? ActualRows : EstRows)}",
                    _ => $"{CostPercent}%"
                };
            }
        }

        public string ActualRowsDisplay => string.IsNullOrEmpty(ActualRows) ? "N/A" : ActualRows;

        public Brush DynamicBackgroundBrush
        {
            get
            {
                Core.Services.PlanGraphCostVisualStyle style =
                    CostVisualService.GetStyle(ActivePercent);
                return new LinearGradientBrush(
                    CreateColor(style.BackgroundTopColorHex),
                    CreateColor(style.BackgroundBottomColorHex),
                    90.0);
            }
        }

        public Brush DynamicBorderBrush
        {
            get
            {
                Core.Services.PlanGraphCostVisualStyle style =
                    CostVisualService.GetStyle(ActivePercent);
                return CreateBrush(style.BorderColorHex);
            }
        }

        public Thickness DynamicBorderThickness =>
            new(CostVisualService.GetStyle(ActivePercent).BorderThickness);

        private static Color CreateColor(string colorHex)
        {
            object? converted = ColorConverter.ConvertFromString(colorHex);
            return converted is Color color
                ? color
                : Colors.Transparent;
        }

        private static Brush CreateBrush(string colorHex)
            => new SolidColorBrush(CreateColor(colorHex));

        public Brush AccentBrush =>
            CreateBrush(OperatorVisualService.GetStyle(OperatorType).AccentColorHex);

        public string OperatorGeometry =>
            OperatorVisualService.GetStyle(OperatorType).GeometryData;

        public Brush CostBadgeBrush =>
            CreateBrush(CostVisualService.GetStyle(ActivePercent).BadgeBackgroundColorHex);

        public Brush CostBadgeForeground =>
            CreateBrush(CostVisualService.GetStyle(ActivePercent).BadgeForegroundColorHex);

        public Brush ActualRowsBrush
        {
            get
            {
                Core.Services.PlanGraphRowSkewResult result =
                    RowSkewService.Analyze(ActualRowsNum, EstRowsNum);

                return result.BrushKey switch
                {
                    Core.Services.PlanGraphRowSkewBrushKey.DarkRed => Brushes.DarkRed,
                    Core.Services.PlanGraphRowSkewBrushKey.DarkOrange => Brushes.DarkOrange,
                    Core.Services.PlanGraphRowSkewBrushKey.HealthyGreen => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                    _ => Brushes.DimGray
                };
            }
        }

        public string SkewWarning =>
            RowSkewService.Analyze(ActualRowsNum, EstRowsNum).Warning;

        public string HasObjectDetails => NodeDisplayService.GetTextVisibility(ObjectDetails);
        public string IsParallelVisible => NodeDisplayService.GetBooleanVisibility(IsParallel);
        public string HasWarningVisible => NodeDisplayService.GetTextVisibility(Warnings);
        public string HasExtraInfo => NodeDisplayService.GetExtraInfoVisibility(IsParallel, Warnings);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
