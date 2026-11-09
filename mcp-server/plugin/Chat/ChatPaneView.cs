using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using revit_mcp_plugin.Core;

namespace revit_mcp_plugin.Chat
{
    /// <summary>
    /// The chat UI shown in the "Claude" dockable pane. Built in code (no XAML).
    /// </summary>
    public class ChatPaneView : UserControl
    {
        private static readonly (string Label, string Value)[] Models =
        {
            ("מודל ברירת מחדל", ""), ("Opus", "opus"), ("Sonnet", "sonnet"), ("Haiku", "haiku"),
        };

        private static readonly string[] Suggestions =
        {
            "מה יש במודל הפתוח?",
            "סכם את מערכת החשמל: לוחות, מעגלים ועומסים",
            "הכן כתב כמויות לחשמל לאקסל",
            "צור תוכנית חשמל לקומה 1, תייג הכל וייצא ל-PDF",
        };

        private readonly ChatSettings _settings;
        private readonly ClaudeSession _session;

        private readonly ScrollViewer _scroll;
        private readonly StackPanel _messages;
        private readonly FrameworkElement _thinking;
        private readonly TextBlock _thinkingText;
        private readonly TextBox _input;
        private readonly TextBlock _placeholder;
        private readonly Button _sendButton;
        private readonly TextBlock _sendGlyph;
        private readonly Ellipse _revitDot;
        private readonly TextBlock _revitStatus;
        private readonly TextBlock _footerStatus;
        private readonly ComboBox _modelBox;
        private readonly DispatcherTimer _timer;

        private RichTextBox _currentAnswer;
        private StringBuilder _currentText;
        private bool _renderPending;
        private int _dots;
        private readonly Dictionary<string, ToolChip> _tools = new Dictionary<string, ToolChip>();
        private bool _warnedMcp;

        public ChatPaneView()
        {
            _settings = ChatSettings.Load();
            _session = new ClaudeSession(_settings);

            Background = ChatTheme.Background;
            FontFamily = ChatTheme.Font;
            Foreground = ChatTheme.Text;
            MinWidth = 260;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            // ---------- header
            var header = new Border
            {
                Background = ChatTheme.Surface,
                BorderBrush = ChatTheme.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 10, 8, 10),
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var logo = ChatTheme.Logo(28);
            headerGrid.Children.Add(logo);

            var titleStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock { Text = "Claude", FontSize = 15, FontWeight = FontWeights.SemiBold });
            var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
            _revitDot = new Ellipse { Width = 7, Height = 7, Fill = ChatTheme.Muted, Margin = new Thickness(0, 1, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            _revitStatus = new TextBlock { FontSize = 11.5, Foreground = ChatTheme.Muted, Cursor = Cursors.Hand };
            _revitStatus.MouseLeftButtonUp += (s, e) => StartRevitServer(showResult: true);
            statusRow.Children.Add(_revitDot);
            statusRow.Children.Add(_revitStatus);
            titleStack.Children.Add(statusRow);
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            var newChat = ChatTheme.FlatButton(
                new TextBlock { Text = "＋  שיחה חדשה", FontSize = 12 },
                "התחל שיחה חדשה (Claude שוכח את השיחה הנוכחית)",
                ChatTheme.Surface, ChatTheme.Hover, ChatTheme.Text, 8, new Thickness(10, 5, 10, 5));
            newChat.VerticalAlignment = VerticalAlignment.Center;
            newChat.Click += (s, e) => NewConversation();
            Grid.SetColumn(newChat, 2);
            headerGrid.Children.Add(newChat);
            header.Child = headerGrid;
            root.Children.Add(header);

            // ---------- messages
            _messages = new StackPanel { Margin = new Thickness(14, 12, 14, 4) };
            _thinkingText = new TextBlock { Text = "Claude חושב", Foreground = ChatTheme.Muted, FontSize = 12.5, FontStyle = FontStyles.Italic };
            var thinkingRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 2, 14, 12) };
            var spark = new TextBlock { Text = "✻", Foreground = ChatTheme.Accent, FontSize = 15, Margin = new Thickness(0, -2, 6, 0) };
            thinkingRow.Children.Add(spark);
            thinkingRow.Children.Add(_thinkingText);
            thinkingRow.Visibility = Visibility.Collapsed;
            _thinking = thinkingRow;

