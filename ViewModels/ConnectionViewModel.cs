using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace SqlXmlAnalyzer
{
    public class ConnectionViewModel : INotifyPropertyChanged
    {
        private PlanNodeViewModel? _source;
        private PlanNodeViewModel? _target;
        private static readonly Core.Services.PlanGraphConnectionDisplayService ConnectionDisplayService = new();
        private static readonly Core.Services.PlanGraphConnectionGeometryService ConnectionGeometryService = new();

        private static readonly Brush DefaultBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C));
        private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
        private static readonly Brush OrangeBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00));
        private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x8E, 0x3C));

        static ConnectionViewModel()
        {
            DefaultBrush.Freeze();
            RedBrush.Freeze();
            OrangeBrush.Freeze();
            GreenBrush.Freeze();
        }

        private static Core.Services.PlanGraphConnectionNodeInfo? ToConnectionNodeInfo(
            PlanNodeViewModel? node)
        {
            return node == null
                ? null
                : new Core.Services.PlanGraphConnectionNodeInfo(
                    node.PhysicalOp,
                    node.EstRowsNum,
                    node.ActualRows,
                    node.ActualRowsNum,
                    node.AvgRowSizeNum);
        }

        private static Core.Services.PlanGraphConnectionMetricKind ToMetricKind(
            LinkMetricMode metricMode)
        {
            return metricMode == LinkMetricMode.DataSize
                ? Core.Services.PlanGraphConnectionMetricKind.DataSize
                : Core.Services.PlanGraphConnectionMetricKind.RowCount;
        }

        private static Core.Services.PlanGraphConnectionGeometryNode? ToGeometryNode(
            PlanNodeViewModel? node)
        {
            return node == null
                ? null
                : new Core.Services.PlanGraphConnectionGeometryNode(
                    node.Location.X,
                    node.Location.Y);
        }

        private static Core.Services.PlanGraphConnectionLayout ToConnectionLayout(
            PlanLayoutMode layoutMode)
        {
            return layoutMode == PlanLayoutMode.Horizontal
                ? Core.Services.PlanGraphConnectionLayout.Horizontal
                : Core.Services.PlanGraphConnectionLayout.Vertical;
        }

        private static Point ToPoint(
            Core.Services.PlanGraphConnectionPoint point)
        {
            return new Point(point.X, point.Y);
        }

        private static Core.Services.PlanGraphConnectionPoint ToConnectionPoint(
            Point point)
        {
            return new Core.Services.PlanGraphConnectionPoint(point.X, point.Y);
        }

        private static Brush ToStrokeBrush(
            Core.Services.PlanGraphConnectionStrokeKey strokeKey)
        {
            return strokeKey switch
            {
                Core.Services.PlanGraphConnectionStrokeKey.Red => RedBrush,
                Core.Services.PlanGraphConnectionStrokeKey.Orange => OrangeBrush,
                Core.Services.PlanGraphConnectionStrokeKey.Green => GreenBrush,
                _ => DefaultBrush
            };
        }

        private PlanLayoutMode _layoutMode = PlanLayoutMode.Horizontal;

        public double ArrowAngle
        {
            get
            {
                return ConnectionGeometryService.GetArrowAngle(
                    ToConnectionLayout(LayoutMode));
            }
        }

        public PlanLayoutMode LayoutMode
        {
            get => _layoutMode;
            set
            {
                _layoutMode = value;
                OnPropertyChanged(nameof(LayoutMode));
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
            }
        }

        private LinkMetricMode _currentLinkMetric = LinkMetricMode.RowCount;
        public LinkMetricMode CurrentLinkMetric
        {
            get => _currentLinkMetric;
            set
            {
                _currentLinkMetric = value;
                OnPropertyChanged(nameof(CurrentLinkMetric));
                OnPropertyChanged(nameof(ThicknessValue));
                OnPropertyChanged(nameof(LabelText));
                OnPropertyChanged(nameof(ToolTipText));
            }
        }

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

        public PlanNodeViewModel? Source
        {
            get => _source;
            set
            {
                if (_source != null)
                    _source.PropertyChanged -= OnSourcePropertyChanged;
                _source = value;
                if (_source != null)
                    _source.PropertyChanged += OnSourcePropertyChanged;
                OnPropertyChanged(nameof(Source));
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
                OnPropertyChanged(nameof(RowsCount));
                OnPropertyChanged(nameof(DataSizeVal));
                OnPropertyChanged(nameof(StrokeBrush));
                OnPropertyChanged(nameof(ToolTipText));
                OnPropertyChanged(nameof(LabelText));
                OnPropertyChanged(nameof(ThicknessValue));
            }
        }

        public PlanNodeViewModel? Target
        {
            get => _target;
            set
            {
                if (_target != null)
                    _target.PropertyChanged -= OnTargetPropertyChanged;
                _target = value;
                if (_target != null)
                    _target.PropertyChanged += OnTargetPropertyChanged;
                OnPropertyChanged(nameof(Target));
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
                OnPropertyChanged(nameof(RowsCount));
                OnPropertyChanged(nameof(DataSizeVal));
                OnPropertyChanged(nameof(StrokeBrush));
                OnPropertyChanged(nameof(ToolTipText));
                OnPropertyChanged(nameof(LabelText));
                OnPropertyChanged(nameof(ThicknessValue));
            }
        }

        public double RowsCount =>
            ConnectionDisplayService.CalculateRowsCount(
                ToConnectionNodeInfo(Source));

        public double DataSizeVal =>
            ConnectionDisplayService.CalculateDataSize(
                ToConnectionNodeInfo(Source));

        public double ThicknessValue
        {
            get
            {
                double val = ConnectionDisplayService.GetMetricValue(
                    ToMetricKind(CurrentLinkMetric),
                    ToConnectionNodeInfo(Source));

                return Core.Services.PlanGraphMetricService.CalculateLinkThickness(val);
            }
        }

        public Point SourceLocation
        {
            get
            {
                return ToPoint(ConnectionGeometryService.CalculateSourceLocation(
                    ToGeometryNode(Source),
                    ToGeometryNode(Target),
                    ToConnectionLayout(LayoutMode)));
            }
        }

        public Point TargetLocation
        {
            get
            {
                return ToPoint(ConnectionGeometryService.CalculateTargetLocation(
                    ToGeometryNode(Source),
                    ToGeometryNode(Target),
                    ToConnectionLayout(LayoutMode)));
            }
        }

        private Core.Services.PlanGraphConnectionPoint LabelLocation =>
            ConnectionGeometryService.CalculateLabelLocation(
                ToConnectionPoint(SourceLocation),
                ToConnectionPoint(TargetLocation),
                LabelText);

        public double MidpointX => LabelLocation.X;
        public double MidpointY => LabelLocation.Y;

        private bool _isHighlighted = true;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                _isHighlighted = value;
                OnPropertyChanged(nameof(IsHighlighted));
                OnPropertyChanged(nameof(Opacity));
            }
        }

        public double Opacity => IsHighlighted ? 1.0 : 0.35;

        public Brush StrokeBrush
        {
            get
            {
                Core.Services.PlanGraphConnectionStrokeKey strokeKey =
                    ConnectionDisplayService.GetStrokeKey(
                        ToConnectionNodeInfo(Source));
                return ToStrokeBrush(strokeKey);
            }
        }

        public string LabelText
        {
            get
            {
                return ConnectionDisplayService.BuildLabel(
                    ToMetricKind(CurrentLinkMetric),
                    ToConnectionNodeInfo(Source));
            }
        }

        public string ToolTipText
        {
            get
            {
                return ConnectionDisplayService.BuildToolTip(
                    ToConnectionNodeInfo(Source),
                    Target?.PhysicalOp);
            }
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanNodeViewModel.Location))
            {
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
            }
        }

        private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanNodeViewModel.Location))
            {
                OnPropertyChanged(nameof(SourceLocation));
                OnPropertyChanged(nameof(ArrowAngle));
                OnPropertyChanged(nameof(TargetLocation));
                OnPropertyChanged(nameof(MidpointX));
                OnPropertyChanged(nameof(MidpointY));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
