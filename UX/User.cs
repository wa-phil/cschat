using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;

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
            _lastPaintedWidth = Math.Max(10, Program.ui.Width - 1);
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
            int width = Math.Max(10, Program.ui.Width - 1);
            Program.ui.SetCursorPosition(0, _regionTop);
            for (int i = 0; i < _regionHeight; i++)
            {
                Program.ui.Write(new string(' ', width));
                if (i < _regionHeight - 1) Program.ui.WriteLine();
            }
            Program.ui.SetCursorPosition(0, _regionTop);
        }

        private async Task DrawLoop()
        {
            // Non-alloc UI loop. Reuse the menu area constraints you already have.
            // Only show Queued/Running (and maybe Failed) while active; hide Completed.
            Program.ui.CursorVisible = false;
            try
            {
                while (_running && !_cts.IsCancellationRequested)
                {
                    while (Program.ui.KeyAvailable)
                    {
                        var key = Program.ui.ReadKey(intercept: true);
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
                Program.ui.CursorVisible = true;
            }
        }

        private void DrawActive()
        {
            int width = Math.Max(10, Program.ui.Width - 1);
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
            Program.ui.SetCursorPosition(0, _regionTop);

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
                Program.ui.WriteLine(new string(' ', width));
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
            Program.ui.SetCursorPosition(0, _regionTop);
        }

        private void DrawSummary()
        {
            ClearRegion();
            Program.ui.SetCursorPosition(0, _regionTop);
            List<Item> done;
            lock (_lock) done = _items.ToList();

            DrawBoxedHeader(_title);

            var completed = done.Count(i => i.State == ProgressState.Completed);
            var failed = done.Where(i => i.State == ProgressState.Failed).ToList();
            var canceled = done.Count(i => i.State == ProgressState.Canceled);

            Program.ui.WriteLine($"Completed: {completed} | Failed: {failed.Count} | Canceled: {canceled}");

            if (failed.Count > 0)
            {
                Program.ui.WriteLine("Failed items:");
                foreach (var f in failed.Take(20))
                    Program.ui.WriteLine($"  - {f.Name} {(string.IsNullOrWhiteSpace(f.Note) ? "" : $"[{f.Note}]")}");
                if (failed.Count > 20) Program.ui.WriteLine($"  ...and {failed.Count - 20} more");
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
            var seconds = _emaStepsPerSec > 0 ? totalRemaining / _emaStepsPerSec : double.MaxValue;

            // Clamp seconds to a reasonable range to avoid overflow
            if (double.IsInfinity(seconds) || double.IsNaN(seconds) || seconds > TimeSpan.MaxValue.TotalSeconds)
            {
                seconds = TimeSpan.MaxValue.TotalSeconds;
            }

            // Smooth the ETA itself to prevent bouncing
            _emaSecondsRemaining = double.IsNaN(_emaSecondsRemaining)
                ? seconds
                : _alpha * seconds + (1 - _alpha) * _emaSecondsRemaining;

            var ts = TimeSpan.FromSeconds(Math.Max(0, Math.Min(_emaSecondsRemaining, TimeSpan.MaxValue.TotalSeconds)));
            if (ts.TotalHours > 1)
            {
                return "??:??"; // too long to reasonably estimate.
            }

            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }

    public static void DrawBoxedHeader(string text)
    {
        int width = Math.Max(10, Program.ui.Width - 1);
        Program.ui.SetCursorPosition(0, 0);

        // Top border
        var top = "┌" + new string('─', Math.Max(0, width - 2)) + "┐";
        WriteFullWidth(top, ConsoleColor.DarkGray);

        // Middle line: │  title  │  (borders dark gray, title green)
        int inner = Math.Max(0, width - 2);
        var centered = CenterOrTrim(text, inner);

        // left border
        Program.ui.ForegroundColor = ConsoleColor.DarkGray;
        Program.ui.Write("│");
        // title area
        Program.ui.ForegroundColor = ConsoleColor.Green;
        if (centered.Length < inner) centered = centered.PadRight(inner);
        if (centered.Length > inner) centered = centered.Substring(0, inner);
        Program.ui.Write(centered);
        // right border
        Program.ui.ForegroundColor = ConsoleColor.DarkGray;
        Program.ui.WriteLine("│");
        Program.ui.ResetColor();

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
        int width = Math.Max(10, Program.ui.Width - 1);
        int row = Program.ui.CursorTop;

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
        Program.ui.SetCursorPosition(0, row);

        // segment 1 (within fill)
        var seg1 = line.Substring(0, Math.Min(fill, line.Length));
        Program.ui.BackgroundColor = barBack;
        Program.ui.ForegroundColor = fore;
        Program.ui.Write(seg1);

        // segment 2 (rest of line)
        var seg2Len = Math.Max(0, width - seg1.Length);
        var seg2 = seg1.Length < line.Length ? line.Substring(seg1.Length) : new string(' ', seg2Len);
        Program.ui.BackgroundColor = ConsoleColor.Black;
        Program.ui.ForegroundColor = fore;
        Program.ui.Write(seg2.PadRight(seg2Len));

        Program.ui.ResetColor();

        // Move to next line for the caller
        Program.ui.SetCursorPosition(0, row + 1);
    }

    public static void DrawFooterStats(
        int running,
        int queued,
        int completed,
        int failed,
        int canceled,
        string hint)
    {
        int width = Math.Max(10, Program.ui.Width - 1);

        string stats = $"in-flight: {running}   queued: {queued}   completed: {completed}   failed: {failed}   canceled: {canceled}";
        WriteFullWidth(stats, ConsoleColor.DarkGray);
        WriteFullWidth(hint, ConsoleColor.DarkGray);
    }

    // ---------- small private helpers ----------

    private static void WriteFullWidth(string s, ConsoleColor fg)
    {
        int width = Math.Max(10, Program.ui.Width - 1);
        Program.ui.ForegroundColor = fg;
        if (s.Length > width) s = s.Substring(0, width);
        if (s.Length < width) s = s.PadRight(width);
        Program.ui.WriteLine(s);
        Program.ui.ResetColor();
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