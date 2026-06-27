namespace SqlXmlAnalyzer.Core.Services
{
    public enum PlanGraphConnectionLayout
    {
        Horizontal,
        Vertical
    }

    public sealed record PlanGraphConnectionGeometryNode(
        double X,
        double Y);

    public sealed record PlanGraphConnectionPoint(
        double X,
        double Y);

    public sealed class PlanGraphConnectionGeometryService
    {
        private const double NodeWidth = 228;
        private const double NodeHeight = 70;
        private const double HorizontalConnectionOffsetY = 35;
        private const double VerticalConnectionOffsetX = 115;

        public double GetArrowAngle(PlanGraphConnectionLayout layout)
        {
            return layout == PlanGraphConnectionLayout.Horizontal ? 180 : -90;
        }

        public PlanGraphConnectionPoint CalculateSourceLocation(
            PlanGraphConnectionGeometryNode? source,
            PlanGraphConnectionGeometryNode? target,
            PlanGraphConnectionLayout layout)
        {
            if (source == null)
            {
                return new PlanGraphConnectionPoint(0, 0);
            }

            if (layout == PlanGraphConnectionLayout.Horizontal)
            {
                return new PlanGraphConnectionPoint(
                    target == null || source.X > target.X
                        ? source.X
                        : source.X + NodeWidth,
                    source.Y + HorizontalConnectionOffsetY);
            }

            return new PlanGraphConnectionPoint(
                source.X + VerticalConnectionOffsetX,
                target == null || source.Y > target.Y
                    ? source.Y
                    : source.Y + NodeHeight);
        }

        public PlanGraphConnectionPoint CalculateTargetLocation(
            PlanGraphConnectionGeometryNode? source,
            PlanGraphConnectionGeometryNode? target,
            PlanGraphConnectionLayout layout)
        {
            if (target == null)
            {
                return new PlanGraphConnectionPoint(0, 0);
            }

            if (layout == PlanGraphConnectionLayout.Horizontal)
            {
                return new PlanGraphConnectionPoint(
                    source == null || source.X > target.X
                        ? target.X + NodeWidth
                        : target.X,
                    target.Y + HorizontalConnectionOffsetY);
            }

            return new PlanGraphConnectionPoint(
                target.X + VerticalConnectionOffsetX,
                source == null || source.Y > target.Y
                    ? target.Y + NodeHeight
                    : target.Y);
        }

        public PlanGraphConnectionPoint CalculateLabelLocation(
            PlanGraphConnectionPoint sourceLocation,
            PlanGraphConnectionPoint targetLocation,
            string? labelText)
        {
            double x = (sourceLocation.X + targetLocation.X) / 2;
            double estimatedWidth = 8 + (labelText?.Length ?? 1) * 5.2;
            double y = (sourceLocation.Y + targetLocation.Y) / 2;

            return new PlanGraphConnectionPoint(
                x - estimatedWidth / 2,
                y - 8);
        }
    }
}
