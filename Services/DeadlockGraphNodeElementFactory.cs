using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockGraphNodeElementFactory
    {
        public FrameworkElement CreateProcessNode(
            double width,
            double height,
            DeadlockProcess process,
            bool isVictim,
            string nodeId,
            int threadCount)
        {
            ArgumentNullException.ThrowIfNull(process);

            var card = new Border
            {
                Width = width,
                Height = height,
                Background = isVictim
                    ? new SolidColorBrush(Color.FromRgb(255, 240, 240))
                    : new SolidColorBrush(Color.FromRgb(240, 248, 255)),
                BorderBrush = isVictim
                    ? new SolidColorBrush(Color.FromRgb(220, 50, 50))
                    : new SolidColorBrush(Color.FromRgb(70, 130, 180)),
                BorderThickness = isVictim ? new Thickness(2.5) : new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Gray,
                    Direction = 315,
                    ShadowDepth = 2,
                    Opacity = 0.3,
                    BlurRadius = 4
                },
                Tag = nodeId
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var headerBar = new Border
            {
                Background = isVictim
                    ? new SolidColorBrush(Color.FromRgb(220, 50, 50))
                    : new SolidColorBrush(Color.FromRgb(70, 130, 180)),
                CornerRadius = new CornerRadius(4, 4, 0, 0)
            };
            headerBar.Child = new TextBlock
            {
                Text = BuildProcessHeader(process, isVictim, threadCount),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            };
            Grid.SetRow(headerBar, 0);
            mainGrid.Children.Add(headerBar);

            var contentStack = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
            contentStack.Children.Add(new TextBlock
            {
                Text = BuildDatabaseTransactionText(process),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkSlateGray,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            contentStack.Children.Add(new TextBlock
            {
                Text = $"User: {process.Loginname} ({process.Hostname})",
                FontSize = 9,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            string sql = BuildProcessSqlPreview(process);
            contentStack.Children.Add(new TextBlock
            {
                Text = sql,
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 120)),
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = new ToolTip
                {
                    Content = new TextBlock
                    {
                        Text = string.IsNullOrEmpty(process.Inputbuf) ? sql : process.Inputbuf,
                        MaxWidth = 400,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            });

            Grid.SetRow(contentStack, 1);
            mainGrid.Children.Add(contentStack);
            card.Child = mainGrid;
            return card;
        }

        public FrameworkElement CreateResourceNode(
            double width,
            double height,
            LockResource resource,
            string nodeId,
            int lockCount)
        {
            ArgumentNullException.ThrowIfNull(resource);

            var container = new Grid
            {
                Width = width,
                Height = height,
                Tag = nodeId,
                Background = Brushes.Transparent
            };

            container.Children.Add(new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection
                {
                    new Point(0, height / 2),
                    new Point(12, 0),
                    new Point(width - 12, 0),
                    new Point(width, height / 2),
                    new Point(width - 12, height),
                    new Point(12, height)
                },
                Fill = new SolidColorBrush(Color.FromRgb(255, 248, 225)),
                Stroke = new SolidColorBrush(Color.FromRgb(255, 179, 0)),
                StrokeThickness = 2,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Gray,
                    Direction = 315,
                    ShadowDepth = 1.5,
                    Opacity = 0.25,
                    BlurRadius = 3
                }
            });

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(14, 0, 14, 0),
                IsHitTestVisible = false
            };
            textStack.Children.Add(new TextBlock
            {
                Text = BuildLockTypeText(resource, lockCount),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(183, 28, 28)),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            string objectName = BuildResourceObjectText(resource);
            textStack.Children.Add(new TextBlock
            {
                Text = objectName,
                FontSize = 8.5,
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = width - 28,
                ToolTip = new ToolTip
                {
                    Content = resource.ObjectName
                        + (!string.IsNullOrEmpty(resource.IndexName)
                            ? $" ({resource.IndexName})"
                            : string.Empty)
                }
            });

            container.Children.Add(textStack);
            return container;
        }

        private static string BuildProcessHeader(
            DeadlockProcess process,
            bool isVictim,
            int threadCount)
        {
            string header = $"{(isVictim ? "💀 " : "👤 ")}SPID {process.Spid} [{(isVictim ? "Victim" : "Survivor")}]";
            return threadCount > 1
                ? $"{header} ({threadCount} 线程)"
                : header;
        }

        private static string BuildDatabaseTransactionText(DeadlockProcess process)
        {
            string text = $"DB: {(!string.IsNullOrEmpty(process.CurrentDbName) ? process.CurrentDbName : "Unknown")}";
            return !string.IsNullOrEmpty(process.TransactionName)
                ? $"{text} | Tx: {process.TransactionName}"
                : text;
        }

        private static string BuildProcessSqlPreview(DeadlockProcess process)
        {
            string sql = process.ExecutionStack.Count > 0
                && !string.IsNullOrEmpty(process.ExecutionStack[0].Statement)
                    ? process.ExecutionStack[0].Statement
                    : !string.IsNullOrEmpty(process.Inputbuf)
                        ? process.Inputbuf
                        : "No statement info";

            sql = Regex.Replace(sql, @"\s+", " ").Trim();
            return sql.Length > 85
                ? sql.Substring(0, 82) + "..."
                : sql;
        }

        private static string BuildLockTypeText(LockResource resource, int lockCount)
        {
            string text = resource.LockType.ToUpperInvariant();
            if (lockCount > 1)
            {
                return $"{text} ({lockCount} 锁)";
            }

            return !string.IsNullOrEmpty(resource.Dbid)
                ? $"{text} (DB: {resource.Dbid})"
                : text;
        }

        private static string BuildResourceObjectText(LockResource resource)
        {
            string objectName = !string.IsNullOrEmpty(resource.ObjectName)
                ? resource.ObjectName
                : "(Object)";

            if (objectName.Contains('.'))
            {
                string[] parts = objectName.Split('.');
                if (parts.Length > 1)
                {
                    objectName = string.Join(".", parts.Skip(Math.Max(0, parts.Length - 2)));
                }
            }

            return !string.IsNullOrEmpty(resource.IndexName)
                ? $"{objectName} ({resource.IndexName})"
                : objectName;
        }
    }
}
