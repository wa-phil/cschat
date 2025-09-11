using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

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
                // 1) Favorites (picker)
                // -------------------------
                new Command {
                    Name = "favorites",
                    Description = () => "Browse folders and manage favorites",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        if (provider is null) { Console.WriteLine("Mail provider unavailable."); return Command.Result.Failed; }

                        // Top-level stores
                        var roots = await provider.ListFoldersAsync(null, 200);
                        if (roots is null || roots.Count == 0) { Console.WriteLine("(no folders)"); return Command.Result.Success; }

                        // Let user choose a root (store), then browse one level down, add/remove favorites
                        while (true)
                        {
                            var rootChoices = roots.Select(r => $"{r.DisplayName} ({r.TotalItemCount}/{r.UnreadItemCount} unread)").ToList();
                            var rootPick = User.RenderMenu("Select a root folder (ESC to exit)\n" + new string('─', Math.Max(60, Console.WindowWidth-1)), rootChoices);
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

                            var subPick = User.RenderMenu("Toggle favorite with ENTER; ESC to go back\n" + new string('─', Math.Max(60, Console.WindowWidth-1)), subChoices);
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

                            Console.WriteLine("Updated favorites.");
                        }
                    }
                },

                // -------------------------
                // 2) Simple text client
                // -------------------------
                new Command {
                    Name = "client",
                    Description = () => "Open a text mail client on a favorite folder",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        if (provider is null) { Console.WriteLine("Mail provider unavailable."); return Command.Result.Failed; }

                        var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                        if (favs.Count == 0)
                        {
                            Console.WriteLine("No favorites yet. Use 'Mail favorites' first.");
                            return Command.Result.Cancelled;
                        }

                        // Pick a favorite and N
                        var favChoices = favs.Select(f => f.DisplayName).ToList();
                        var favPick = User.RenderMenu("Select a favorite folder\n" + new string('─', Math.Max(60, Console.WindowWidth-1)), favChoices);
                        if (string.IsNullOrWhiteSpace(favPick)) return Command.Result.Cancelled;

                        Console.Write("Last N items (default 50): ");
                        var nText = Console.ReadLine();
                        int topN = 50;
                        if (!string.IsNullOrWhiteSpace(nText) && int.TryParse(nText, out var n) && n > 0) topN = n;

                        var folder = await provider.GetFolderByIdOrNameAsync(favPick);
                        if (folder is null) { Console.WriteLine("Folder not found."); return Command.Result.Failed; }

                        var msgs = await provider.ListMessagesSinceAsync(folder, TimeSpan.FromDays(3650), topN); // fetch by N (cap by huge window)
                        msgs = msgs.OrderByDescending(m => m.ReceivedDateTime).Take(topN).ToList();

                        // Menu rows: "From | Date | Subject | First Line"
                        List<string> ToRows(List<IMailMessage> items)
                        {
                            int consoleW = Console.WindowWidth;
                            string Row(IMailMessage m)
                            {
                                string from = m.From?.EmailAddress ?? "(unknown)";
                                string date = m.ReceivedDateTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                                string subj = Utilities.TruncatePlain(m.Subject ?? "", 40);
                                string first = Utilities.TruncatePlain((m.BodyPreview ?? "").Replace("\r"," ").Replace("\n"," "), Math.Max(10, consoleW - 70));
                                return $"{from} | {date} | {subj} | {first}";
                            }
                            return items.Select(Row).ToList();
                        }

                        var header = $"Folder: {favPick}\n" +
                                     "Use ↑/↓, ENTER to read; ESC to exit\n" +
                                     "From | Date | Subject | First Line\n" +
                                     new string('─', Math.Max(60, Console.WindowWidth-1));

                        while (true)
                        {
                            var selectedRow = User.RenderMenu(header, ToRows(msgs));
                            if (string.IsNullOrWhiteSpace(selectedRow)) return Command.Result.Cancelled;
                            var idx = ToRows(msgs).IndexOf(selectedRow);
                            if (idx < 0) continue;

                            var picked = msgs[idx];
                            var opened = await provider.GetMessageAsync(picked.Id);
                            if (opened is null) { Console.WriteLine("(message no longer available)"); continue; }

                            // Read mode with scroll + ESC menu
                            await ReadEmailLoop(provider, opened);
                        }
                    }
                },

                // -------------------------
                // 3) Summarize (topics)
                // -------------------------
                new Command {
                    Name = "summarize",
                    Description = () => "Summarize last N items in a favorite folder by configured topics (and Others)",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        if (provider is null) { Console.WriteLine("Mail provider unavailable."); return Command.Result.Failed; }

                        var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                        if (favs.Count == 0) { Console.WriteLine("No favorites yet. Use 'Mail favorites' first."); return Command.Result.Cancelled; }

                        var topics = Program.userManagedData.GetItems<MailTopic>();
                        if (topics.Count == 0)
                        {
                            Console.WriteLine("No topics configured. Add some with Data→add (type: MailTopic) or create a small helper command if you like.");
                        }

                        var favChoices = favs.Select(f => f.DisplayName).ToList();
                        var favPick = User.RenderMenu("Select a favorite folder\n" + new string('─', Math.Max(60, Console.WindowWidth-1)), favChoices);
                        if (string.IsNullOrWhiteSpace(favPick)) return Command.Result.Cancelled;

                        int topN = 25;
                        Console.Write($"Last N items (default {topN}): ");
                        var nText = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(nText) && int.TryParse(nText, out var n) && n > 0) topN = n;

                        var folder = await provider.GetFolderByIdOrNameAsync(favPick);
                        if (folder is null) { Console.WriteLine("Folder not found."); return Command.Result.Failed; }

                        var msgs = await provider.ListMessagesSinceAsync(folder, TimeSpan.FromDays(7), topN);
                        msgs = msgs.OrderByDescending(m => m.ReceivedDateTime).Take(topN).ToList();

                        // Build a blob and ask LLM to bucket by topics; then present summaries.
                        var topicBlock = string.Join("\n\n", topics.Select(t => $"- {t.Name}: {t.Description}\n  keywords: {t.Keywords}"));
                        var docs = string.Join("\n---\n", msgs.Select(m =>
                            $"From: {m.From?.EmailAddress}\nDate: {m.ReceivedDateTime.LocalDateTime:yyyy-MM-dd HH:mm}\nSubject: {m.Subject}\nPreview: {m.BodyPreview}"));

                        var prompt =
