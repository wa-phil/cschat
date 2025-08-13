using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;

public class User
{
    private static string? lastInput = null;
    
    // Renders a menu at the current cursor position, allows arrow key navigation, and returns the selected string or null if cancelled
    public static string? RenderMenu(string header, List<string> choices, int selected = 0) // Allow nullable return type to handle null cases
    {
        // Store original choices for scrolling
        var originalChoices = new List<string>(choices);
        int actualMaxVisibleItems = Program.config.MaxMenuItems, originalSelected = selected;
    
        // always position the menu at the top, and print the header
        Console.Clear();
        Console.SetCursorPosition(0, 0);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(header);
        Console.ResetColor();

        // Calculate scrolling parameters
        int scrollOffset = 0, visibleItems = Math.Min(originalChoices.Count, actualMaxVisibleItems);
        bool hasMoreAbove = false, hasMoreBelow = false;
        
        // Reserve space for menu lines, indicators, and input
        int indicatorLines = 2; // up to 2 lines for "more above/below" indicators
        int inputLines = 1; // input line
        int menuLines = visibleItems + indicatorLines;
        
        // Print placeholder lines for the menu area
        for (int i = 0; i < menuLines + inputLines; i++)
        {
            Console.WriteLine();
        }
        
        int menuStartRow = Console.CursorTop - (menuLines + inputLines);
        int inputTop = Console.CursorTop - inputLines;

        string filter = "";
        List<string> filteredChoices = new List<string>(originalChoices);
        int filteredSelected = originalSelected;

        void DrawMenu() => Log.Method(ctx =>
        {
            ctx.OnlyEmitOnFailure();
            ctx.Append(Log.Data.ConsoleHeight, Console.BufferHeight);
            ctx.Append(Log.Data.ConsoleWidth, Console.WindowWidth);
            
            // Calculate which items to show and update scroll indicators
            visibleItems = Math.Min(filteredChoices.Count, actualMaxVisibleItems);
            
            // Adjust scroll offset to keep selected item visible
            if (filteredSelected < scrollOffset)
            {
                scrollOffset = filteredSelected;
            }
            else if (filteredSelected >= scrollOffset + visibleItems)
            {
                scrollOffset = filteredSelected - visibleItems + 1;
            }
            
            // Ensure scroll offset is within bounds
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, Math.Max(0, filteredChoices.Count - visibleItems)));            
            hasMoreAbove = scrollOffset > 0;
            hasMoreBelow = scrollOffset + visibleItems < filteredChoices.Count;
            
            // Ensure we don't exceed console buffer bounds
            int maxRow = Console.BufferHeight - 1;
            int currentRow = menuStartRow;

            // Draw "more above" indicator if needed
            if (hasMoreAbove && currentRow < maxRow)
            {
                Console.SetCursorPosition(0, currentRow);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var countAbove = Math.Min(scrollOffset, filteredChoices.Count);
                Console.Write($"^^^ {countAbove} items above ^^^".PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
                currentRow++;
            }

            // Draw visible menu items
            for (int i = 0; i < visibleItems && currentRow < maxRow; i++)
            {
                int choiceIndex = scrollOffset + i;
                if (choiceIndex >= filteredChoices.Count) break;

                ctx.Append(Log.Data.MenuTop, currentRow);
                Console.SetCursorPosition(0, currentRow);
                string line;
                
                bool isSelected = choiceIndex == filteredSelected;
                if (isSelected)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                    if (filteredChoices.Count <= 9)
                        line = $"> [{choiceIndex + 1}] {filteredChoices[choiceIndex]} ";
                    else
                        line = $"> {filteredChoices[choiceIndex]} ";
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.BackgroundColor = ConsoleColor.Black;
                    if (filteredChoices.Count <= 9)
                        line = $"  [{choiceIndex + 1}] {filteredChoices[choiceIndex]} ";
                    else
                        line = $"  {filteredChoices[choiceIndex]} ";
                }
                Console.Write(line.PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
                currentRow++;
            }

            // Draw "more below" indicator if needed
            if (hasMoreBelow && currentRow < maxRow)
            {
                Console.SetCursorPosition(0, currentRow);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var countBelow = Math.Max(0, filteredChoices.Count - (scrollOffset + visibleItems));
                Console.Write($"vvv {countBelow} items below vvv".PadRight(Console.WindowWidth - 1));
                Console.ResetColor();
                currentRow++;
            }

            // Clear any leftover lines in the menu area
            while (currentRow < inputTop && currentRow < maxRow)
            {
                Console.SetCursorPosition(0, currentRow);
                Console.Write(new string(' ', Console.WindowWidth - 1));
                currentRow++;
            }

            // Draw input header only if it fits in the buffer
            if (inputTop < maxRow)
            {
                ctx.Append(Log.Data.InputTop, inputTop);
                Console.SetCursorPosition(0, inputTop);
                var inputHeader = "[filter]> ";
                Console.Write($"{inputHeader}{filter}".PadRight(Console.WindowWidth - 1));
                if (inputHeader.Length + filter.Length < Console.WindowWidth && inputTop < maxRow)
                {
                    Console.SetCursorPosition(inputHeader.Length + filter.Length, inputTop);
                }
            }
            ctx.Succeeded();
        });

