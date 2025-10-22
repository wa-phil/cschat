using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;

public enum ProgressState { Queued, Running, Failed, Canceled, Completed }

public sealed class ProgressItem
{
    public string Name { get; }
    public ProgressState State { get; private set; } = ProgressState.Queued;
    private readonly object _gate = new();
    private int _total, _done;
    public DateTime StartedUtc { get; private set; }
    public DateTime? EndedUtc { get; private set; }
    public string? Note { get; private set; }

    internal ProgressItem(string name) => Name = name;
    public void SetTotal(int total)
    {
        lock (_gate)
        {
            _total = Math.Max(0, total);
            if (State == ProgressState.Queued)
            {
                State = ProgressState.Running;
                StartedUtc = DateTime.UtcNow;
            }
        }
    }

    public void Advance(int delta = 1, string? note = null)
    {
        lock (_gate)
        {
            if (State is ProgressState.Completed or ProgressState.Failed or ProgressState.Canceled) return;
            if (State == ProgressState.Queued)
            {
                State = ProgressState.Running;
                StartedUtc = DateTime.UtcNow;
            }
            _done += Math.Max(0, delta);
            if (!string.IsNullOrWhiteSpace(note)) Note = note;
            if (_total > 0 && _done >= _total) Complete(note);
        }
    }

    public void Fail(string? note = null)
    {
        lock (_gate)
        {
            if (State is ProgressState.Completed or ProgressState.Canceled or ProgressState.Failed) return;
            State = ProgressState.Failed;
            Note = note;
            EndedUtc = DateTime.UtcNow;
        }
    }

    public void Cancel(string? note = null)
    {
        lock (_gate)
        {
            if (State is ProgressState.Completed or ProgressState.Canceled) return;
            State = ProgressState.Canceled;
            Note = note;
            EndedUtc = DateTime.UtcNow;
        }
    }

    public void Complete(string? note = null)
    {
        lock (_gate)
        {
            if (State == ProgressState.Completed) return;
            if (_total > 0) _done = _total;
            State = ProgressState.Completed;
            Note = note;
            EndedUtc = DateTime.UtcNow;
        }
    }
    
    public double Percent
    {
        get
        {
            lock (_gate) {
                return (_total <= 0) ? 0 : Math.Min(100.0, 100.0 * _done / _total);
            }
        }
    }
    
    public (int done, int total) Steps
    {
        get
        {
            lock (_gate)
            {
                return (_done, _total);
            }
        }
    }
}

public sealed record ProgressSnapshot(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<(string name, double percent, ProgressState state, string? note, (int done, int total) steps)> Items,
    (int running, int queued, int completed, int failed, int canceled) Stats,
    string? EtaHint,
    bool IsActive);

public sealed class AsyncProgress
{
    public static Builder For(string title) => new(title);

    public sealed class Builder
    {
        private readonly string _title;
        private string _description = string.Empty;
        private int _maxConcurrency = Program.config.RagSettings.MaxIngestConcurrency;
        private CancellationTokenSource? _externalCts;

        internal Builder(string title) { _title = title; }
        public Builder MaxConcurrency(int n) { _maxConcurrency = Math.Max(1, n); return this; }
        public Builder WithCancellation(CancellationTokenSource cts) { _externalCts = cts; return this; }
        public Builder WithDescription(string desc) { _description = desc ?? string.Empty; return this; }