            var scrollContent = new StackPanel();
            scrollContent.Children.Add(_messages);
            scrollContent.Children.Add(_thinking);
            _scroll = new ScrollViewer
            {
                Content = scrollContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            Grid.SetRow(_scroll, 1);
            root.Children.Add(_scroll);

            // ---------- input
            var inputOuter = new Border { Padding = new Thickness(12, 4, 12, 6) };
            var inputBox = new Border
            {
                Background = ChatTheme.Surface,
                BorderBrush = ChatTheme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 8, 8, 8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.08 },
            };
            var inputGrid = new Grid();
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _input = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 13.5,
                MinHeight = 40,
                MaxHeight = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Thickness(0, 2, 0, 0),
            };
            _input.PreviewKeyDown += OnInputKeyDown;
            _input.TextChanged += (s, e) =>
            {
                _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_input.Text.Length > 0)
                    _input.FlowDirection = MarkdownRenderer.IsRtl(_input.Text) || !_input.Text.Any(char.IsLetter)
                        ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            };
            _placeholder = new TextBlock
            {
                Text = "כתוב ל-Claude…  (Enter לשליחה, Shift+Enter לשורה חדשה)",
                Foreground = ChatTheme.Muted,
                FontSize = 13,
                IsHitTestVisible = false,
                Margin = new Thickness(2, 3, 2, 0),
                FlowDirection = FlowDirection.RightToLeft,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            inputGrid.Children.Add(_input);
            inputGrid.Children.Add(_placeholder);

            _sendGlyph = new TextBlock { Text = "↑", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, -2, 0, 0) };
            _sendButton = ChatTheme.FlatButton(_sendGlyph, "שלח", ChatTheme.Accent, ChatTheme.AccentHover, Brushes.White, 10, new Thickness(0));
            _sendButton.Width = 34;
            _sendButton.Height = 34;
            _sendButton.VerticalAlignment = VerticalAlignment.Bottom;
            _sendButton.Margin = new Thickness(8, 0, 0, 0);
            _sendButton.Click += (s, e) => { if (_session.IsBusy) StopAnswer(); else SendText(_input.Text); };
            Grid.SetColumn(_sendButton, 1);
            inputGrid.Children.Add(_sendButton);
            inputBox.Child = inputGrid;
            inputOuter.Child = inputBox;
            Grid.SetRow(inputOuter, 2);
            root.Children.Add(inputOuter);

            // ---------- footer
            var footer = new Grid { Margin = new Thickness(14, 0, 10, 8) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _modelBox = new ComboBox { FontSize = 11.5, MinWidth = 120, Padding = new Thickness(6, 2, 6, 2), Focusable = false };
            foreach (var m in Models) _modelBox.Items.Add(new ComboBoxItem { Content = m.Label, Tag = m.Value });
            int sel = Array.FindIndex(Models, m => string.Equals(m.Value, _settings.Model ?? "", StringComparison.OrdinalIgnoreCase));
            _modelBox.SelectedIndex = sel < 0 ? 0 : sel;
            _modelBox.SelectionChanged += (s, e) => ChangeModel();
            footer.Children.Add(_modelBox);

            _footerStatus = new TextBlock
            {
                FontSize = 11, Foreground = ChatTheme.Muted, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(_footerStatus, 1);
            footer.Children.Add(_footerStatus);

            var folder = ChatTheme.FlatButton(new TextBlock { Text = "📁", FontSize = 13 },
                "פתח את תיקיית העבודה (CLAUDE.md, ייצואים, כתבי כמויות)",
                Brushes.Transparent, ChatTheme.Hover, ChatTheme.Text, 6, new Thickness(6, 2, 6, 2));
            folder.Click += (s, e) => OpenFolder();
            Grid.SetColumn(folder, 2);
            footer.Children.Add(folder);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            // ---------- session events (background thread -> UI thread)
            _session.TextDelta += t => Ui(() => AppendAnswer(t));
            _session.AssistantText += t => Ui(() => AppendAnswer(t));
            _session.ToolStarted += (id, name, input) => Ui(() => AddTool(id, name, input));
            _session.ToolFinished += (id, err, result) => Ui(() => FinishTool(id, err, result));
            _session.TurnCompleted += (err, ms, text) => Ui(() => OnTurnCompleted(err, ms, text));
            _session.Initialized += (model, servers) => Ui(() => OnInitialized(model, servers));
            _session.Error += msg => Ui(() => AddError(msg));
            _session.BusyChanged += busy => Ui(() => OnBusyChanged(busy));

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _timer.Tick += (s, e) => OnTick();
            _timer.Start();

            ShowWelcome();
            UpdateRevitStatus();
            _footerStatus.Text = "רק כלי Revit וקריאת קבצים מותרים";
        }

        // ================================================================= public

        public void FocusInput()
        {
            Dispatcher.BeginInvoke(new Action(() => { _input.Focus(); Keyboard.Focus(_input); }), DispatcherPriority.Input);
        }

        public void Shutdown()
        {
            _timer.Stop();
            _session.Dispose();
        }

        // ================================================================= actions

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                if (!_session.IsBusy) SendText(_input.Text);
            }
            else if (e.Key == Key.Escape && _session.IsBusy)
            {
                e.Handled = true;
                StopAnswer();
            }
        }

        private void SendText(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || _session.IsBusy) return;

            RemoveWelcome();
            AddUser(text);
            _input.Clear();
            EndAnswer();

            if (!SocketService.Instance.IsRunning) StartRevitServer(showResult: false);

            try
            {
                _session.Send(text);
            }
            catch (Exception ex)
            {
                AddError(ex.Message);
            }
            ScrollToEnd(force: true);
        }

        private void StopAnswer()
        {
            _session.Stop();
            EndAnswer();
            foreach (var chip in _tools.Values.Where(c => c.Pending)) chip.Finish(true, "Stopped");
            AddNote("העצירה בוצעה. אפשר להמשיך לכתוב, Claude זוכר את השיחה.");
        }

        private void NewConversation()
        {
            _session.Reset();
            _messages.Children.Clear();
            _tools.Clear();
            EndAnswer();
            _warnedMcp = false;
            ShowWelcome();
            FocusInput();
        }

        private void ChangeModel()
        {
            var item = _modelBox.SelectedItem as ComboBoxItem;
            string value = item?.Tag as string ?? "";
            if (string.Equals(value, _settings.Model ?? "", StringComparison.OrdinalIgnoreCase)) return;
            _settings.Model = value;
            _settings.Save();
            bool wasBusy = _session.IsBusy;
            // Restarts on the next message with --resume, so the conversation is kept.
            _session.Stop();
            if (wasBusy) AddNote("התשובה נעצרה כדי להחליף מודל.");
            AddNote("המודל יוחלף ל-" + item?.Content + " מההודעה הבאה.");
        }

        private void StartRevitServer(bool showResult)
        {
            bool queued = ChatPane.RequestStartServer(ok => Ui(() =>
            {
                UpdateRevitStatus();
                if (showResult && !ok) AddError("לא הצלחתי להפעיל את שרת ה-MCP של Revit (פורט 8080). אולי מופע Revit אחר כבר משתמש בו?");
            }));
            if (!queued && showResult)
                AddNote("לחץ על Add-Ins ‹ Claude Chat כדי להפעיל את החיבור ל-Revit.");
        }

        private void OpenFolder()
        {
            try { Process.Start(new ProcessStartInfo(_settings.ResolvedWorkingFolder) { UseShellExecute = true }); }
            catch (Exception ex) { AddError(ex.Message); }
        }

        // ================================================================= session events

        private void OnBusyChanged(bool busy)
        {
            _sendGlyph.Text = busy ? "■" : "↑";
            _sendGlyph.FontSize = busy ? 13 : 17;
            _sendButton.ToolTip = busy ? "עצור (Esc)" : "שלח";
            _thinking.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _modelBox.IsEnabled = true;
            if (busy) ScrollToEnd(force: false);
        }

        private void OnInitialized(string model, IDictionary<string, string> servers)
        {
            string revit = servers.TryGetValue("mcp-server-for-revit", out var st) ? st : null;
            _footerStatus.Text = (string.IsNullOrEmpty(model) ? "" : model + " · ") +
                                 (revit == "connected" ? "כלי Revit זמינים" : revit == null ? "כלי Revit לא מוגדרים" : "כלי Revit: " + revit);
            _footerStatus.ToolTip = string.Join("\n", servers.Select(kv => kv.Key + ": " + kv.Value));
            if (revit != "connected" && !_warnedMcp)
            {
                _warnedMcp = true;
                AddError(revit == null
                    ? "שרת ה-MCP של Revit לא רשום ב-Claude Code. הרץ ב-PowerShell את install-electrical.ps1 (או: claude mcp add -s user mcp-server-for-revit -- node <path>\\server\\build\\index.js)."
                    : "שרת ה-MCP של Revit לא התחבר (" + revit + "). בדוק ב-PowerShell: claude mcp list");
            }
        }

        private void OnTurnCompleted(bool isError, long ms, string text)
        {
            EndAnswer();
            foreach (var chip in _tools.Values.Where(c => c.Pending)) chip.Finish(false, "");
            if (isError)
                AddError(string.IsNullOrWhiteSpace(text) ? "Claude החזיר שגיאה." : text);
            else if (ms > 0)
                AddMeta(FormatDuration(ms));
            ScrollToEnd(force: false);
        }

        private void OnTick()
        {
            _dots = (_dots + 1) % 4;
            _thinkingText.Text = "Claude עובד" + new string('.', _dots);
            UpdateRevitStatus();
        }

        private void UpdateRevitStatus()
        {
            bool on = SocketService.Instance.IsRunning;
            _revitDot.Fill = on ? ChatTheme.Ok : ChatTheme.Warn;
            _revitStatus.Text = on ? "מחובר ל-Revit" : "Revit לא מחובר · לחץ לחיבור";
            _revitStatus.ToolTip = on ? "שרת ה-MCP של Revit פועל בפורט 8080" : "לחץ כדי להפעיל את שרת ה-MCP של Revit";
        }

        // ================================================================= message building

        private void ShowWelcome()
        {
            var panel = new StackPanel { Tag = "welcome", Margin = new Thickness(0, 18, 0, 8) };
            var logo = ChatTheme.Logo(44);
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(logo);
            panel.Children.Add(new TextBlock
            {
                Text = "שלום, במה אפשר לעזור?",
                FontSize = 18, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 4),
                FlowDirection = FlowDirection.RightToLeft,
            });
            panel.Children.Add(new TextBlock
            {
                Text = "אני מחובר למודל הפתוח ב-Revit. אפשר לבקש ממני לבדוק, לשרטט, לתייג, לייצא ולהכין כתבי כמויות.",
                FontSize = 12.5, Foreground = ChatTheme.Muted, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0, 8, 14), FlowDirection = FlowDirection.RightToLeft,
            });
            foreach (string s in Suggestions)
            {
                var chip = ChatTheme.FlatButton(
                    new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = ChatTheme.Text },
                    null, ChatTheme.Surface, ChatTheme.Hover, ChatTheme.Text, 10, new Thickness(12, 8, 12, 8));
                chip.HorizontalAlignment = HorizontalAlignment.Stretch;
                chip.HorizontalContentAlignment = HorizontalAlignment.Right;
                chip.FlowDirection = FlowDirection.RightToLeft;
                chip.BorderBrush = ChatTheme.Border;
                chip.Margin = new Thickness(0, 0, 0, 6);
                var wrapped = new Border { BorderBrush = ChatTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Child = chip, Margin = new Thickness(0, 0, 0, 6) };
                chip.Margin = new Thickness(0);
                string text = s;
                chip.Click += (o, e) => SendText(text);
                panel.Children.Add(wrapped);
            }
            _messages.Children.Add(panel);
        }

        private void RemoveWelcome()
        {
            var w = _messages.Children.OfType<FrameworkElement>().FirstOrDefault(c => (c.Tag as string) == "welcome");
            if (w != null) _messages.Children.Remove(w);
        }

        private void AddUser(string text)
        {
            var box = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 13.5,
                Foreground = ChatTheme.Text,
                FlowDirection = MarkdownRenderer.IsRtl(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            };
            var bubble = new Border
            {
                Background = ChatTheme.UserBubble,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(36, 8, 0, 10),
                Child = box,
            };
            _messages.Children.Add(bubble);
        }

        private void AppendAnswer(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_currentAnswer == null)
            {
                _currentText = new StringBuilder();
                _currentAnswer = new RichTextBox
                {
                    IsReadOnly = true,
                    IsDocumentEnabled = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(0),
                    Margin = new Thickness(-2, 2, 0, 4),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                };
                _messages.Children.Add(_currentAnswer);
            }
            _currentText.Append(text);
            if (!_renderPending)
            {
                _renderPending = true;
                var target = _currentAnswer;
                var source = _currentText;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _renderPending = false;
                    bool atBottom = IsAtBottom();
                    target.Document = MarkdownRenderer.Render(source.ToString());
                    if (atBottom) ScrollToEnd(force: true);
                }), DispatcherPriority.Background);
            }
        }

        /// <summary>The next text starts a new answer block (after a tool call or at the end of a turn).</summary>
        private void EndAnswer()
        {
            if (_currentAnswer != null && _currentText != null)
                _currentAnswer.Document = MarkdownRenderer.Render(_currentText.ToString());
            _currentAnswer = null;
            _currentText = null;
        }

        private void AddTool(string id, string name, string input)
        {
            EndAnswer();
            var chip = new ToolChip(name, input);
            if (!string.IsNullOrEmpty(id)) _tools[id] = chip;
            _messages.Children.Add(chip.Element);
            ScrollToEnd(force: false);
        }

        private void FinishTool(string id, bool isError, string result)
        {
            if (id != null && _tools.TryGetValue(id, out var chip)) chip.Finish(isError, result);
        }

        private void AddError(string text)
        {
            EndAnswer();
            _messages.Children.Add(new Border
            {
                Background = ChatTheme.ErrorBg,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 8),
                Child = new TextBox
                {
                    Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent, Foreground = ChatTheme.ErrorText, FontSize = 12.5,
                    FlowDirection = MarkdownRenderer.IsRtl(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                },
            });
            ScrollToEnd(force: true);
        }

        private void AddNote(string text)
        {
            _messages.Children.Add(new TextBlock
            {
                Text = text, Foreground = ChatTheme.Muted, FontSize = 12, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8),
                FlowDirection = MarkdownRenderer.IsRtl(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            });
            ScrollToEnd(force: true);
        }

        private void AddMeta(string text)
        {
            _messages.Children.Add(new TextBlock
            {
                Text = text, Foreground = ChatTheme.Muted, FontSize = 10.5, Margin = new Thickness(0, 0, 0, 10), Opacity = 0.8,
            });
        }

        private static string FormatDuration(long ms)
        {
            double s = ms / 1000.0;
            return s < 60 ? s.ToString("0.0") + " שנ׳" : ((int)(s / 60)) + " דק׳ " + ((int)(s % 60)) + " שנ׳";
        }

        private bool IsAtBottom() => _scroll.VerticalOffset >= _scroll.ScrollableHeight - 60;

        private void ScrollToEnd(bool force)
        {
            if (!force && !IsAtBottom()) return;
            Dispatcher.BeginInvoke(new Action(() => _scroll.ScrollToEnd()), DispatcherPriority.Background);
        }

        private void Ui(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.BeginInvoke(a);
        }

        // ================================================================= tool chip

        private class ToolChip
        {
            public readonly FrameworkElement Element;
            public bool Pending { get; private set; } = true;
            private readonly TextBlock _icon;
            private readonly TextBox _details;
            private readonly string _input;

            public ToolChip(string name, string input)
            {
                _input = input ?? "";
                var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
                var head = new Border
                {
                    Background = ChatTheme.ToolBg,
                    BorderBrush = ChatTheme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 3, 10, 3),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ToolTip = "לחץ להצגת הפרטים",
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                _icon = new TextBlock { Text = "⋯", Width = 16, Foreground = ChatTheme.Accent, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(_icon);
                row.Children.Add(new TextBlock { Text = name, FontFamily = ChatTheme.Mono, FontSize = 12, Foreground = ChatTheme.Text, VerticalAlignment = VerticalAlignment.Center });
                head.Child = row;
                _details = new TextBox
                {
                    Text = Truncate("קלט:\n" + _input, 3000),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = ChatTheme.Mono,
                    FontSize = 11,
                    Background = ChatTheme.CodeBg,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 4, 0, 0),
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Visibility = Visibility.Collapsed,
                    FlowDirection = FlowDirection.LeftToRight,
                };
                head.MouseLeftButtonUp += (s, e) =>
                    _details.Visibility = _details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                panel.Children.Add(head);
                panel.Children.Add(_details);
                Element = panel;
            }

            public void Finish(bool isError, string result)
            {
                if (!Pending) return;
                Pending = false;
                _icon.Text = isError ? "✕" : "✓";
                _icon.Foreground = isError ? ChatTheme.ErrorText : ChatTheme.Ok;
                if (!string.IsNullOrEmpty(result))
                    _details.Text = Truncate("קלט:\n" + _input + "\n\nתוצאה:\n" + result, 6000);
            }

            private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "\n…";
        }
    }
}