        DrawMenu();
        ConsoleKeyInfo key;
        while (true)
        {
            key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (filteredSelected > 0) filteredSelected--;
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.PageUp || (key.Key == ConsoleKey.UpArrow && key.Modifiers.HasFlag(ConsoleModifiers.Shift)))
            {
                if (filteredSelected > 0)
                {
                    filteredSelected -= Math.Min(actualMaxVisibleItems, filteredSelected);
                }
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.Home)
            {
                filteredSelected = 0;
                scrollOffset = 0; // Reset scroll when going to home
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.End)
            {
                filteredSelected = Math.Max(0, filteredChoices.Count - 1);
                scrollOffset = Math.Max(0, filteredChoices.Count - actualMaxVisibleItems); // Reset scroll when going to end
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                if (filteredSelected < filteredChoices.Count - 1) filteredSelected++;
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.PageDown || (key.Key == ConsoleKey.DownArrow && key.Modifiers.HasFlag(ConsoleModifiers.Shift)))
            {
                if (filteredSelected < filteredChoices.Count - 1)
                {
                    filteredSelected += Math.Min(actualMaxVisibleItems, filteredChoices.Count - 1 - filteredSelected);
                }
                DrawMenu();
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                // Safe cursor positioning for exit
                int exitRow = Math.Min(inputTop + 1, Console.BufferHeight - 1);
                Console.SetCursorPosition(0, exitRow);
                if (filteredChoices.Count > 0)
                    return filteredChoices[filteredSelected];
                else
                    return null;
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                // Safe cursor positioning for exit
                int exitRow = Math.Min(inputTop + 1, Console.BufferHeight - 1);
                Console.SetCursorPosition(0, exitRow);
                Console.WriteLine("Selection cancelled.");
                return null;
            }
            else if (filteredChoices.Count <= 10 && key.KeyChar >= '1' && key.KeyChar <= (char)('0' + filteredChoices.Count))
            {
                int idx = key.KeyChar - '1';
                // Safe cursor positioning for exit
                int exitRow = Math.Min(inputTop + 1, Console.BufferHeight - 1);
                Console.SetCursorPosition(0, exitRow);
                return filteredChoices[idx];
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (filter.Length > 0)
                {
                    filter = filter.Substring(0, filter.Length - 1);
                    filteredChoices = originalChoices.Where(c => c.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                    if (filteredSelected >= filteredChoices.Count) filteredSelected = Math.Max(0, filteredChoices.Count - 1);
                    scrollOffset = 0; // Reset scroll when filtering
                    DrawMenu();
                }
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                string newFilter = filter + key.KeyChar;
                var newFiltered = originalChoices.Where(c => c.IndexOf(newFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (newFiltered.Count > 0)
                {
                    filter = newFilter;
                    filteredChoices = newFiltered;
                    filteredSelected = 0;
                    scrollOffset = 0; // Reset scroll when filtering
                    DrawMenu();
                }
                // else: ignore input that would result in no options
            }
        }
    }

    public static async Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager) // Allow nullable return type
    {
        var buffer = new List<char>();
        var lines = new List<string>();
        int cursor = 0;
        ConsoleKeyInfo key;

        while (true)
        {
            key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                // Soft new line
                lines.Add(new string(buffer.ToArray()));
                buffer.Clear();
                cursor = 0;
                Console.Write("\n> ");
                continue;
            }
            if (key.Key == ConsoleKey.Enter)
            {
                // erase the contents of the current line, and do not advance to the next line
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write(new string(' ', Console.WindowWidth - 1));
                Console.SetCursorPosition(0, Console.CursorTop);
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buffer.RemoveAt(cursor - 1);
                    cursor--;
                    Console.Write("\b \b");
                }
                continue;
            }
            if (key.Key == ConsoleKey.Escape)
            {
                var result = await commandManager.Action();
                if (result == Command.Result.Failed)
                {
                    Console.WriteLine("Command failed.");
                }
                Console.Write("[press ESC to open menu]\n> ");
                buffer.Clear();
                cursor = 0;
                continue;
            }
            if (key.Key == ConsoleKey.UpArrow)
            {
                if (lastInput != null)
                {
                    // Clear current buffer display
                    for (int i = 0; i < buffer.Count; i++)
                    {
                        Console.Write("\b \b");
                    }
                    
                    // Set buffer to last input
                    buffer.Clear();
                    buffer.AddRange(lastInput.ToCharArray());
                    cursor = buffer.Count;
                    
                    // Display the recalled input
                    Console.Write(lastInput);
                }
                continue;
            }
            if (key.KeyChar != '\0')
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Console.Write(key.KeyChar);
            }
        }
        lines.Add(new string(buffer.ToArray()));
        var input = string.Join("\n", lines).Trim();
        
        // Store the input for history if it's not empty
        if (!string.IsNullOrWhiteSpace(input))
        {
            lastInput = input;
        }
        
        return string.IsNullOrWhiteSpace(input) ? null : input;
    }

    public static async Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory)
    {
        await Task.CompletedTask; // Simulate asynchronous behavior
        var buffer = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace && buffer.Count > 0)
            {
                buffer.RemoveAt(buffer.Count - 1);
                Console.Write("\b \b");
            }
            else if (key.Key == ConsoleKey.Tab)
            {
                var current = new string(buffer.ToArray());
                var prefix = Path.GetDirectoryName(current) ?? ".";
                var partial = Path.GetFileName(current);
                var matches = Directory
                    .GetFileSystemEntries(prefix)
                    .Where(f => Path.GetFileName(f).StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Count == 1)
                {
                    var completion = matches[0];
                    for (int i = 0; i < partial.Length; i++) Console.Write("\b \b");
                    buffer.RemoveRange(buffer.Count - partial.Length, partial.Length);
                    buffer.AddRange(Path.GetFileName(completion));
                    Console.Write(Path.GetFileName(completion));
                }
                else if (matches.Count > 1)
                {
                    Console.WriteLine();
                    Console.WriteLine("Matches:");
                    matches.ForEach(m => Console.WriteLine("  " + m));
                    Console.Write("> " + new string(buffer.ToArray()));
                }
            }
            else if (key.KeyChar != '\0')
            {
                buffer.Add(key.KeyChar);
                Console.Write(key.KeyChar);
            }
        }

        var result = new string(buffer.ToArray());
        return string.IsNullOrWhiteSpace(result) ? null : Path.GetFullPath(result);
    }

    public static string? ReadLineWithHistory()
    {
        var buffer = new List<char>();
        int cursor = 0;
        ConsoleKeyInfo key;

        while (true)
        {
            key = Console.ReadKey(intercept: true);
            
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (cursor > 0)
                {
                    buffer.RemoveAt(cursor - 1);
                    cursor--;
                    Console.Write("\b \b");
                }
            }
            else if (key.Key == ConsoleKey.UpArrow)
            {
                if (lastInput != null)
                {
                    // Clear current buffer display
                    for (int i = 0; i < buffer.Count; i++)
                    {
                        Console.Write("\b \b");
                    }
                    
                    // Set buffer to last input
                    buffer.Clear();
                    buffer.AddRange(lastInput.ToCharArray());
                    cursor = buffer.Count;
                    
                    // Display the recalled input
                    Console.Write(lastInput);
                }
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                buffer.Insert(cursor, key.KeyChar);
                cursor++;
                Console.Write(key.KeyChar);
            }
        }
        
        var input = new string(buffer.ToArray()).Trim();
        
        // Store the input for history if it's not empty
        if (!string.IsNullOrWhiteSpace(input))
        {
            lastInput = input;
        }
        
        return string.IsNullOrWhiteSpace(input) ? null : input;
    }

    // Shared infrastructure for rendering chat messages with timestamps and role indicators
    public static void RenderChatMessage(ChatMessage message)
    {
        string timestamp = message.CreatedAt.ToString("HH:mm:ss");
        string roleIndicator;
        ConsoleColor roleColor, textColor = Console.ForegroundColor;
        
        switch (message.Role)
        {
            case Roles.Tool:
                roleIndicator = "[TOOL]";
                roleColor = ConsoleColor.Yellow;
                textColor = ConsoleColor.DarkGray; // Tool messages in dark gray
                break;            
            case Roles.System:
                roleIndicator = "[SYSTEM]";
                roleColor = ConsoleColor.DarkBlue;
                textColor = ConsoleColor.DarkGray; // System messages in gray
                break;
            case Roles.User:
                roleIndicator = "[USER]";
                roleColor = ConsoleColor.Cyan;
                break;
            case Roles.Assistant:
                roleIndicator = "[ASSISTANT]";
                roleColor = ConsoleColor.Green;
                break;
            default:
                roleIndicator = "[UNKNOWN]";
                roleColor = ConsoleColor.Red;
                break;
        }
        
        // For new messages in the main loop, show all messages with timestamp and role formatting
        // Render timestamp in gray
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(timestamp);
        Console.Write(" ");
        
        // Render role indicator in role-specific color
        Console.ForegroundColor = roleColor;
        Console.Write(roleIndicator);
        Console.Write(" ");

        // Render content
        Console.ForegroundColor = textColor; // Reset to original text color
        Console.WriteLine(message.Content);
        Console.ResetColor();
    }
    
    public static void RenderChatHistory(IEnumerable<ChatMessage> messages)
    {
        Console.WriteLine("Chat History:");
        Console.WriteLine(new string('-', 50));
        
        foreach (var message in messages)
        {
            // Skip empty system messages in history view
            if (message.Role == Roles.System && string.IsNullOrWhiteSpace(message.Content))
                continue;
                
            RenderChatMessage(message);
        }
        
        Console.WriteLine(new string('-', 50));
    }
}

