using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace revit_mcp_plugin.Chat
{
    /// <summary>Colors, fonts and small control factories for the chat pane.</summary>
    internal static class ChatTheme
    {
        public static readonly FontFamily Font = new FontFamily("Segoe UI, Arial");
        public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas, Courier New");

        public static readonly Brush Background = Hex("#FAF9F5");
        public static readonly Brush Surface = Hex("#FFFFFF");
        public static readonly Brush Border = Hex("#E3DED3");
        public static readonly Brush Text = Hex("#2B2A26");
        public static readonly Brush Muted = Hex("#86817A");
        public static readonly Brush Accent = Hex("#D97757");
        public static readonly Brush AccentHover = Hex("#C4623F");
        public static readonly Brush UserBubble = Hex("#EFEBE1");
        public static readonly Brush ToolBg = Hex("#F3F0E8");
        public static readonly Brush CodeBg = Hex("#F1EEE6");
        public static readonly Brush Hover = Hex("#ECE8DE");
        public static readonly Brush Ok = Hex("#3C8D5A");
        public static readonly Brush Warn = Hex("#C98A1B");
        public static readonly Brush ErrorText = Hex("#A3261E");
        public static readonly Brush ErrorBg = Hex("#FBEAE8");

        public static SolidColorBrush Hex(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        /// <summary>A flat button with rounded corners and a hover color.</summary>
        public static Button FlatButton(object content, string tooltip, Brush background, Brush hover, Brush foreground,
            double radius = 8, Thickness? padding = null)
        {
            var border = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.Name = "bd";
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetBinding(System.Windows.Controls.Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(System.Windows.Controls.Border.PaddingProperty,
                new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, hover, "bd"));
            template.Triggers.Add(hoverTrigger);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            template.Triggers.Add(disabled);

            return new Button
            {
                Content = content,
                ToolTip = tooltip,
                Template = template,
                Background = background,
                Foreground = foreground,
                Padding = padding ?? new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand,
                FontFamily = Font,
                Focusable = false,
            };
        }

        /// <summary>The Claude mark: an orange rounded square with a white asterisk.</summary>
        public static FrameworkElement Logo(double size)
        {
            return new System.Windows.Controls.Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.28),
                Background = Accent,
                Child = new TextBlock
                {
                    Text = "✻",
                    Foreground = Brushes.White,
                    FontSize = size * 0.62,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -size * 0.06, 0, 0),
                },
            };
        }
    }
}
