using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager
{
    private static Command CreateChatCommands()
    {
        return new Command
        {
            Name = "chat",
            Description = () => "chat-related commands",
            SubCommands = new List<Command>
            {
                // Fork the current conversation into a brand-new thread (auto-named)
                new Command {
                    Name = "new",
                    Description = () => "Create a new thread (saves current thread, auto-naming it)",
                    Action = () =>
                    {
                        var forked = ChatManager.CreateNewThread();
                        Program.ui.WriteLine($"Created and switched to '{forked.Name}'.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name="switch", Description=()=>$"Switch active thread [current: {Program.config.ChatThreadSettings.ActiveThreadName ?? "none"}]",
                    Action = () =>
                    {
                        var items = Program.userManagedData.GetItems<ChatThread>()
                                .OrderByDescending(t => t.LastUsedUtc).ToList();

                        var chosen = Program.ui.RenderMenu("Switch to thread", items.Select(t => t.Name).ToList());
                        if (string.IsNullOrEmpty(chosen)) return Task.FromResult(Command.Result.Cancelled);
                        var target = items.First(x => x.Name.Equals(chosen, StringComparison.OrdinalIgnoreCase));

                        // Always save the current active thread if one exists
                        if (Program.config.ChatThreadSettings.ActiveThreadName is string currentName) {
                            var current = items.FirstOrDefault(x => x.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));
                            if (current != null) ChatManager.SaveActiveThread(current);
                        }

                        // Always load via ChatManager.LoadThread
                        ChatManager.LoadThread(target);

                        // Update config and persist
                        Program.config.ChatThreadSettings.ActiveThreadName = target.Name;
                        Config.Save(Program.config, Program.ConfigFilePath);

                        Program.ui.WriteLine($"Switched to '{target.Name}'.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "show", Description = () => "Show chat history",
                    Action = () =>
                    {
                        Program.ui.RenderChatHistory(Program.Context.Messages());
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "clear", Description = () => "Clear chat history",
                    Action = () =>
                    {
                        Program.Context.Clear();
                        Program.Context.AddSystemMessage(Program.config.SystemPrompt);
                        Program.ui.WriteLine("Chat history cleared.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "new chat name",
                    Description = () => $"Set default name for new chat threads [current: {Program.config.ChatThreadSettings.DefaultNewThreadName}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Set default new chat thread name", Program.config.ChatThreadSettings.DefaultNewThreadName);
                        form.AddString("Default new thread name")
                            .WithHelp("This is the name used for new chat threads.");

                        if (await Program.ui.ShowFormAsync(form))
                        {
                            Program.config.ChatThreadSettings.DefaultNewThreadName = (string)form.Model!; // the CLONE with changes
                            Config.Save(Program.config, Program.ConfigFilePath);
                            return Command.Result.Success;
                        }
                        return Command.Result.Cancelled;
                    }
                }
            }
        };
    }
}