public static class ProgressUi
{
    public enum ProgressState { Queued, Running, Failed, Canceled, Completed }

    public sealed class Item
    {
        public string Name { get; }
        public ProgressState State { get; private set; } = ProgressState.Queued;

        private int _total;
        private int _done;
        private readonly object _lock = new();

        public DateTime StartedUtc { get; private set; }
        public DateTime? EndedUtc { get; private set; }
        public string? Note { get; private set; }

        public Item(string name) => Name = name;

        public void SetTotalSteps(int total)
        {
            lock (_lock)
            {
                _total = Math.Max(0, total);
                if (State == ProgressState.Queued) { State = ProgressState.Running; StartedUtc = DateTime.UtcNow; }
            }
        }

        public void Advance(int delta = 1, string? note = null)
        {
            lock (_lock)
            {
                if (State is ProgressState.Canceled or ProgressState.Completed or ProgressState.Failed) return;
                if (State == ProgressState.Queued) { State = ProgressState.Running; StartedUtc = DateTime.UtcNow; }
                _done += Math.Max(0, delta);
                if (!string.IsNullOrWhiteSpace(note)) Note = note;
                if (_total > 0 && _done >= _total) CompleteInternal(note);
            }
        }

        public void Fail(string? note = null)
        {
            lock (_lock)
            {
                if (State is ProgressState.Completed or ProgressState.Canceled or ProgressState.Failed) return;
                State = ProgressState.Failed; Note = note; EndedUtc = DateTime.UtcNow;
            }
        }

