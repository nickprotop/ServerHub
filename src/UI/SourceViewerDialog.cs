// Copyright (c) Nikolaos Protopapas. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using Spectre.Console;

namespace ServerHub.UI;

/// <summary>
/// Shared dialog for viewing source code with syntax highlighting.
/// Supports both local files and remote URLs.
/// </summary>
public static class SourceViewerDialog
{
    /// <summary>
    /// Shows a modal window displaying the contents of a local file.
    /// </summary>
    public static void ShowFile(
        ConsoleWindowSystem windowSystem,
        string filePath,
        Window? parentWindow = null)
    {
        if (!File.Exists(filePath))
            return;

        var fileName = Path.GetFileName(filePath);
        var content = File.ReadAllText(filePath);

        Show(windowSystem, fileName, $"Full path: {filePath}", content, parentWindow);
    }

    /// <summary>
    /// Shows a modal window displaying content fetched from a URL.
    /// </summary>
    public static void ShowFromUrl(
        ConsoleWindowSystem windowSystem,
        string title,
        string url,
        Window? parentWindow = null)
    {
        int modalWidth = Math.Min((int)(Console.WindowWidth * 0.9), 150);
        int modalHeight = Math.Min((int)(Console.WindowHeight * 0.9), 45);

        var builder = new WindowBuilder(windowSystem)
            .WithTitle(title)
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .Maximizable(true)
            .WithColors(Color.Grey15, Color.Grey93);

        if (parentWindow != null)
            builder = builder.WithParent(parentWindow);

        var modal = builder.Build();

        // Header
        var header = Controls
            .Markup()
            .AddLine($"[grey70]Source: {Markup.Escape(url)}[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        modal.AddControl(header);

        modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).Build());

        // Loading indicator
        var loadingControl = Controls
            .Markup()
            .WithName("loading")
            .AddLine("")
            .AddLine("[cyan1]⟳ Fetching source code...[/]")
            .AddLine("")
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
            .Build();
        modal.AddControl(loadingControl);

        // Scrollable panel (initially hidden)
        var scrollPanel = Controls
            .ScrollablePanel()
            .WithName("source_scroll")
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithScrollbarPosition(ScrollbarPosition.Right)
            .WithMouseWheel(true)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithBackgroundColor(Color.Grey15)
            .Build();
        scrollPanel.Visible = false;
        modal.AddControl(scrollPanel);

        // Footer
        modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).StickyBottom().Build());
        modal.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]↑↓: Scroll | Mouse Wheel: Scroll | Esc/Enter: Close[/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
                .StickyBottom()
                .Build()
        );

        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape || e.KeyInfo.Key == ConsoleKey.Enter)
            {
                windowSystem.CloseWindow(modal);
                e.Handled = true;
            }
        };

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);

        // Fetch asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "ServerHub-Marketplace/1.0");
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                var sourceCode = await httpClient.GetStringAsync(url);

                loadingControl.Visible = false;

                var highlightedLines = ApplySyntaxHighlighting(sourceCode.Split('\n'));
                var contentBuilder = Controls.Markup().WithBackgroundColor(Color.Grey19);
                foreach (var line in highlightedLines)
                {
                    contentBuilder.AddLine(line);
                }
                scrollPanel.AddControl(contentBuilder.Build());

                scrollPanel.Visible = true;
                scrollPanel.SetFocus(true, FocusReason.Programmatic);
            }
            catch (Exception ex)
            {
                loadingControl.SetContent(new List<string>
                {
                    "",
                    "[red]Failed to fetch source code:[/]",
                    $"[red]{Markup.Escape(ex.Message)}[/]",
                    ""
                });
            }
        });
    }

    /// <summary>
    /// Shows a modal window displaying the provided content.
    /// </summary>
    public static void Show(
        ConsoleWindowSystem windowSystem,
        string title,
        string subtitle,
        string content,
        Window? parentWindow = null)
    {
        int modalWidth = Math.Min((int)(Console.WindowWidth * 0.9), 150);
        int modalHeight = Math.Min((int)(Console.WindowHeight * 0.9), 45);

        var builder = new WindowBuilder(windowSystem)
            .WithTitle(title)
            .WithSize(modalWidth, modalHeight)
            .Centered()
            .AsModal()
            .WithBorderStyle(BorderStyle.Single)
            .WithBorderColor(Color.Grey35)
            .Resizable(true)
            .Movable(true)
            .Minimizable(false)
            .Maximizable(true)
            .WithColors(Color.Grey15, Color.Grey93);

        if (parentWindow != null)
            builder = builder.WithParent(parentWindow);

        var modal = builder.Build();

        // Header
        var header = Controls
            .Markup()
            .AddLine($"[grey70]{Markup.Escape(subtitle)}[/]")
            .WithMargin(1, 1, 1, 0)
            .Build();
        modal.AddControl(header);

        modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).Build());

        // Content with syntax highlighting
        var highlightedLines = ApplySyntaxHighlighting(content.Split('\n'));
        var contentBuilder = Controls.Markup().WithBackgroundColor(Color.Grey19);
        foreach (var line in highlightedLines)
        {
            contentBuilder.AddLine(line);
        }

        var scrollPanel = Controls
            .ScrollablePanel()
            .WithVerticalScroll(ScrollMode.Scroll)
            .WithScrollbar(true)
            .WithScrollbarPosition(ScrollbarPosition.Right)
            .WithMouseWheel(true)
            .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Stretch)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithBackgroundColor(Color.Grey15)
            .AddControl(contentBuilder.Build())
            .Build();

        modal.AddControl(scrollPanel);

        // Footer
        modal.AddControl(Controls.RuleBuilder().WithColor(Color.Grey23).StickyBottom().Build());
        modal.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]↑↓: Scroll | Mouse Wheel: Scroll | Esc/Enter: Close[/]")
                .WithAlignment(SharpConsoleUI.Layout.HorizontalAlignment.Center)
                .StickyBottom()
                .Build()
        );

        modal.KeyPressed += (s, e) =>
        {
            if (e.KeyInfo.Key == ConsoleKey.Escape || e.KeyInfo.Key == ConsoleKey.Enter)
            {
                windowSystem.CloseWindow(modal);
                e.Handled = true;
            }
        };

        windowSystem.AddWindow(modal);
        windowSystem.SetActiveWindow(modal);
        scrollPanel.SetFocus(true, FocusReason.Programmatic);
    }

    /// <summary>
    /// Applies simple syntax highlighting with line numbers.
    /// </summary>
    public static List<string> ApplySyntaxHighlighting(IEnumerable<string> lines, int startLineNumber = 1)
    {
        var result = new List<string>();
        int lineNum = startLineNumber;

        foreach (var line in lines)
        {
            var escapedLine = Markup.Escape(line.TrimEnd('\r'));
            result.Add($"[grey50]{lineNum,4}[/] {escapedLine}");
            lineNum++;
        }

        return result;
    }
}
