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
                        Console.WriteLine($"Created and switched to '{forked.Name}'.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name="switch", Description=()=>$"Switch active thread [current: {Program.config.ChatThreadSettings.ActiveThreadName ?? "none"}]",
                    Action = () =>
                    {
                        var items = Program.userManagedData.GetItems<ChatThread>()
                                .OrderByDescending(t => t.LastUsedUtc).ToList();

                        var chosen = User.RenderMenu("Switch to thread", items.Select(t => t.Name).ToList());
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

                        Console.WriteLine($"Switched to '{target.Name}'.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "show", Description = () => "Show chat history",
                    Action = () =>
                    {
                        User.RenderChatHistory(Program.Context.Messages());
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
                        Console.WriteLine("Chat history cleared.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "default new chat name",
                    Description = () => $"Set default name for new chat threads (current: {Program.config.ChatThreadSettings.DefaultNewThreadName})",
                    Action = () =>
                    {
                        Console.Write("New default name> ");
                        var name = User.ReadLineWithHistory();
                        if (string.IsNullOrWhiteSpace(name)) return Task.FromResult(Command.Result.Cancelled);
                        Program.config.ChatThreadSettings.DefaultNewThreadName = name!;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Console.WriteLine($"Default new chat thread name set to '{name}'.");
                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}