        public void Cancel(string? note = null)
        {
            lock (_lock)
            {
                if (State is ProgressState.Completed or ProgressState.Canceled) return;
                State = ProgressState.Canceled; Note = note; EndedUtc = DateTime.UtcNow;
            }
        }

        public void Complete(string? note)
        {
            lock (_lock) { CompleteInternal(note); }
        }

        private void CompleteInternal(string? note)
        {
            if (State is ProgressState.Completed) return;
            if (_total > 0) _done = _total;
            State = ProgressState.Completed; Note = note; EndedUtc = DateTime.UtcNow;
        }

        public double Percent
        {
            get { lock (_lock) { return (_total <= 0) ? 0 : Math.Min(100.0, (100.0 * _done) / _total); } }
        }

        public (int done, int total) Steps
        {
            get { lock (_lock) return (_done, _total); }
        }
    }

    public sealed class AsyncProgressReporterWithCancel : IDisposable
    {
        private readonly int _regionTop = 0;
        private readonly int _regionHeight;
        private int _lastPaintedWidth;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private double _emaStepsPerSec = 0;           // smoothed throughput (steps/sec)
        private double _emaAvgStepsPerItem = 0;       // smoothed avg total steps per item
        private const double _alpha = 0.2;            // EMA smoothing (0..1)
        private double _emaSecondsRemaining = double.NaN;
        private long _lastTotalDoneForRate = 0;
        private DateTime _lastRateStampUtc = DateTime.UtcNow;
        private readonly List<Item> _items = new();
        private readonly object _lock = new();
        private readonly CancellationTokenSource _cts;
        private readonly Task _pump;
        private readonly int _topN;
        private readonly string _title;
        private bool _running = true;

