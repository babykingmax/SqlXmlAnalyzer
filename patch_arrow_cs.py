import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'r', encoding='utf-8') as f:
    c = f.read()

# 1. Update TargetLocation and SourceLocation to touch exactly the edges (Width=230, Height=100 roughly)
# Horizontal: Target right edge is X + 230. Source left edge is X.
# Wait, currently:
# if (Source.Location.X > Target.Location.X) return new Point(Target.Location.X + 228, Target.Location.Y + 35);
# Let's change 228 to 232 (a bit of padding outside the node) to be safe!
c = c.replace('Target.Location.X + 228', 'Target.Location.X + 235')
c = c.replace('Source.Location.X + 228', 'Source.Location.X + 235')
c = c.replace('Target.Location.Y + 95', 'Target.Location.Y + 105')
c = c.replace('Source.Location.Y + 95', 'Source.Location.Y + 105')

# Also fix the other side of the logic
c = c.replace('Target.Location.X, Target.Location.Y + 35', 'Target.Location.X - 5, Target.Location.Y + 35')
c = c.replace('Source.Location.X, Source.Location.Y + 35', 'Source.Location.X - 5, Source.Location.Y + 35')
c = c.replace('Target.Location.X + 115, Target.Location.Y', 'Target.Location.X + 115, Target.Location.Y - 5')
c = c.replace('Source.Location.X + 115, Source.Location.Y', 'Source.Location.X + 115, Source.Location.Y - 5')

# 2. Add Arrow properties to ConnectionViewModel
arrow_props = """
        public System.Windows.Media.PointCollection ArrowPoints
        {
            get
            {
                if (LayoutMode == PlanLayoutMode.Horizontal)
                {
                    if (Source != null && Target != null && Source.Location.X > Target.Location.X)
                        return new System.Windows.Media.PointCollection(new[] { new System.Windows.Point(14, 0), new System.Windows.Point(0, 7), new System.Windows.Point(14, 14) });
                    else
                        return new System.Windows.Media.PointCollection(new[] { new System.Windows.Point(0, 0), new System.Windows.Point(14, 7), new System.Windows.Point(0, 14) });
                }
                else
                {
                    if (Source != null && Target != null && Source.Location.Y > Target.Location.Y)
                        return new System.Windows.Media.PointCollection(new[] { new System.Windows.Point(0, 14), new System.Windows.Point(7, 0), new System.Windows.Point(14, 14) });
                    else
                        return new System.Windows.Media.PointCollection(new[] { new System.Windows.Point(0, 0), new System.Windows.Point(7, 14), new System.Windows.Point(14, 0) });
                }
            }
        }

        public double ArrowTransformX => TargetLocation.X - (LayoutMode == PlanLayoutMode.Horizontal ? (Source != null && Target != null && Source.Location.X > Target.Location.X ? 0 : 14) : 7);
        public double ArrowTransformY => TargetLocation.Y - (LayoutMode == PlanLayoutMode.Horizontal ? 7 : (Source != null && Target != null && Source.Location.Y > Target.Location.Y ? 0 : 14));
"""
c = c.replace('public double ArrowAngle', arrow_props + '\n        public double ArrowAngle')

# Notify changes
c = c.replace('OnPropertyChanged(nameof(ArrowAngle));', 'OnPropertyChanged(nameof(ArrowAngle));
                    OnPropertyChanged(nameof(ArrowPoints));
                    OnPropertyChanged(nameof(ArrowTransformX));
                    OnPropertyChanged(nameof(ArrowTransformY));')

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(c)

print("C# arrow logic updated.")
