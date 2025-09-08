using System;
using System.Threading.Tasks;
using System.Collections.Generic;

public static class MailCommands
{
    public static Command Commands(MailClient mail)
    {
        return new Command
        {
            Name = "Mail",
            Description = () => "Mail (Microsoft Graph)",
            SubCommands = new List<Command>
            {
                new Command {
                    Name = "folders",
                    Description = () => "List mail folders",
                    Action = async () => {
                        var resp = await ToolRegistry.InvokeToolAsync("tool.mail.list_folders", new ListMailFoldersInput{ ParentId = null, Top = 50 });
                        Console.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "since",
                    Description = () => "List messages in a folder since N minutes",
                    Action = async () => {
                        Console.Write("Folder [Inbox]: "); var f = (User.ReadLineWithHistory() ?? "").Trim(); if (f==string.Empty) f="Inbox";
                        Console.Write("Lookback minutes [60]: "); var t = User.ReadLineWithHistory(); int.TryParse(t, out var mins); if (mins<=0) mins=60;
                        var resp = await ToolRegistry.InvokeToolAsync("tool.mail.list_since", new ListMailSinceInput{ Folder = f, Minutes = mins, Top = 50 });
                        Console.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "read",
                    Description = () => "Read a message by id",
                    Action = async () => {
                        Console.Write("Message Id: "); var id = User.ReadLineWithHistory() ?? "";
                        var resp = await ToolRegistry.InvokeToolAsync("tool.mail.read", new ReadMailInput{ MessageId = id });
                        Console.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "summarize",
                    Description = () => "Summarize a message by id",
                    Action = async () => {
                        Console.Write("Message Id: "); var id = User.ReadLineWithHistory() ?? "";
                        var resp = await ToolRegistry.InvokeToolAsync("tool.mail.summarize", new SummarizeMailInput{ MessageId = id });
                        Console.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "send",
                    Description = () => "Send an email",
                    Action = async () => {
                        Console.Write("To: "); var to = User.ReadLineWithHistory() ?? "";
                        Console.Write("Subject: "); var sub = User.ReadLineWithHistory() ?? "";
                        Console.WriteLine("Body (blank line to finish):");
                        var lines = new List<string>(); while (true){var ln = Console.ReadLine(); if (string.IsNullOrEmpty(ln)) break; lines.Add(ln);}
                        Console.Write("HTML body? (y/N): "); var html = ((User.ReadLineWithHistory() ?? "").Trim().ToLowerInvariant()) is "y" or "yes";
                        var resp = await ToolRegistry.InvokeToolAsync("tool.mail.send", new SendMailInput{ To = to, Subject = sub, Body = string.Join("\n", lines), Html = html });
                        Console.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "reply",
                    Description = () => "Reply to a message",
                    Action = async () => {
                        Console.Write("Message Id: "); var id = User.ReadLineWithHistory() ?? "";
                        Console.WriteLine("Reply body (blank line to finish):");
                        var lines = new List<string>(); while (true){var ln = Console.ReadLine(); if (string.IsNullOrEmpty(ln)) break; lines.Add(ln);}
                        Console.Write("Reply all? (y/N): "); var all = ((User.ReadLineWithHistory() ?? "").Trim().ToLowerInvariant()) is "y" or "yes";
                        var resp = await ToolRegistry.InvokeToolAsync("tool.mail.reply", new ReplyMailInput{ MessageId = id, Body = string.Join("\n", lines), ReplyAll = all });
                        Console.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "move",
                    Description = () => "Move a message to a folder (e.g., DeletedItems)",
                    Action = async () => {
                        Console.Write("Message Id: "); var id = User.ReadLineWithHistory() ?? "";
                        Console.Write("Destination [DeletedItems]: "); var dest = (User.ReadLineWithHistory() ?? "").Trim(); if (dest=="") dest="DeletedItems";
                        var resp = await ToolRegistry.InvokeToolAsync("tool.mail.move", new MoveMailInput{ MessageId = id, Destination = dest });
                        Console.WriteLine(resp);
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name = "triage",
                    Description = () => "Apply EmailTriage rules to a folder",
                    Action = async () => {
                        Console.Write("Folder [Inbox]: "); var f = (User.ReadLineWithHistory() ?? "").Trim(); if (f==string.Empty) f="Inbox";
                        Console.Write("Lookback minutes [240]: "); var t = User.ReadLineWithHistory(); int.TryParse(t, out var mins); if (mins<=0) mins=240;
                        Console.Write("UMD Config Name [Default]: "); var name = (User.ReadLineWithHistory() ?? "").Trim(); if (name=="") name="Default";
                        Console.Write("Move spam to JunkEmail? (y/N): "); var mv = ((User.ReadLineWithHistory() ?? "").Trim().ToLowerInvariant()) is "y" or "yes";
                        var resp = await ToolRegistry.InvokeToolAsync("tool.mail.triage_folder", new TriageFolderInput { Folder = f, Minutes = mins, ConfigName = name, MoveSpam = mv });
                        Console.WriteLine(resp);
                        return Command.Result.Success;
                    }
                }
            }
        };
    }
}