        public CancellationToken Token => _cts.Token;

        public AsyncProgressReporterWithCancel(string title, CancellationTokenSource cts)
        {
            _title = title;
            _topN = Program.config.MaxMenuItems;
            _regionHeight = 3 /*header*/ + _topN /*rows*/ + 2 /*footer*/;
            _lastPaintedWidth = Math.Max(10, Console.WindowWidth - 1);
            _cts = cts;
            _pump = Task.Run(DrawLoop);
        }

        public Item StartItem(string name)
        {
            var it = new Item(name);
            lock (_lock) _items.Add(it);
            return it;
        }

        private void ClearRegion()
        {
            int width = Math.Max(10, Console.WindowWidth - 1);
            Console.SetCursorPosition(0, _regionTop);
            for (int i = 0; i < _regionHeight; i++)
            {
                Console.Write(new string(' ', width));
                if (i < _regionHeight - 1) Console.WriteLine();
            }
            Console.SetCursorPosition(0, _regionTop);
        }

        private async Task DrawLoop()
        {
            // Non-alloc UI loop. Reuse the menu area constraints you already have.
            // Only show Queued/Running (and maybe Failed) while active; hide Completed.
            Console.CursorVisible = false;
            try
            {
                while (_running && !_cts.IsCancellationRequested)
                {
                    while (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Escape)
                            _cts.Cancel();
                    }

                    DrawActive();
                    await Task.Delay(66); // ~15 fps
                }
            }
            finally
            {
                DrawSummary(); // on close
                Console.CursorVisible = true;
            }
        }