@"You are a mail analyst. Given topics (name/description/keywords) and a set of recent emails (header + first-line preview),
1) Assign each email to at most one topic; if none fits, label it 'Other'.
2) For each topic, produce a concise summary (1-2 short paragraphs) and 3-6 bullets with concrete action items or follow-ups.
3) After topics, create an 'Other' section for remaining emails (same format).
Be crisp and avoid duplication.";
                        var ctx = new Context(prompt);
                        ctx.AddUserMessage("Topics:\n" + topicBlock + "\n\nEmails:\n" + docs);

                        try
                        {
                            var summary = await Engine.Provider!.PostChatAsync(ctx, 0.2f);
                            Console.WriteLine("\n—— Summary ——");
                            Console.WriteLine(summary);
                            Console.WriteLine("—— End ——");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Summarization failed: {ex.Message}");
                        }

                        return Command.Result.Success;
                    }
                },

                // -------------------------
                // 4) Spam filter trainer
                // -------------------------
                new Command {
                    Name = "spam filter",
                    Description = () => "Run SPAM rules on last N items in a favorite folder; confirm/move and adapt rules",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        if (provider is null) { Console.WriteLine("Mail provider unavailable."); return Command.Result.Failed; }

                        var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                        if (favs.Count == 0) { Console.WriteLine("No favorites yet. Use 'Mail favorites' first."); return Command.Result.Cancelled; }

                        var favChoices = favs.Select(f => f.DisplayName).ToList();
                        var favPick = User.RenderMenu("Select a favorite folder\n" + new string('─', Math.Max(60, Console.WindowWidth-1)), favChoices);
                        if (string.IsNullOrWhiteSpace(favPick)) return Command.Result.Cancelled;

                        Console.Write("Last N items (default 80): ");
                        var nText = Console.ReadLine();
                        int topN = 80;
                        if (!string.IsNullOrWhiteSpace(nText) && int.TryParse(nText, out var n) && n > 0) topN = n;

                        var folder = await provider.GetFolderByIdOrNameAsync(favPick);
                        if (folder is null) { Console.WriteLine("Folder not found."); return Command.Result.Failed; }

                        var msgs = await provider.ListMessagesSinceAsync(folder, TimeSpan.FromDays(3650), topN);
                        msgs = msgs.OrderByDescending(m => m.ReceivedDateTime).Take(topN).ToList();

                        var rules = Program.userManagedData.GetItems<SpamRule>();

                        foreach (var m in msgs)
                        {
                            // Ask LLM to evaluate stochastic spam rules (and propose new rule if none match strongly)
                            var evalPrompt =
$@"You are a mail spam evaluator. You have a set of existing rules and one email.
Return a JSON object {{ ""isSpam"": bool, ""confidence"": 0..1, ""matchingRuleId"": ""guid or null"", ""proposedRule"": ""optional text"" }}.
Consider sender patterns, greeting errors (e.g. wrong name), solicitation cues, and common spam telltales.

Existing rules:
{string.Join("\n", rules.Select(r => $"- [{r.Id}] w={r.Weight:0.0}: {r.RuleText}"))}

Email:
From: {m.From?.EmailAddress}
Subject: {m.Subject}
Preview: {m.BodyPreview}";
                            var ctx = new Context(evalPrompt);
                            string json = "{}";
                            try { json = await Engine.Provider!.PostChatAsync(ctx, 0.0f); } catch {}

                            // naive extraction
                            bool isSpam = json.IndexOf("\"isSpam\": true", StringComparison.OrdinalIgnoreCase) >= 0;
                            string snippet = Utilities.TruncatePlain($"{m.From?.EmailAddress} | {m.Subject} | {m.BodyPreview}", Math.Max(20, Console.WindowWidth - 4));

                            Console.WriteLine();
                            Console.WriteLine($"? SPAM?  {snippet}");
                            Console.Write("[y/N/cancel]> ");
                            var resp = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

                            if (resp == "cancel" || resp == "c") return Command.Result.Cancelled;

                            if (resp == "y" || (resp == "" && isSpam))
                            {
                                // move to Deleted Items
                                var deleted = await provider.GetFolderByIdOrNameAsync("Deleted Items") ?? await provider.GetFolderByIdOrNameAsync("Trash");
                                if (deleted != null) await provider.MoveAsync(m, deleted);
                                else await provider.DeleteAsync(m);

                                // update rule stats (very rough heuristic)
                                var firstRule = rules.FirstOrDefault();
                                if (firstRule != null)
                                {
                                    firstRule.TruePositives += 1;
                                    Program.userManagedData.UpdateItem(firstRule, x => x.Id == firstRule.Id);
                                }
                            }
                            else
                            {
                                // not spam: update FP for first rule (rough; in practice parse matchingRuleId from json)
                                var firstRule = rules.FirstOrDefault();
                                if (firstRule != null)
                                {
                                    firstRule.FalsePositives += 1;
                                    Program.userManagedData.UpdateItem(firstRule, x => x.Id == firstRule.Id);
                                }
                            }
                        }

                        Console.WriteLine("\nDone.");
                        return Command.Result.Success;
                    }
                },

                // -------------------------
                // (Existing) list folders
                // -------------------------
                new Command {
                    Name = "list folders",
                    Description = () => "List folders (top-two levels)",
                    Action = async () =>
                    {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        var folders = await provider!.ListFoldersAsync(null, 200);
                        foreach (var f in folders)
                        {
                            Console.WriteLine($"- {f.DisplayName} ({f.TotalItemCount}/{f.UnreadItemCount} unread)");
                            var subfolders = await provider.ListFoldersAsync(f.DisplayName, 50);
                            foreach (var sf in subfolders)
                            {
                                Console.WriteLine($"  - {sf.DisplayName} ({sf.TotalItemCount}/{sf.UnreadItemCount} unread)");
                            }
                        }
                        return Command.Result.Success;
                    }
                }
            }
        };
    }

    // ===== Helpers =====

    private static async Task ReadEmailLoop(IMailProvider provider, IMailMessage msg)
    {
        string RenderBody(IMailMessage m)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"From: {m.From?.EmailAddress}");
            sb.AppendLine($"To:   {string.Join("; ", m.ToRecipients?.Select(r => r.EmailAddress) ?? Enumerable.Empty<string>())}");
            if (m.CcRecipients?.Count > 0) sb.AppendLine($"Cc:   {string.Join("; ", m.CcRecipients.Select(r => r.EmailAddress))}");
            sb.AppendLine($"Date: {m.ReceivedDateTime.LocalDateTime:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Subj: {m.Subject}");
            sb.AppendLine(new string('─', Math.Max(60, Console.WindowWidth-1)));
            // We only have BodyPreview here; if/when full body is available, substitute in.
            sb.AppendLine(m.BodyPreview ?? string.Empty);
            return sb.ToString();
        }

        int topLine = 0;
        var lines = (RenderBody(msg)).Split('\n');
        while (true)
        {
            Console.Clear();
            int height = Math.Max(5, Console.WindowHeight - 3);
            for (int i = 0; i < height && topLine + i < lines.Length; i++)
            {
                var ln = lines[topLine + i];
                if (ln.Length > Console.WindowWidth - 1) ln = ln.Substring(0, Console.WindowWidth - 2) + "…";
                Console.WriteLine(ln);
            }
            Console.WriteLine(new string('─', Math.Max(20, Console.WindowWidth-1)));
            Console.WriteLine("[↑/↓/PgUp/PgDn/Home/End to scroll]  [ESC: menu]");

            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape)
            {
                var choices = new List<string> { "done", "reply", "reply-all", "delete", "mark as spam", "move" };
                var pick = User.RenderMenu("Email actions (ESC to cancel)\n" + new string('─', Math.Max(60, Console.WindowWidth-1)), choices);
                if (string.IsNullOrWhiteSpace(pick))
                {
                    continue;
                }
                else if (pick.StartsWith("done", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                else if (pick.StartsWith("reply-all", StringComparison.OrdinalIgnoreCase) || pick.StartsWith("reply", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("Prompt to draft the reply: ");
                    var prompt = User.ReadLineWithHistory() ?? "";
                    var ctx = new Context(@"Draft a professional, concise email reply. Keep it short, specific, and kind. Output plain text only.");
                    var blob = $"ORIGINAL EMAIL\nFrom: {msg.From?.EmailAddress}\nSubj: {msg.Subject}\nPreview: {msg.BodyPreview}";
                    ctx.AddUserMessage($"User prompt: {prompt}\n\n{blob}");
                    try
                    {
                        var draftBody = await Engine.Provider!.PostChatAsync(ctx, 0.2f);
                        IMailMessage draft = pick.StartsWith("reply-all", StringComparison.OrdinalIgnoreCase)
                            ? await provider.DraftReplyAllAsync(msg, draftBody)
                            : await provider.DraftReplyAsync(msg, draftBody);
                        Console.WriteLine("Draft saved to Drafts.");
                    }
                    catch (Exception ex) { Console.WriteLine($"Drafting failed: {ex.Message}"); }
                }
                else if (pick.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
                {
                    try { await provider.DeleteAsync(msg); Console.WriteLine("Moved to Deleted Items."); }
                    catch (Exception ex) { Console.WriteLine($"Delete failed: {ex.Message}"); }
                }
                else if (pick.StartsWith("move", StringComparison.OrdinalIgnoreCase))
                {
                    var favs = Program.userManagedData.GetItems<FavoriteMailFolder>();
                    var destChoices = favs.Select(f => f.DisplayName).ToList();
                    var dest = User.RenderMenu("Move to which favorite? (ESC to cancel)\n" + new string('─', Math.Max(60, Console.WindowWidth - 1)), destChoices);
                    if (!string.IsNullOrWhiteSpace(dest))
                    {
                        var target = await provider.GetFolderByIdOrNameAsync(dest);
                        if (target != null)
                        {
                            try { await provider.MoveAsync(msg, target); Console.WriteLine($"Moved to {dest}."); }
                            catch (Exception ex) { Console.WriteLine($"Move failed: {ex.Message}"); }
                        }
                    }
                }
                else if (pick.StartsWith("mark as spam", StringComparison.OrdinalIgnoreCase))
                {
                    // Create/update a SpamRule using LLM proposal
                    var seed = $"From: {msg.From?.EmailAddress}\nSubject: {msg.Subject}\nPreview: {msg.BodyPreview}";
                    var ctx = new Context(
@"Given this email, propose a short rule text that would likely match similar spam in the future.
Focus on features like sender domain patterns, wrong salutation, solicitation language, etc.
Return only the rule text, one or two sentences.");
                    ctx.AddUserMessage(seed);
                    try
                    {
                        var ruleText = await Engine.Provider!.PostChatAsync(ctx, 0.0f);
                        var rule = new SpamRule { RuleText = ruleText?.Trim() ?? "(heuristic)", Weight = 1.0f };
                        Program.userManagedData.AddItem(rule);
                        Console.WriteLine("A spam rule was added to UserManagedData.");
                    }
                    catch (Exception ex) { Console.WriteLine($"Could not create spam rule: {ex.Message}"); }
                }
            }
            else if (key.Key == ConsoleKey.UpArrow)       { topLine = Math.Max(0, topLine - 1); }
            else if (key.Key == ConsoleKey.DownArrow)     { topLine = Math.Min(Math.Max(0, lines.Length - 1), topLine + 1); }
            else if (key.Key == ConsoleKey.PageUp)        { topLine = Math.Max(0, topLine - Math.Max(5, Console.WindowHeight - 4)); }
            else if (key.Key == ConsoleKey.PageDown)      { topLine = Math.Min(Math.Max(0, lines.Length - 1), topLine + Math.Max(5, Console.WindowHeight - 4)); }
            else if (key.Key == ConsoleKey.Home)          { topLine = 0; }
            else if (key.Key == ConsoleKey.End)           { topLine = Math.Max(0, lines.Length - 1); }
        }
    }
}
