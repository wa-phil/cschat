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
                // 1) 3-pane mail client
                // -------------------------
                new Command {
                    Name = "client",
                    Description = () => $"Open a 3-pane mail client (sidebar | messages | reading pane)",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        if (provider is null)
                        {
                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("Mail provider unavailable.");
                            return Command.Result.Failed;
                        }

                        var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                        if (favs.Count == 0)
                        {
                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("No favorites yet. Use 'Mail favorites' first.");
                            return Command.Result.Cancelled;
                        }

                        await MailClientOverlay.ShowAsync(Program.ui, provider, favs);
                        return Command.Result.Success;
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
                        if (provider is null)
                        {
                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("Mail provider unavailable.");
                            return Command.Result.Failed;
                        }

                        var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                        if (favs.Count == 0)
                        {
                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("No favorites yet. Use 'Mail favorites' first.");
                            return Command.Result.Cancelled;
                        }

                        var topics = Program.userManagedData.GetItems<MailTopic>();
                        if (topics.Count == 0)
                        {
                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("No topics configured. Add some with Data→add (type: MailTopic).");
                        }

                        var favChoices = favs.Select(f => f.DisplayName).ToList();
                        var favPick = await Program.ui.RenderMenuAsync("Select a favorite folder\n" + new string('─', Math.Max(60, Program.ui.Width - 1)), favChoices);
                        if (string.IsNullOrWhiteSpace(favPick)) return Command.Result.Cancelled;

                        int topN = Program.config.MailSettings.MaxEmailsToProcess;
                        var folder = await provider.GetFolderByIdOrNameAsync(favPick);
                        if (folder is null)
                        {
                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("Folder not found.");
                            return Command.Result.Failed;
                        }

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
                        {
                            var msgList = msgs.ToList();
                            try
                            {
                                var indices = msgs.Select((m,i) => (m.Id, i)).ToDictionary(x => x.Id, x => x.i);

                                var (results, failures, canceled) = await AsyncProgress.For("Classifying emails")
                                    .WithCancellation(cts)
                                    .Run<IMailMessage, bool>(
                                        items: () => msgList,
                                        nameOf: m => $"{indices[m.Id] + 1}/{msgList.Count}  " + Utilities.TruncatePlain(m.Subject ?? "(no subject)", 60),
                                        processAsync: async (m, pi, ct) =>
                                        {
                                            pi.SetTotal(1);
                                            var ctx = new Context(classifyPrompt);
                                            ctx.AddUserMessage($"EMAIL\nfrom: {m.From?.EmailAddress}\nsubject: {m.Subject}\npreview: {m.BodyPreview}");

                                            try
                                            {
                                                var assignment = await TypeParser.GetAsync(ctx, typeof(MailAssignment)) as MailAssignment;
                                                assignments[m.Id] = String.IsNullOrWhiteSpace(assignment?.Topic) ? "Other" : assignment.Topic;
                                            }
                                            catch (OperationCanceledException)
                                            {
                                                assignments[m.Id] = "Other";
                                                throw;
                                            }
                                            catch (Exception)
                                            {
                                                assignments[m.Id] = "Other";
                                                throw;
                                            }

                                            pi.Advance(1, "done");
                                            return true;
                                        });
                            }
                            catch (Exception ex)
                            {
                                using var w = Program.ui.BeginRealtime("Mail");
                                w.WriteLine($"Error during classification: {ex.Message}");
                            }
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
                        {
                            var kvList = grouped.ToList();
                            try
                            {
                                var (results, failures, canceled) = await AsyncProgress.For("Summarizing topics")
                                    .WithCancellation(ctsSummary)
                                    .Run<KeyValuePair<string, List<IMailMessage>>, bool>(
                                        items: () => kvList,
                                        nameOf: kv => $"topic: {kv.Key} ({kv.Value.Count})",
                                        processAsync: async (kv, pi, ct) =>
                                        {
                                            pi.SetTotal(1);
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
                                            }
                                            catch (OperationCanceledException)
                                            {
                                                topicSummaries[key] = "ERROR: canceled";
                                                throw;
                                            }
                                            catch (Exception ex)
                                            {
                                                topicSummaries[key] = $"ERROR: {ex.Message}";
                                                throw;
                                            }

                                            pi.Advance(1, "done");
                                            return true;
                                        });
                            }
                            catch (Exception ex)
                            {
                                using var w = Program.ui.BeginRealtime("Mail");
                                w.WriteLine($"Error during topic summarization: {ex.Message}");
                            }
                        }

                        // 5) Interactive menus: categories -> subjects -> read
                        while (true)
                        {
                            var categoryChoices = grouped
                                .OrderByDescending(kv => kv.Value.Count)
                                .Select(kv => $"{kv.Key} ({kv.Value.Count})")
                                .ToList();

                            var catPick = await Program.ui.RenderMenuAsync(
                                "Select a category\n" + new string('─', Math.Max(60, Program.ui.Width - 1)),
                                categoryChoices);

                            if (string.IsNullOrWhiteSpace(catPick)) return Command.Result.Cancelled;

                            var pickedCat = grouped.Keys.First(k => catPick.StartsWith(k));
                            var header = $"== {pickedCat} ==\n{topicSummaries[pickedCat]}\n"+
                                new string('─', Math.Max(60, Program.ui.Width - 1))+
                                "Select an email (ESC to go back)\n"+
                                new string('─', Math.Max(60, Program.ui.Width - 1));

                            var list = grouped[pickedCat];
                            var subjectChoices = list.Select(m =>
                                $"{m.ReceivedDateTime.LocalDateTime:yyyy-MM-dd HH:mm} | {m.From?.EmailAddress} | {Utilities.TruncatePlain(m.Subject ?? string.Empty, 80)}")
                                .ToList();

                            var subjPick = await Program.ui.RenderMenuAsync(header, subjectChoices);
                            if (string.IsNullOrWhiteSpace(subjPick)) continue;

                            var idx = subjectChoices.IndexOf(subjPick);
                            if (idx >= 0)
                            {
                                var full = await provider.GetMessageAsync(list[idx].Id);
                                if (full != null) await MailClientOverlay.ShowMessageAsync(Program.ui, provider, full);
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
                        if (provider is null)
                        {
                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("Mail provider unavailable.");
                            return Command.Result.Failed;
                        }

                        // Top-level stores
                        var roots = await provider.ListFoldersAsync(null, 200);
                        if (roots is null || roots.Count == 0)
                        {
                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("(no folders)");
                            return Command.Result.Success;
                        }

                        // Let user choose a root (store), then browse one level down, add/remove favorites
                        while (true)
                        {
                            var rootChoices = roots.Select(r => $"{r.DisplayName} ({r.TotalItemCount}/{r.UnreadItemCount} unread)").ToList();
                            var rootPick = await Program.ui.RenderMenuAsync("Select a root folder (ESC to exit)\n" + new string('─', Math.Max(60, Program.ui.Width-1)), rootChoices);
                            if (string.IsNullOrWhiteSpace(rootPick)) return Command.Result.Cancelled;

                            var root = roots[rootChoices.IndexOf(rootPick)];

                            var subs = await provider.ListFoldersAsync(root.DisplayName, 500);
                            var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                            var favSet = new HashSet<string>(favs.Select(f => f.IdOrName), StringComparer.OrdinalIgnoreCase);

                            var subChoices = subs.Select(sf =>
                            {
                                var star = favSet.Contains(sf.DisplayName) ? "[*]" : "[ ]";
                                return $"{star} {sf.DisplayName} ({sf.TotalItemCount}/{sf.UnreadItemCount} unread)";
                            }).ToList();

                            var subPick = await Program.ui.RenderMenuAsync("Toggle favorite with ENTER; ESC to go back\n" + new string('─', Math.Max(60, Program.ui.Width-1)), subChoices);
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

                            using var w = Program.ui.BeginRealtime("Mail");
                            w.WriteLine("Updated favorites.");
                        }
                    }
                },
                // -------------------------
                // 4) Settings (combined form for MaxEmailsToProcess, LookbackWindow, LookbackCount)
                // -------------------------
                new Command{
                    Name = "settings",
                    Description = () => $"Edit mail settings (max summarize: {Program.config.MailSettings.MaxEmailsToProcess}, lookback days: {Program.config.MailSettings.LookbackWindow}, lookback count: {Program.config.MailSettings.LookbackCount})",
                    Action = async () =>
                    {
                        // Clone via UiForm.Create so edits are transactional until submit
                        var form = UiForm.Create("Mail Settings", Program.config.MailSettings);
                        form.AddInt<MailSettings>("Max Emails To Summarize", m => m.MaxEmailsToProcess, (m,v)=> m.MaxEmailsToProcess = v)
                            .IntBounds(1,100).WithHelp("Maximum emails summarized in one operation (1-100).");
                        form.AddInt<MailSettings>("Lookback Window (Days)", m => m.LookbackWindow, (m,v)=> m.LookbackWindow = v)
                            .IntBounds(1,365).WithHelp("Days to look back when fetching emails (1-365).");
                        form.AddInt<MailSettings>("Lookback Count", m => m.LookbackCount, (m,v)=> m.LookbackCount = v)
                            .IntBounds(1,250).WithHelp("Maximum emails fetched for browsing (1-250).");

                        if (!await Program.ui.ShowFormAsync(form)) { return Command.Result.Cancelled; }

                        // Persist edited clone
                        Program.config.MailSettings = (MailSettings)form.Model!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        return Command.Result.Success;
                    }
                }
            }
        };
    }
}