        private void DrawActive()
        {
            int width = Math.Max(10, Console.WindowWidth - 1);
            if (width != _lastPaintedWidth) { ClearRegion(); _lastPaintedWidth = width; }

            // snapshot of items we show
            List<Item> snapshot;
            lock (_lock)
            {
                snapshot = _items
                    .Where(i => i.State is ProgressState.Queued or ProgressState.Running || i.State == ProgressState.Failed)
                    .OrderByDescending(i => i.Percent)
                    .ThenBy(i => i.Name)
                    .Take(_topN)
                    .ToList();
            }

            // start at the top every frame
            Console.SetCursorPosition(0, _regionTop);

            DrawBoxedHeader(_title);

            // draw rows that exist
            int drawnRows = 0;
            foreach (var it in snapshot)
            {
                var p = it.Percent; var (done, total) = it.Steps;
                DrawProgressRow(it.Name, p, it.State, it.Note, done, total);
                drawnRows++;
            }

            // clear any leftover rows in the region
            for (int i = drawnRows; i < _topN; i++)
            {
                // full-width blank line to erase stale content
                Console.WriteLine(new string(' ', width));
            }

            // footer
            int totalCount, running, completed, failed, canceled, queued;
            lock (_lock)
            {
                totalCount = _items.Count;
                running = _items.Count(i => i.State == ProgressState.Running);
                completed = _items.Count(i => i.State == ProgressState.Completed);
                failed = _items.Count(i => i.State == ProgressState.Failed);
                canceled = _items.Count(i => i.State == ProgressState.Canceled);
                queued = _items.Count(i => i.State == ProgressState.Queued);
            }
            var eta = ComputeEta();
            DrawFooterStats(running, queued, completed, failed, canceled, $"ETA: {eta}   •   Press ESC to cancel");

            // park the cursor back at region top so the next frame never scrolls
            Console.SetCursorPosition(0, _regionTop);
        }

        private void DrawSummary()
        {
            ClearRegion();
            Console.SetCursorPosition(0, _regionTop);
            List<Item> done;
            lock (_lock) done = _items.ToList();

            DrawBoxedHeader(_title);

            var completed = done.Count(i => i.State == ProgressState.Completed);
            var failed = done.Where(i => i.State == ProgressState.Failed).ToList();
            var canceled = done.Count(i => i.State == ProgressState.Canceled);

            Console.WriteLine($"Completed: {completed} | Failed: {failed.Count} | Canceled: {canceled}");

            if (failed.Count > 0)
            {
                Console.WriteLine("Failed items:");
                foreach (var f in failed.Take(20))
                    Console.WriteLine($"  - {f.Name} {(string.IsNullOrWhiteSpace(f.Note) ? "" : $"[{f.Note}]")}");
                if (failed.Count > 20) Console.WriteLine($"  ...and {failed.Count - 20} more");
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _pump.Wait(500); } catch { /* ignore */ }
        }

