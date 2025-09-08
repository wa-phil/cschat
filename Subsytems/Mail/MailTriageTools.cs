using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Graph;

public sealed class TriageFolderInput
{
    [UserField(required:true, display:"Source Folder")] public string Folder { get; set; } = "Inbox";
    [UserField(display:"Lookback (minutes)")] public int Minutes { get; set; } = 240;
    [UserField(display:"Config Name")] public string ConfigName { get; set; } = "Default";
    [UserField(display:"Move spam to JunkEmail")] public bool MoveSpam { get; set; } = false;
}

[IsConfigurable("tool.mail.triage_folder")]
public sealed class TriageFolderTool : ITool
{
    private readonly MailClient _mail;
    public TriageFolderTool(MailClient mail) => _mail = mail;
    public string Description => "Apply EmailTriage rules to a folder (route topics, flag or move spam).";
    public string Usage => "TriageFolder({ \"Folder\":\"Inbox\", \"Minutes\":240, \"ConfigName\":\"Default\" })";
    public Type InputType => typeof(TriageFolderInput);
    public string InputSchema => "{\"type\":\"object\",\"properties\":{\"Folder\":{\"type\":\"string\"},\"Minutes\":{\"type\":\"integer\"},\"ConfigName\":{\"type\":\"string\"},\"MoveSpam\":{\"type\":\"boolean\"}},\"required\":[\"Folder\"]}";

    public async Task<ToolResult> InvokeAsync(object input, Context ctx)
    {
        var p = (TriageFolderInput)input;
        var cfg = Program.userManagedData.GetItems<EmailTriageConfig>()
                   .FirstOrDefault(c => string.Equals(c.Name, p.ConfigName, StringComparison.OrdinalIgnoreCase))
                 ?? new EmailTriageConfig(); // safe default
        var msgs = await _mail.ListMessagesSinceAsync(p.Folder, TimeSpan.FromMinutes(p.Minutes), top: 200);

        int routed = 0, spam = 0;
        var lines = new List<string>();
        foreach (var m in msgs)
        {
            var from = m.From?.EmailAddress?.Address ?? "";
            var subj = m.Subject ?? "";
            var text = (m.BodyPreview ?? "").ToLowerInvariant();

            // 1) Topic routing
            var matched = cfg.Topics.FirstOrDefault(rule =>
            {
                bool kw = rule.Keywords.Any() && rule.Keywords.Any(k => subj.Contains(k, StringComparison.OrdinalIgnoreCase) || text.Contains(k.ToLowerInvariant()));
                bool dom = rule.FromDomains.Count == 0 || rule.FromDomains.Any(d => @from.EndsWith("@" + d, StringComparison.OrdinalIgnoreCase));
                return kw && dom;
            });

            if (matched != null && !string.IsNullOrWhiteSpace(matched.DestinationFolder))
            {
                await _mail.MoveAsync(m.Id!, matched.DestinationFolder);
                routed++;
                lines.Add($"[Routed:{matched.Topic}] {subj} — {from}");
                continue;
            }

            // 2) Spam heuristic (external + wrong-name)
            if (LooksLikeSpam(cfg, from, subj, text))
            {
                spam++;
                var msg = $"[Spam?] {subj} — {from}";
                if (p.MoveSpam)
                {
                    try { await _mail.MoveAsync(m.Id!, "JunkEmail"); msg += " → JunkEmail"; }
                    catch { /* ignore */ }
                }
                lines.Add(msg);
            }
        }

        var summary = $"Triage complete. Routed:{routed}  Spam:{spam}\n" + string.Join("\n", lines.Take(100));
        ctx.AddToolMessage(summary);
        return ToolResult.Success(summary, ctx);
    }

    private static bool LooksLikeSpam(EmailTriageConfig cfg, string from, string subj, string text)
    {
        bool external = !string.IsNullOrWhiteSpace(cfg.OrgDomain) &&
                        !from.EndsWith("@" + cfg.OrgDomain, StringComparison.OrdinalIgnoreCase);

        // Names starting with 'P' but not in MyNames (Phil/Phillip/Philip) — crude but useful
        var nameCandidates = Regex.Matches(subj + " " + text, @"\bP[a-zA-Z]{2,}\b")
                                  .Select(m => m.Value.TrimEnd(',', '.', '!', '?')).ToList();
        bool wrongName = nameCandidates.Any(n => !cfg.MyNames.Any(mn => n.Equals(mn, StringComparison.OrdinalIgnoreCase)));

        // Also treat “RE:” / “FWD:” with unrelated subjects from external senders as suspicious (lightweight)
        bool thready = subj.StartsWith("re:", StringComparison.OrdinalIgnoreCase) || subj.StartsWith("fwd:", StringComparison.OrdinalIgnoreCase);

        return external && (wrongName || thready);
    }
}
