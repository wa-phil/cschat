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
                        User.RenderChatHistory(Program.memory.Messages);
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
                        var filePath = User.ReadLineWithHistory();
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
                        var filePath = User.ReadLineWithHistory();
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
                },
                new Command
                {
                    Name = "set max steps", Description = "Set maximum steps for planning",
                    Action = () =>
                    {
                        Console.Write("Enter maximum steps (default 25): ");
                        var input = Console.ReadLine();
                        if (int.TryParse(input, out int maxSteps))
                        {
                            Program.config.MaxSteps = maxSteps;
                            Console.WriteLine($"Maximum steps set to {maxSteps}.");
                        }
                        else
                        {
                            Console.WriteLine($"Invalid input. Maximum steps remain at {Program.config.MaxSteps}.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}