        private string ComputeEta()
        {
            // Snapshot to avoid locking during math
            List<Item> snapshot;
            lock (_lock) snapshot = _items.ToList();

            // Partition
            var known = snapshot.Where(i => i.Steps.total > 0).ToList(); // items with totals
            var unknownActive = snapshot
                .Where(i => i.Steps.total == 0 &&
                    (i.State == ProgressState.Queued || i.State == ProgressState.Running))
                .ToList();

            long knownTotalSteps = 0;
            long knownDoneSteps = 0;
            long knownRemainingSteps = 0;

            foreach (var it in known)
            {
                var (done, total) = it.Steps;
                knownTotalSteps += total;

                long doneCap = (it.State == ProgressState.Completed || it.State == ProgressState.Failed || it.State == ProgressState.Canceled)
                    ? total
                    : Math.Min(done, total);

                knownDoneSteps += doneCap;
                knownRemainingSteps += Math.Max(0, total - doneCap);
            }

            // Smoothed avg steps per item (for estimating queued/unknown)
            if (known.Count > 0)
            {
                var instAvgStepsPerItem = (double)knownTotalSteps / known.Count;
                _emaAvgStepsPerItem = _emaAvgStepsPerItem <= 0
                    ? instAvgStepsPerItem
                    : _alpha * instAvgStepsPerItem + (1 - _alpha) * _emaAvgStepsPerItem;
            }

            // Estimate unknown work as (#unknown items) * avg steps per item (smoothed)
            var avgStepsPerItem = _emaAvgStepsPerItem > 0 ? _emaAvgStepsPerItem : 10.0;  // conservative default
            var unknownRemainingSteps = (long)Math.Round(avgStepsPerItem * unknownActive.Count);

            // Update smoothed throughput (steps/sec) using known done deltas
            var now = DateTime.UtcNow;
            var dt = Math.Max(0.001, (now - _lastRateStampUtc).TotalSeconds);
            var instRate = (knownDoneSteps - _lastTotalDoneForRate) / dt;

            if (_emaStepsPerSec <= 0 && instRate > 0)
            {
                _emaStepsPerSec = instRate;
            }
            else if (instRate >= 0)
            {
                _emaStepsPerSec = _alpha * instRate + (1 - _alpha) * _emaStepsPerSec;
            }

            _lastTotalDoneForRate = knownDoneSteps;
            _lastRateStampUtc = now;

            // Warm-up fallback: use global avg if EMA isn’t ready but progress exists
            if (_emaStepsPerSec <= 0 && knownDoneSteps > 0)
            {
                _emaStepsPerSec = knownDoneSteps / Math.Max(0.001, _sw.Elapsed.TotalSeconds);
            }

            if (_emaStepsPerSec <= 0)
            {
                return "--:--"; // cannot estimate yet
            }

            var totalRemaining = knownRemainingSteps + unknownRemainingSteps;
            var seconds = totalRemaining / _emaStepsPerSec;

            // Smooth the ETA itself to prevent bouncing
            _emaSecondsRemaining = double.IsNaN(_emaSecondsRemaining)
                ? seconds
                : _alpha * seconds + (1 - _alpha) * _emaSecondsRemaining;

            var ts = TimeSpan.FromSeconds(Math.Max(0, _emaSecondsRemaining));
            if (ts.TotalHours > 1)
            {
                return "??:??"; // too long to reasonably estimate.
            }

            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }

    public static void DrawBoxedHeader(string text)
    {
        int width = Math.Max(10, Console.WindowWidth - 1);
        Console.SetCursorPosition(0, 0);

        // Top border
        var top = "┌" + new string('─', Math.Max(0, width - 2)) + "┐";
        WriteFullWidth(top, ConsoleColor.DarkGray);

        // Middle line: │  title  │  (borders dark gray, title green)
        int inner = Math.Max(0, width - 2);
        var centered = CenterOrTrim(text, inner);

        // left border
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("│");
        // title area
        Console.ForegroundColor = ConsoleColor.Green;
        if (centered.Length < inner) centered = centered.PadRight(inner);
        if (centered.Length > inner) centered = centered.Substring(0, inner);
        Console.Write(centered);
        // right border
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("│");
        Console.ResetColor();

        // Separator
        var sep = "├" + new string('─', Math.Max(0, width - 2)) + "┤";
        WriteFullWidth(sep, ConsoleColor.DarkGray);
    }

