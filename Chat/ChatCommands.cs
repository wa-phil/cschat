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
                    Action = async () =>
                    {
                        var forked = ChatManager.CreateNewThread();
                        
                        // Create header with new thread name
                        var header = ChatSurface.CreateHeader(
                            threadName: forked.Name,
                            onClear: async (e) => 
                            {
                                Program.Context.Clear();
                                Program.Context.AddSystemMessage(Program.config.SystemPrompt);
                                await Program.ui.PatchAsync(ChatSurface.ClearMessages());
                            }
                        );
                        
                        // Create ChatSurface content for new thread
                        var chatContent = ChatSurface.Create(
                            Array.Empty<ChatMessage>(),
                            inputText: "",
                            onSend: null,
                            onInput: async (e) =>
                            {
                                var currentInputText = e.Value ?? "";
                                await Program.ui.PatchAsync(ChatSurface.UpdateInput(currentInputText));
                            }
                        );
                        
                        // Wrap in UiFrame and mount
                        var frame = new UiFrame(
                            Header: header,
                            Content: chatContent,
                            Overlays: Array.Empty<UiNode>()
                        );
                        var frameRoot = UiFrameBuilder.Create(frame);
                        await Program.ui.SetRootAsync(frameRoot, new UiControlOptions(TrapKeys: true, InitialFocusKey: "input"));
                        
                        using var output = Program.ui.BeginRealtime("Creating new thread...");
                        output.WriteLine($"Switched to '{forked.Name}'.");
                        return Command.Result.Success;
                    }
                },
                new Command {
                    Name="switch", Description=()=>$"Switch active thread [current: {Program.config.ChatThreadSettings.ActiveThreadName ?? "none"}]",
                    Action = async () =>
                    {
                        var items = Program.userManagedData.GetItems<ChatThread>()
                                .OrderByDescending(t => t.LastUsedUtc).ToList();

                        var chosen = Program.ui.RenderMenu("Switch to thread", items.Select(t => t.Name).ToList());
                        if (string.IsNullOrEmpty(chosen)) return Command.Result.Cancelled;
                        var target = items.First(x => x.Name.Equals(chosen, StringComparison.OrdinalIgnoreCase));

                        // Always save the current active thread if one exists
                        var currentName = Program.config.ChatThreadSettings.ActiveThreadName ?? "";
                        if (!string.IsNullOrEmpty(currentName)) {
                            var current = items.FirstOrDefault(x => x.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));
                            if (current != null) ChatManager.SaveActiveThread(current);
                        }

                        // Always load via ChatManager.LoadThread
                        ChatManager.LoadThread(target);

                        // Update config and persist
                        Program.config.ChatThreadSettings.ActiveThreadName = target.Name;
                        Config.Save(Program.config, Program.ConfigFilePath);
                        
                        // Create header with new thread name
                        var header = ChatSurface.CreateHeader(
                            threadName: target.Name,
                            onClear: async (e) => 
                            {
                                Program.Context.Clear();
                                Program.Context.AddSystemMessage(Program.config.SystemPrompt);
                                await Program.ui.PatchAsync(ChatSurface.ClearMessages());
                            }
                        );
                        
                        // Remount ChatSurface with new thread's messages
                        var messages = Program.Context.Messages(InluceSystemMessage: false).ToList();
                        var chatContent = ChatSurface.Create(
                            messages,
                            inputText: "",
                            onSend: null,
                            onInput: async (e) =>
                            {
                                var currentInputText = e.Value ?? "";
                                await Program.ui.PatchAsync(ChatSurface.UpdateInput(currentInputText));
                            }
                        );
                        
                        // Wrap in UiFrame and mount
                        var frame = new UiFrame(
                            Header: header,
                            Content: chatContent,
                            Overlays: Array.Empty<UiNode>()
                        );
                        var frameRoot = UiFrameBuilder.Create(frame);
                        await Program.ui.SetRootAsync(frameRoot, new UiControlOptions(TrapKeys: true, InitialFocusKey: "input"));
                        
                        using var output = Program.ui.BeginRealtime($"Switching from thread '{currentName}' to '{target.Name}'.");
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "show", Description = () => "Show chat history",
                    Action = async () =>
                    {
                        // Create header
                        var header = ChatSurface.CreateHeader(
                            threadName: Program.config.ChatThreadSettings.ActiveThreadName,
                            onClear: async (e) => 
                            {
                                Program.Context.Clear();
                                Program.Context.AddSystemMessage(Program.config.SystemPrompt);
                                await Program.ui.PatchAsync(ChatSurface.ClearMessages());
                            }
                        );
                        
                        // Remount ChatSurface to refresh display
                        var messages = Program.Context.Messages(InluceSystemMessage: false).ToList();
                        var chatContent = ChatSurface.Create(
                            messages,
                            inputText: "",
                            onSend: null,
                            onInput: async (e) =>
                            {
                                var currentInputText = e.Value ?? "";
                                await Program.ui.PatchAsync(ChatSurface.UpdateInput(currentInputText));
                            }
                        );
                        
                        // Wrap in UiFrame and mount
                        var frame = new UiFrame(
                            Header: header,
                            Content: chatContent,
                            Overlays: Array.Empty<UiNode>()
                        );
                        var frameRoot = UiFrameBuilder.Create(frame);
                        await Program.ui.SetRootAsync(frameRoot, new UiControlOptions(TrapKeys: true, InitialFocusKey: "input"));
                        
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "clear", Description = () => "Clear chat history",
                    Action = async () =>
                    {
                        Program.Context.Clear();
                        Program.Context.AddSystemMessage(Program.config.SystemPrompt);
                        
                        await Program.ui.PatchAsync(ChatSurface.ClearMessages());
                        
                        using var output = Program.ui.BeginRealtime("Clearing chat history...");
                        return Command.Result.Success;
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
