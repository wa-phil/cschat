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
            Description = "chat-related commands",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "show", Description = "Show chat history",
                    Action = () =>
                    {
                        Console.WriteLine("Chat History:");
                        foreach (var msg in Program.memory.Messages)
                        {
                            Console.WriteLine($"{msg.Role}: {msg.Content}");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "clear", Description = "Clear chat history",
                    Action = () =>
                    {
                        Program.memory.Clear();
                        Program.memory.AddSystemMessage(Program.config.SystemPrompt);
                        Console.WriteLine("Chat history cleared.");
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "load", Description = "Load chat history from a file",
                    Action = () =>
                    {
                        Console.Write("Enter file path to load chat history: ");
                        var filePath = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(filePath))
                        {
                            try
                            {
                                Program.memory.Load(filePath);
                                Console.WriteLine($"Chat history loaded from '{filePath}'.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to load chat history: {ex.Message}");
                            }
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "save", Description = "Save chat history to a file",
                    Action = () =>
                    {
                        Console.Write("Enter file path to save chat history: ");
                        var filePath = Console.ReadLine();
                        if (!string.IsNullOrWhiteSpace(filePath))
                        {
                            try
                            {
                                Program.memory.Save(filePath);
                                Console.WriteLine($"Chat history saved to '{filePath}'.");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to save chat history: {ex.Message}");
                            }
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}