        public async Task<(IReadOnlyList<TResult> results, IReadOnlyList<(string name, string? error)> failures, bool canceled)>
            Run<T, TResult>(
                Func<IEnumerable<T>> items,
                Func<T, string> nameOf,
                Func<T, ProgressItem, CancellationToken, Task<TResult>> processAsync)
        {
            // Per-run CTS + link to any caller CTS
            using var runCts = new CancellationTokenSource(); // the one the UI will cancel via ESC
            using var workCts = (_externalCts != null)
                ? CancellationTokenSource.CreateLinkedTokenSource(runCts.Token, _externalCts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(runCts.Token); // still gives a single token to cancel

            var ct = workCts.Token;
            using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // Give UI the *run* CTS (so ESC cancels this run only)
            var id = Program.ui.StartProgress(_title, runCts);

            var all = items()?.ToList() ?? new List<T>();
            var sem = new SemaphoreSlim(_maxConcurrency);
            var results = new ConcurrentBag<TResult>();
            var failures = new ConcurrentBag<(string name, string? error)>();
            var progressItems = new ConcurrentDictionary<object, ProgressItem>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // background pump to emit periodic snapshots
            var pump = Task.Run(async () =>
            {
                var t = pumpCts.Token;
                while (!t.IsCancellationRequested)
                {
                    Program.ui.UpdateProgress(id, Snapshot(_title, _description, progressItems.Values.ToList(), active:true));
                    try { await Task.Delay(100, t); } catch { /* canceled */ }
                }
            }, pumpCts.Token);

            try
            {
                var tasks = all.Select(async item =>
                {
                    // make it visible as Queued right away
                    var pi = new ProgressItem(nameOf(item));
                    progressItems[item!] = pi;

                    bool acquired = false;
                    try
                    {
                        await sem.WaitAsync(ct);
                        acquired = true;

                        var result = await processAsync(item, pi, ct);

                        // if the worker didn’t set a total, guarantee a completion transition
                        if (pi.Steps.total == 0) pi.SetTotal(1);
                        pi.Complete("done");
                        results.Add(result);
                    }
                    catch (OperationCanceledException)
                    {
                        // Swallow OCE to avoid bubbling to WhenAll; mark item canceled
                        pi.Cancel("canceled");
                        failures.Add((pi.Name, "canceled"));
                    }
                    catch (Exception ex)
                    {
                        pi.Fail(ex.Message);
                        failures.Add((pi.Name, ex.Message));
                    }
                    finally
                    {
                        if (acquired) sem.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }
            finally
            {
                sw.Stop();
                pumpCts.Cancel();
                try { await pump; } catch { /* ignore */ }
                // final snapshot & artifact
                var final = Snapshot(_title, _description, progressItems.Values.ToList(), active:false);
                var summaryMd = RenderSummaryMarkdown(final, sw.Elapsed);
                // 1. persist to chat history (do not render here -- UIs handle visuals)
                try { Program.Context.AddToolMessage(summaryMd); } catch { /* best effort */ }
                // 2. Tell the UI to freeze the live bubble into the artifact
                try { Program.ui.CompleteProgress(id, final, summaryMd); } catch { /* best-effort */ }
                try { if (!pump.IsCompleted) _ = pump.ContinueWith(_ => { }); } catch { }
            }

            return (results.ToList(), failures.ToList(),
                    runCts.IsCancellationRequested || (_externalCts?.IsCancellationRequested ?? false));
        }

        private static ProgressSnapshot Snapshot(string title, string description, List<ProgressItem> items, bool active)
        {
            int running = 0, queued = 0, completed = 0, failed = 0, canceled = 0;
            var rows = new List<(string,double,ProgressState,string? ,(int,int))>(items.Count);
            foreach (var it in items)
            {
                switch (it.State)
                {
                    case ProgressState.Running:  running++; break;
                    case ProgressState.Queued:   queued++; break;
                    case ProgressState.Completed:completed++; break;
                    case ProgressState.Failed:   failed++; break;
                    case ProgressState.Canceled: canceled++; break;
                }
                rows.Add((it.Name, it.Percent, it.State, it.Note, it.Steps));
            }
            // Simple ETA: items with totals only
            var known = items.Where(i => i.Steps.total > 0).ToList();
            string? eta = null;
            if (active && known.Count > 0)
            {
                var done = known.Sum(i => Math.Min(i.Steps.done, i.Steps.total));
                var total = known.Sum(i => i.Steps.total);
                var remain = Math.Max(0, total - done);
                eta = remain == 0 ? "0s" : "…";
            }

            return new ProgressSnapshot(
                Id: Guid.NewGuid().ToString("n"), // renderer can ignore
                Title: title,
                Description: description,
                Items: rows,
                Stats: (running, queued, completed, failed, canceled),
                EtaHint: eta,
                IsActive: active
            );
        }

        private static string RenderSummaryMarkdown(ProgressSnapshot s, TimeSpan elapsed)
        {
            var (r,q,c,f,x) = s.Stats;
            var total = r+q+c+f+x;
            var failedList = s.Items.Where(i => i.state == ProgressState.Failed)
                                    .Take(20)
                                    .Select(i => $"- {i.name} `{i.note}`");
            var more = Math.Max(0, s.Items.Count(i => i.state == ProgressState.Failed) - 20);
            var moreLine = more > 0 ? $"\n…and **{more}** more failures." : "";
            return
$@"**{s.Title+(string.IsNullOrWhiteSpace(s.Description) ? "" : $": {s.Description}")}**
— Finished : **{elapsed.TotalMilliseconds:N0}** ms
- Processed: **{total}**
- Completed: **{c}**   Failed: **{f}**   Canceled: **{x}**

{(f>0 ? ("### Failed items\n" + string.Join("\n", failedList) + moreLine) : "_No failures._")}";
        }
    }
}

/// <summary>
/// UI composition helpers for Progress. Builds a backend-agnostic UiNode subtree
/// for rendering progress as generic nodes (Column/Row/Label).
/// </summary>
public static class ProgressUi
{
    /// <summary>
    /// Create a composed progress node with header (title + stats/eta), items list, and footer hint.
    /// Root node key: "progress-{id}".
    /// </summary>
    public static UiNode CreateNode(string id, ProgressSnapshot snapshot)
    {
        var keyPrefix = $"progress-{id}";

        // Header: left title, right stats+eta
        var (running, queued, completed, failed, canceled) = snapshot.Stats;
        var statsText = $"in-flight: {running}   queued: {queued}   completed: {completed}   failed: {failed}   canceled: {canceled}";
        var etaText = !string.IsNullOrWhiteSpace(snapshot.EtaHint) ? $"ETA: {snapshot.EtaHint}" : null;

        var headerChildren = new List<UiNode>
        {
            new UiNode($"{keyPrefix}-title", UiKind.Label,
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = snapshot.Title },
                Array.Empty<UiNode>(),
                UiStyles.Of((UiStyleKey.Bold, true)))
        };
        var rightHeaderText = string.IsNullOrWhiteSpace(etaText) ? statsText : $"{statsText}   {etaText}";
        headerChildren.Add(
            new UiNode($"{keyPrefix}-stats", UiKind.Label,
                new Dictionary<UiProperty, object?> { [UiProperty.Text] = rightHeaderText, [UiProperty.Align] = "right" },
                Array.Empty<UiNode>(),
                UiStyles.Empty)
        );

        var header = new UiNode($"{keyPrefix}-header", UiKind.Row,
            new Dictionary<UiProperty, object?> { [UiProperty.Layout] = "row-justify" },
            headerChildren.ToArray());

        // Items list (rank interesting tasks first)
        static int Rank(ProgressState s) => s switch
        {
            ProgressState.Running => 3,
            ProgressState.Queued => 2,
            ProgressState.Failed => 1,
            _ => 0
        };

        var items = snapshot.Items ?? Array.Empty<(string name, double percent, ProgressState state, string? note, (int done, int total) steps)>();
        var rows = items
            .OrderByDescending(x => Rank(x.state))
            .ThenBy(x => x.percent)
            .ThenBy(x => x.name)
            .Take(Math.Min(10, Program.config.MaxMenuItems))
            .Select((r, idx) =>
            {
                var glyph = r.state switch
                {
                    ProgressState.Running => "▶",
                    ProgressState.Completed => "✓",
                    ProgressState.Failed => "✖",
                    ProgressState.Canceled => "■",
                    _ => "•"
                };
                var steps = r.steps.total > 0 ? $" ({r.steps.done}/{r.steps.total})" : string.Empty;
                var leftText = $"{glyph} {r.name}" + (string.IsNullOrWhiteSpace(r.note) ? string.Empty : $" — {r.note}");
                var rightText = $"{r.percent:0.0}%{steps}";

                var left = new UiNode($"{keyPrefix}-item-{idx}-left", UiKind.Label,
                    new Dictionary<UiProperty, object?> { [UiProperty.Text] = leftText },
                    Array.Empty<UiNode>());
                var right = new UiNode($"{keyPrefix}-item-{idx}-right", UiKind.Label,
                    new Dictionary<UiProperty, object?> { [UiProperty.Text] = rightText, [UiProperty.Align] = "right" },
                    Array.Empty<UiNode>());

                return new UiNode($"{keyPrefix}-item-{idx}", UiKind.Row,
                    new Dictionary<UiProperty, object?> { [UiProperty.Layout] = "row-justify" },
                    new[] { left, right });
            })
            .ToArray();

        var itemsContainer = new UiNode($"{keyPrefix}-items", UiKind.Column,
            new Dictionary<UiProperty, object?>(),
            rows);

        // Footer hint
        var hint = new UiNode($"{keyPrefix}-hint", UiKind.Label,
            new Dictionary<UiProperty, object?> { [UiProperty.Text] = "Press ESC to cancel", [UiProperty.Style] = "dim" },
            Array.Empty<UiNode>());

        return new UiNode(
            keyPrefix,
            UiKind.Column,
            new Dictionary<UiProperty, object?>
            {
                [UiProperty.State] = ChatMessageState.EphemeralActive.ToString(),
                [UiProperty.Role] = "progress"
            },
            new[] { header, itemsContainer, hint }
        );
    }
}