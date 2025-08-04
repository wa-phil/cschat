using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager
{
    private static Command CreateSystemCommands()
    {
        return new Command
        {
            Name = "system", Description = () => "System commands",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "log", Description = () => "Logging commands",
                    SubCommands = new List<Command>
                    {
                        new Command
                        {
                            Name = "show", Description = () => "Show the contents of the log",
                            Action = () =>
                            {
                                var entries = Log.GetOutput().ToList();
                                Console.WriteLine($"Log Entries [{entries.Count}]:");
                                entries.ToList().ForEach(entry => Console.WriteLine(entry));
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        new Command
                        {
                            Name = "clear", Description = () => "Clear the log entries",
                            Action = () =>
                            {
                                Log.ClearOutput();
                                Console.WriteLine("Log cleared.");
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        new Command
                        {
                            Name = "save", Description = () => "Save the contents of the log to a file",
                            Action = () =>
                            {
                                Console.Write("Enter file path to save the log: ");
                                var filePath = User.ReadLineWithHistory();
                                if (!string.IsNullOrWhiteSpace(filePath))
                                {
                                    try
                                    {
                                        var logEntries = Log.GetOutput();
                                        System.IO.File.WriteAllLines(filePath, logEntries);
                                        Console.WriteLine($"Log saved to '{filePath}'.");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Failed to save log: {ex.Message}");
                                    }
                                }
                                return Task.FromResult(Command.Result.Success);
                            }
                        }
                    }
                },
                new Command
                {
                    Name = "clear", Description = () => "Clear the console screen",
                    Action = () =>
                    {
                        Console.Clear();
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command {
                    Name = "config", Description = () => "Configuration commands",
                    SubCommands = new List<Command>
                    {
                        new Command
                        {
                            Name = "show", Description = () => "Show current system configuration",
                            Action = () =>
                            {
                                Console.WriteLine("Current Configuration:");
                                Console.WriteLine(Program.config.ToJson());
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        new Command
                        {
                            Name = "save", Description = () => "Save the current configuration",
                            Action = () =>
                            {
                                Config.Save(Program.config, Program.ConfigFilePath);
                                Console.WriteLine("Configuration saved.");
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        new Command
                        {
                            Name = "factory reset", Description = () => "Delete the current configuration and reset everything to defaults",
                            Action = async () =>
                            {
                                File.Delete(Program.ConfigFilePath);
                                Program.config = new Config(); // Reset to default config
                                await Program.InitProgramAsync();
                                Console.WriteLine("Configuration reset to default.");
                                return Command.Result.Success;
                            }
                        },
                        new Command
                        {
                            Name = "max menu items", Description = () => $"Configure maximum number of menu items displayed at once [currently: {Program.config.MaxMenuItems}]",
                            Action = () =>
                            {
                                Console.WriteLine($"Current max menu items: {Program.config.MaxMenuItems}");
                                Console.Write("Enter new value (minimum 1): ");
                                var input = User.ReadLineWithHistory();
                                if (int.TryParse(input, out int value) && value >= 1)
                                {
                                    Program.config.MaxMenuItems = value;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Console.WriteLine($"Max menu items set to {value}");
                                }
                                else
                                {
                                    Console.WriteLine("Invalid input. Please enter a number >= 1.");
                                }
                                return Task.FromResult(Command.Result.Success);
                            }
                        },
                        CreateProviderCommands(),
                        CreateRagConfigCommands(),
                        CreateRagFileTypeCommands(),
                        CreateMcpCommands()
                    }
                }
            }
        };
    }
}