    public static void DrawProgressRow(
        string name,
        double percent,
        ProgressUi.ProgressState state,
        string? note,
        int done,
        int total)
    {
        int width = Math.Max(10, Console.WindowWidth - 1);
        int row = Console.CursorTop;

        var glyph = state switch
        {
            ProgressUi.ProgressState.Running  => "▶",
            ProgressUi.ProgressState.Completed=> "✓",
            ProgressUi.ProgressState.Failed   => "✖",
            ProgressUi.ProgressState.Canceled => "■",
            _                                 => "•"
        };

        string left = $"{glyph} {name}";
        string right = total > 0
            ? $"{percent,6:0.0}% ({done}/{total})"
            : $"{percent,6:0.0}%";

        string line = ComposeLeftRight(left, right, width);

        var barBack = state switch
        {
            ProgressUi.ProgressState.Failed   => ConsoleColor.DarkRed,
            ProgressUi.ProgressState.Canceled => ConsoleColor.DarkGray,
            ProgressUi.ProgressState.Completed=> ConsoleColor.DarkGray,
            ProgressUi.ProgressState.Running  => ConsoleColor.DarkGray,
            _                                 => ConsoleColor.DarkBlue,
        };

        var fore = state switch
        {
            ProgressUi.ProgressState.Failed   => ConsoleColor.Yellow,
            ProgressUi.ProgressState.Canceled => ConsoleColor.Gray,
            _                                 => ConsoleColor.Gray
        };

        int fill = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * width);

        // Draw the two segments so the background fill remains visible "behind" the text.
        Console.SetCursorPosition(0, row);

        // segment 1 (within fill)
        var seg1 = line.Substring(0, Math.Min(fill, line.Length));
        Console.BackgroundColor = barBack;
        Console.ForegroundColor = fore;
        Console.Write(seg1);

        // segment 2 (rest of line)
        var seg2Len = Math.Max(0, width - seg1.Length);
        var seg2 = seg1.Length < line.Length ? line.Substring(seg1.Length) : new string(' ', seg2Len);
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = fore;
        Console.Write(seg2.PadRight(seg2Len));

        Console.ResetColor();

        // Move to next line for the caller
        Console.SetCursorPosition(0, row + 1);
    }

    public static void DrawFooterStats(
        int running,
        int queued,
        int completed,
        int failed,
        int canceled,
        string hint)
    {
        int width = Math.Max(10, Console.WindowWidth - 1);

        string stats = $"in-flight: {running}   queued: {queued}   completed: {completed}   failed: {failed}   canceled: {canceled}";
        WriteFullWidth(stats, ConsoleColor.DarkGray);
        WriteFullWidth(hint, ConsoleColor.DarkGray);
    }

    // ---------- small private helpers ----------

    private static void WriteFullWidth(string s, ConsoleColor fg)
    {
        int width = Math.Max(10, Console.WindowWidth - 1);
        Console.ForegroundColor = fg;
        if (s.Length > width) s = s.Substring(0, width);
        if (s.Length < width) s = s.PadRight(width);
        Console.WriteLine(s);
        Console.ResetColor();
    }

    private static string CenterOrTrim(string s, int innerWidth)
    {
        if (innerWidth <= 0) return string.Empty;
        if (s.Length > innerWidth) s = s.Substring(0, Math.Max(0, innerWidth - 3)) + "...";
        int pad = innerWidth - s.Length;
        int left = pad / 2;
        int right = pad - left;
        return new string(' ', left) + s + new string(' ', right);
    }

    private static string ComposeLeftRight(string left, string right, int width)
    {
        // Ensure the right label fits; truncate left with ellipsis if needed
        int rightLen = right.Length;
        int availLeft = Math.Max(0, width - rightLen - 1); // at least one space gap
        if (left.Length > availLeft)
        {
            // leave room for ellipsis
            int keep = Math.Max(0, availLeft - 3);
            left = (keep > 0 ? left.Substring(0, keep) : "") + (availLeft > 0 ? "..." : "");
        }
        int pad = Math.Max(1, width - left.Length - rightLen);
        return left + new string(' ', pad) + right;
    }
}