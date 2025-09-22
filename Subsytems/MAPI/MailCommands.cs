using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

public static class MailCommands
{
    public static Command Commands(MapiMailClient mail)
    {
        return new Command
        {
            Name = "Mail",
            Description = () => "Mail (MAPI)",
            SubCommands = new List<Command>
            {
                // -------------------------
                // 1) Simple text client
                // -------------------------
                new Command {
                    Name = "client",
                    Description = () => $"Open a text mail client on a selected favorite folder, looking back {Program.config.MailSettings.LookbackWindow} days, up to {Program.config.MailSettings.LookbackCount} emails",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        if (provider is null) { Program.ui.WriteLine("Mail provider unavailable."); return Command.Result.Failed; }

                        var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                        if (favs.Count == 0)
                        {
                            Program.ui.WriteLine("No favorites yet. Use 'Mail favorites' first.");
                            return Command.Result.Cancelled;
                        }

                        // Pick a favorite and N
                        var favChoices = favs.Select(f => f.DisplayName).ToList();
                        var favPick = Program.ui.RenderMenu("Select a favorite folder\n" + new string('─', Math.Max(60, Program.ui.Width-1)), favChoices);
                        if (string.IsNullOrWhiteSpace(favPick)) return Command.Result.Cancelled;

                        int topN = Program.config.MailSettings.LookbackCount;
                        TimeSpan lookback = TimeSpan.FromDays(Program.config.MailSettings.LookbackWindow);
                        var folder = await provider.GetFolderByIdOrNameAsync(favPick);
                        if (folder is null) { Program.ui.WriteLine("Folder not found."); return Command.Result.Failed; }

                        while (true)
                        {
                            var msgs = await provider.ListMessagesSinceAsync(folder, lookback, topN); // fetch by N (cap by huge window)
                            msgs = msgs.OrderByDescending(m => m.ReceivedDateTime).Take(topN).ToList();
                            var lengths = msgs.Select(m=> new {
                                fromWidth = m.From?.EmailAddress?.Length ?? 0,
                                dateWidth = m.ReceivedDateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm").Length,
                                subjWidth = Utilities.TruncatePlain(m.Subject ?? "", 40).Length,
                                firstWidth = Utilities.TruncatePlain((m.BodyPreview ?? "").Replace("\r"," ").Replace("\n"," "), Math.Max(10, Program.ui.Width - 70)).Length,
                            }).ToList();

                            // Calculate max widths for From, Date columns, splitting the remaining width between Subject and First Line
                            int consoleW = Program.ui.Width;
                            int maxFromWidth = lengths.Max(l => l.fromWidth);
                            int maxDateWidth = lengths.Max(l => l.dateWidth);
                            int remainingForSubjAndFirst = Program.ui.Width - maxFromWidth - maxDateWidth - 6; // 6 for " | " and padding
                            int maxSubjWidth = Math.Min(remainingForSubjAndFirst/2,lengths.Max(l => l.subjWidth));
                            int maxFirstWidth = Math.Min(remainingForSubjAndFirst/2,lengths.Max(l => l.firstWidth));

                            // Menu rows: "From | Date | Subject | First Line"
                            List<string> ToRows(List<IMailMessage> items)
                            {
                                string Row(IMailMessage m)
                                {
                                    string from = m.From?.EmailAddress ?? "(unknown)";
                                    string date = m.ReceivedDateTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                                    string subj = Utilities.TruncatePlain(m.Subject ?? "", 40);
                                    string first = Utilities.TruncatePlain((m.BodyPreview ?? "").Replace("\r", " ").Replace("\n", " "), Math.Max(10, consoleW - 70));

                                    return string.Format(
                                        "{0,-" + maxFromWidth + "} | {1,-" + maxDateWidth + "} | {2,-" + maxSubjWidth + "} | {3,-" + maxFirstWidth + "}",
                                        from, date, subj, first);
                                }
                                return items.Select(Row).ToList();
                            }

                            var header = $"Folder: {favPick}\n" +
                                        "Use ↑/↓, ENTER to read; ESC to exit\n" +
                                        string.Format(
                                            "  {0,-" + maxFromWidth + "} | {1,-" + maxDateWidth + "} | {2,-" + maxSubjWidth + "} | {3,-" + maxFirstWidth + "}\n",
                                            "From", "Date", "Subject", "First Line")+
                                        new string('─', Math.Max(60, Program.ui.Width-1));
                            var rows = ToRows(msgs);
                            var selectedRow = Program.ui.RenderMenu(header, rows);
                            if (string.IsNullOrWhiteSpace(selectedRow)) return Command.Result.Cancelled;
                            var idx = rows.IndexOf(selectedRow);
                            if (idx < 0) continue;

                            var picked = msgs[idx];
                            var opened = await provider.GetMessageAsync(picked.Id);
                            if (opened is null)
                            {
                                Program.ui.WriteLine("(message no longer available)");
                                continue;
                            }

                            // Read mode with scroll + ESC menu
                            await ReadEmailLoop(provider, opened);
                        }
                    }
                },

                // -------------------------
                // 2) Summarize (topics) — batched, navigable
                // -------------------------
                new Command {
                    Name = "summarize",
                    Description = () => $"Summarize last {Program.config.MailSettings.MaxEmailsToProcess} items in a favorite folder by configured topics (with drill-in)",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        if (provider is null) { Program.ui.WriteLine("Mail provider unavailable."); return Command.Result.Failed; }

                        var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                        if (favs.Count == 0) { Program.ui.WriteLine("No favorites yet. Use 'Mail favorites' first."); return Command.Result.Cancelled; }

                        var topics = Program.userManagedData.GetItems<MailTopic>();
                        if (topics.Count == 0)
                        {
                            Program.ui.WriteLine("No topics configured. Add some with Data→add (type: MailTopic).");
                        }

                        var favChoices = favs.Select(f => f.DisplayName).ToList();
                        var favPick = Program.ui.RenderMenu("Select a favorite folder\n" + new string('─', Math.Max(60, Program.ui.Width - 1)), favChoices);
                        if (string.IsNullOrWhiteSpace(favPick)) return Command.Result.Cancelled;

                        int topN = Program.config.MailSettings.MaxEmailsToProcess;
                        var folder = await provider.GetFolderByIdOrNameAsync(favPick);
                        if (folder is null) { Program.ui.WriteLine("Folder not found."); return Command.Result.Failed; }

                        var msgs = await provider.ListMessagesSinceAsync(folder, TimeSpan.FromDays(Program.config.MailSettings.LookbackWindow), topN);
                        msgs = msgs.OrderByDescending(m => m.ReceivedDateTime).Take(topN).ToList();

                        // 1) Build topic prompt once
                        var topicBlock = string.Join("\n", topics.Select(t => $"- {t.Name}: {t.Description}\n  keywords: {t.Keywords}"));
                        var classifyPrompt =
@"You are a mail classifier. Given topics (name/description/keywords) and an email (from/subject/preview), assign the email to AT MOST one topic (by name).
Valid topics, their name/description/keywords are as follows:" +
topicBlock +
@"
If none of the above fits, assign 'Other'.";

                        // 2) Classify in batches to keep prompts small
                        var idToMsg = msgs.ToDictionary(m => m.Id);
                        var assignments = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        using var cts = new CancellationTokenSource();
                        using (var reporter = new ProgressUi.AsyncProgressReporterWithCancel("Classifying emails", cts))
                        {
                            var gate = new SemaphoreSlim(Math.Max(1, Program.config.RagSettings.MaxIngestConcurrency));
                            var tasks = msgs.Select((m, idx) => Task.Run(async () =>
                            {
                                await gate.WaitAsync(reporter.Token);
                                var item = reporter.StartItem($"msg {idx + 1}/{msgs.Count}");
                                try
                                {
                                    item.SetTotalSteps(1);
                                    var ctx = new Context(classifyPrompt);
                                    ctx.AddUserMessage($"EMAIL\nfrom: {m.From?.EmailAddress}\nsubject: {m.Subject}\npreview: {m.BodyPreview}");

                                    try
                                    {
                                        var assignment = await TypeParser.GetAsync(ctx, typeof(MailAssignment)) as MailAssignment;
                                        assignments[m.Id] = String.IsNullOrWhiteSpace(assignment?.Topic) ? "Other" : assignment.Topic;
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        item.Cancel("canceled");
                                        assignments[m.Id] = "Other";
                                        return;
                                    }
                                    catch (Exception ex)
                                    {
                                        item.Fail(ex.Message);
                                        assignments[m.Id] = "Other";
                                        return;
                                    }

                                    item.Advance(1, "done");
                                    item.Complete("done");
                                }
                                finally
                                {
                                    gate.Release();
                                }
                            })).ToList();

                            try { await Task.WhenAll(tasks); }
                            catch (Exception ex) { Program.ui.WriteLine($"Error during classification: {ex.Message}"); }
                        }

                        // 3) Group by topic
                        var grouped = assignments
                            .GroupBy(kv => kv.Value)
                            .ToDictionary(g => g.Key, g => g.Select(kv => idToMsg[kv.Key]).OrderByDescending(m => m.ReceivedDateTime).ToList(),
                                        StringComparer.OrdinalIgnoreCase);

                        if (!grouped.ContainsKey("Other"))
                            grouped["Other"] = msgs.Where(m => !assignments.ContainsKey(m.Id)).ToList();

                        // 4) Summarize each topic separately (short) — run with bounded concurrency and progress reporting
                        var topicSummaries = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        using var ctsSummary = new CancellationTokenSource();
                        using (var reporterSummary = new ProgressUi.AsyncProgressReporterWithCancel("Summarizing topics", ctsSummary))
                        {
                            var summaryGate = new SemaphoreSlim(Math.Max(1, Program.config.RagSettings.MaxIngestConcurrency));
                            var summaryTasks = grouped.Select(kv => Task.Run(async () =>
                            {
                                await summaryGate.WaitAsync(reporterSummary.Token);
                                var item = reporterSummary.StartItem($"topic: {kv.Key} ({kv.Value.Count})");
                                try
                                {
                                    item.SetTotalSteps(1);
                                    var key = kv.Key;
                                    var list = kv.Value;
                                    var docs = string.Join("\n---\n", list.Select(m =>
                                        $"from: {m.From?.EmailAddress}\nsubject: {m.Subject}\npreview: {m.BodyPreview}"));

                                    var sctx = new Context(
@"You are a mail analyst. Summarize this set of emails into 1-2 short paragraphs, then provide 1-6 concise bullets of actions or follow-ups. Be crisp.");
                                    sctx.AddUserMessage(docs);
                                    try
                                    {
                                        var summary = await Engine.Provider!.PostChatAsync(sctx, 0.2f);
                                        topicSummaries[key] = summary;
                                        item.Advance(1, "done");
                                        item.Complete("done");
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        item.Cancel("canceled");
                                        topicSummaries[key] = "ERROR: canceled";
                                    }
                                    catch (Exception ex)
                                    {
                                        item.Fail(ex.Message);
                                        topicSummaries[key] = $"ERROR: {ex.Message}";
                                    }
                                }
                                finally { summaryGate.Release(); }
                            })).ToList();

                            try { await Task.WhenAll(summaryTasks); }
                            catch (Exception ex) { Program.ui.WriteLine($"Error during topic summarization: {ex.Message}"); }
                        }

                        // 5) Interactive menus: categories -> subjects -> read
                        while (true)
                        {
                            var categoryChoices = grouped
                                .OrderByDescending(kv => kv.Value.Count)
                                .Select(kv => $"{kv.Key} ({kv.Value.Count})")
                                .ToList();

                            var catPick = Program.ui.RenderMenu(
                                "Select a category\n" + new string('─', Math.Max(60, Program.ui.Width - 1)),
                                categoryChoices);

                            if (string.IsNullOrWhiteSpace(catPick)) return Command.Result.Cancelled;

                            var pickedCat = grouped.Keys.First(k => catPick.StartsWith(k));
                            Program.ui.Clear();
                            var header = $"== {pickedCat} ==\n{topicSummaries[pickedCat]}\n"+
                                new string('─', Math.Max(60, Program.ui.Width - 1))+
                                "Select an email (ESC to go back)\n"+
                                new string('─', Math.Max(60, Program.ui.Width - 1));

                            var list = grouped[pickedCat];
                            var subjectChoices = list.Select(m =>
                                $"{m.ReceivedDateTime.LocalDateTime:yyyy-MM-dd HH:mm} | {m.From?.EmailAddress} | {Utilities.TruncatePlain(m.Subject ?? string.Empty, 80)}")
                                .ToList();

                            var subjPick = Program.ui.RenderMenu(header, subjectChoices);
                            if (string.IsNullOrWhiteSpace(subjPick)) continue;

                            var idx = subjectChoices.IndexOf(subjPick);
                            if (idx >= 0)
                            {
                                var full = await provider.GetMessageAsync(list[idx].Id);
                                if (full != null) await ReadEmailLoop(provider, full); // reuse the same reader
                            }
                        }
                    }
                },

                // -------------------------
                // 3) Favorites (picker)
                // -------------------------
                new Command {
                    Name = "favorites",
                    Description = () => "Browse folders and manage favorites",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        if (provider is null) { Program.ui.WriteLine("Mail provider unavailable."); return Command.Result.Failed; }

                        // Top-level stores
                        var roots = await provider.ListFoldersAsync(null, 200);
                        if (roots is null || roots.Count == 0) { Program.ui.WriteLine("(no folders)"); return Command.Result.Success; }

                        // Let user choose a root (store), then browse one level down, add/remove favorites
                        while (true)
                        {
                            var rootChoices = roots.Select(r => $"{r.DisplayName} ({r.TotalItemCount}/{r.UnreadItemCount} unread)").ToList();
                            var rootPick = Program.ui.RenderMenu("Select a root folder (ESC to exit)\n" + new string('─', Math.Max(60, Program.ui.Width-1)), rootChoices);
                            if (string.IsNullOrWhiteSpace(rootPick)) return Command.Result.Cancelled;

                            var root = roots[rootChoices.IndexOf(rootPick)];

                            var subs = await provider.ListFoldersAsync(root.DisplayName, 500);
                            var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                            var favSet = new HashSet<string>(favs.Select(f => f.IdOrName), StringComparer.OrdinalIgnoreCase);

                            var subChoices = subs.Select(sf =>
                            {
                                var star = favSet.Contains(sf.DisplayName) ? "★" : "☆";
                                return $"{star} {sf.DisplayName} ({sf.TotalItemCount}/{sf.UnreadItemCount} unread)";
                            }).ToList();

                            var subPick = Program.ui.RenderMenu("Toggle favorite with ENTER; ESC to go back\n" + new string('─', Math.Max(60, Program.ui.Width-1)), subChoices);
                            if (string.IsNullOrWhiteSpace(subPick)) continue; // go back to root selection

                            var picked = subs[subChoices.IndexOf(subPick)];
                            if (favSet.Contains(picked.DisplayName))
                            {
                                // remove
                                Program.config.UserManagedData.TypedData.TryGetValue(nameof(FavoriteMailFolder), out var list);
                                if (list != null)
                                {
                                    Program.userManagedData.DeleteItem<FavoriteMailFolder>(x => x.IdOrName.Equals(picked.DisplayName, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            else
                            {
                                Program.userManagedData.AddItem(new FavoriteMailFolder(picked.DisplayName, picked.DisplayName));
                            }

                            Program.ui.WriteLine("Updated favorites.");
                        }
                    }
                },
                // -------------------------
                // 4) Max Emails To Process
                // -------------------------
                new Command{
                    Name = "max emails to summarize",
                    Description = () => $"Set the maximum number of emails to summarize in one go [currently: {Program.config.MailSettings.MaxEmailsToProcess}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current max emails to summarize: {Program.config.MailSettings.MaxEmailsToProcess}\n");
                        int max = 100;
                        Program.ui.Write($"Enter new maximum (1-{max}): ");
                        var input = Program.ui.ReadLine();
                        if (int.TryParse(input, out int newMax) && newMax >= 1 && newMax <= max)
                        {
                            Program.config.MailSettings.MaxEmailsToProcess = newMax;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Max emails to process updated to {newMax}.");
                            return Task.FromResult(Command.Result.Success);
                        }
                        Program.ui.WriteLine("Cancelled or invalid input.");
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },

                // -------------------------
                // 5) Lookback window
                // -------------------------
                new Command{
                    Name = "lookback window",
                    Description = () => $"Set the lookback window (in days) for fetching emails [currently: {Program.config.MailSettings.LookbackWindow}]",
                    Action = () =>
                    {
                        int max = 365;
                        Program.ui.Write($"Current lookback window (days): {Program.config.MailSettings.LookbackWindow}\n");
                        Program.ui.Write($"Enter new lookback window (1-{max}): ");
                        var input = Program.ui.ReadLine();
                        if (int.TryParse(input, out int newWindow) && newWindow >= 1 && newWindow <= max)
                        {
                            Program.config.MailSettings.LookbackWindow = newWindow;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Lookback window updated to {newWindow} days.");
                            return Task.FromResult(Command.Result.Success);
                        }
                        Program.ui.WriteLine("Cancelled or invalid input.");
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                // -------------------------
                // 6) Lookback count
                // -------------------------                
                new Command
                {
                    Name="lookback count",
                    Description = () => $"Set the lookback count for fetching emails [currently: {Program.config.MailSettings.LookbackCount}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current lookback count: {Program.config.MailSettings.LookbackCount}\n");
                        int max = 250;
                        Program.ui.Write($"Enter new lookback count (1-{max}): ");
                        var input = Program.ui.ReadLine();
                        if (int.TryParse(input, out int newCount) && newCount >= 1 && newCount <= max)
                        {
                            Program.config.MailSettings.LookbackCount = newCount;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Lookback count updated to {newCount}.");
                            return Task.FromResult(Command.Result.Success);
                        }
                        Program.ui.WriteLine("Cancelled or invalid input.");
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                }
            }
        };
    }
    
    // ===== Helpers =====
    private static async Task ReadEmailLoop(IMailProvider provider, IMailMessage msg)
    {
        // Build a compact, fixed header
        void BuildHeader(IMailMessage m)
        {
            if (null == m) throw new ArgumentNullException(nameof(m));
            var to = string.Join("; ", m.ToRecipients?.Select(r => r.EmailAddress) ?? Enumerable.Empty<string>());
            var dt = m?.ReceivedDateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "????-??-?? ??:??";
            var colColor = ConsoleColor.Green;
            var valColor = ConsoleColor.White;
            Program.ui.ForegroundColor = colColor;
            Program.ui.Write("From:  ");
            Program.ui.ForegroundColor = valColor;
            // Width is set to half the width of the console, truncated with ellipsis if too long, and minus 7 for padding
            var fromValue = String.Format("{0,-" + (Program.ui.Width / 2 - 7) + "}", m?.From?.EmailAddress ?? "(unknown)");
            Program.ui.Write(fromValue);
            Program.ui.ForegroundColor = colColor;
            Program.ui.Write("Date:  ");
            Program.ui.ForegroundColor = valColor;
            Program.ui.WriteLine(dt);
            Program.ui.ForegroundColor = colColor;
            Program.ui.Write("To:    ");
            Program.ui.ForegroundColor = valColor;
            Program.ui.WriteLine(to);

            if (m?.CcRecipients.Count > 0)
            {
                Program.ui.ForegroundColor = colColor;
                Program.ui.Write("Cc:    ");
                Program.ui.ForegroundColor = valColor;
                Program.ui.WriteLine(string.Join("; ", m?.CcRecipients?.Select(r => r.EmailAddress) ?? Enumerable.Empty<string>()));
            }

            Program.ui.ForegroundColor = colColor;
            Program.ui.Write("Subj:  ");
            Program.ui.ForegroundColor = valColor;
            Program.ui.WriteLine(m?.Subject ?? "(unknown)");
            Program.ui.ForegroundColor = ConsoleColor.DarkGray;
            Program.ui.WriteLine($@"{new string('─', Math.Max(60, Program.ui.Width - 1))}");
            Program.ui.ForegroundColor = ConsoleColor.Gray;
        }

        // Body (for now we use BodyPreview; swap in the full body when available)
        var bodyText = msg.BodyPreview ?? string.Empty;
        var bodyLines = bodyText.Split('\n');

        // Viewport scroll state applies ONLY to the body
        int topLine = 0;

        while (true)
        {
            Program.ui.Clear();

            // 1) Render fixed header (not part of the scrollable region)
            BuildHeader(msg);

            // 2) Render scrollable body region below the header
            int headerLines = Program.ui.CursorTop; // how many rows the header consumed
            int viewport = Math.Max(5, Program.ui.Height - headerLines) - 3; // leave room for footer line + hint
            for (int i = 0; i < viewport && topLine + i < bodyLines.Length; i++)
            {
                var ln = bodyLines[topLine + i];
                if (ln.Length > Program.ui.Width - 1) ln = ln.Substring(0, Program.ui.Width - 2) + "…";
                Program.ui.WriteLine(ln);
            }

            // Footer / controls hint
            Program.ui.ForegroundColor = ConsoleColor.DarkGray;
            Program.ui.WriteLine(new string('─', Math.Max(20, Program.ui.Width - 1)));
            Program.ui.ForegroundColor = ConsoleColor.Green;
            Program.ui.WriteLine("[↑/↓/PgUp/PgDn/Home/End to scroll]  [ESC: menu]");
            Program.ui.ForegroundColor = ConsoleColor.Gray;

            var key = Program.ui.ReadKey(true);
            if (key.Key == ConsoleKey.Escape)
            {
                var choices = new List<string> { "reply", "reply-all", "delete", "move" };
                var pick = Program.ui.RenderMenu("Email actions (ESC to cancel)\n" + new string('─', Math.Max(60, Program.ui.Width - 1)), choices);
                if (string.IsNullOrWhiteSpace(pick))
                {
                    return;
                }
                else if (pick.StartsWith("reply-all", StringComparison.OrdinalIgnoreCase) || pick.StartsWith("reply", StringComparison.OrdinalIgnoreCase))
                {
                    Program.ui.Write("Prompt to draft the reply: ");
                    var prompt = Program.ui.ReadLineWithHistory() ?? "";
                    var ctx = new Context(@"Draft a professional, concise email reply. Keep it short, specific, and kind. Output plain text only.");
                    ctx.AddUserMessage($"Original message subject: {msg.Subject}\n\nUser prompt for the reply:\n{prompt}");
                    var replyBody = await Engine.Provider!.PostChatAsync(ctx, 0.2f);

                    // Draft (reply vs reply-all)
                    var draft = pick.StartsWith("reply-all", StringComparison.OrdinalIgnoreCase)
                        ? await provider.DraftReplyAllAsync(msg, replyBody)
                        : await provider.DraftReplyAsync(msg, replyBody);

                    Program.ui.WriteLine($"Draft saved. Subject: {draft.Subject}");
                }
                else if (pick.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
                {
                    try { await provider.DeleteAsync(msg); Program.ui.WriteLine("Moved to Deleted Items."); }
                    catch (Exception ex) { Program.ui.WriteLine($"Delete failed: {ex.Message}"); }
                }
                else if (pick.StartsWith("move", StringComparison.OrdinalIgnoreCase))
                {
                    var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                    var destChoices = favs.Select(f => f.DisplayName).ToList();
                    var dest = Program.ui.RenderMenu("Move to which favorite? (ESC to cancel)\n" + new string('─', Math.Max(60, Program.ui.Width - 1)), destChoices);
                    if (!string.IsNullOrWhiteSpace(dest))
                    {
                        var target = await provider.GetFolderByIdOrNameAsync(dest);
                        if (target != null)
                        {
                            try { await provider.MoveAsync(msg, target); Program.ui.WriteLine($"Moved to {dest}."); }
                            catch (Exception ex) { Program.ui.WriteLine($"Move failed: {ex.Message}"); }
                        }
                    }
                    return;
                }
            }
            else if (key.Key == ConsoleKey.UpArrow) { if (topLine > 0) topLine--; }
            else if (key.Key == ConsoleKey.DownArrow) { if (topLine < Math.Max(0, bodyLines.Length - 1)) topLine++; }
            else if (key.Key == ConsoleKey.PageUp) { topLine = Math.Max(0, topLine - viewport); }
            else if (key.Key == ConsoleKey.PageDown) { topLine = Math.Min(Math.Max(0, bodyLines.Length - viewport), topLine + viewport); }
            else if (key.Key == ConsoleKey.Home) { topLine = 0; }
            else if (key.Key == ConsoleKey.End) { topLine = Math.Max(0, bodyLines.Length - viewport); }
        }
    }
}