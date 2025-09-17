using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager
{
    private static Command CreateSystemCommands()
    {
        var subCommands = new List<Command>
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
                            Log.PrintColorizedOutput();
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "clear", Description = () => "Clear the contents of the log",
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
                        Name = "current working directory", Description = () => $"Show the current working directory [currently: {Environment.CurrentDirectory}]",
                        Action = async () =>
                        {
                            Console.WriteLine($"Current working directory: {Environment.CurrentDirectory}");
                            Console.Write("Enter new working directory (or leave blank to keep current): ");
                            var input = await User.ReadPathWithAutocompleteAsync(true);
                            if (!string.IsNullOrWhiteSpace(input) && Directory.Exists(input))
                            {
                                try
                                {
                                    Program.config.DefaultDirectory = input;
                                    Environment.CurrentDirectory = Program.config.DefaultDirectory;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Console.WriteLine($"Working directory changed to: {Environment.CurrentDirectory}");
                                    return Command.Result.Success;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to change directory: {ex.Message}");
                                    return Command.Result.Failed;
                                }
                            }
                            return Command.Result.Cancelled;
                        }
                    },
                    new Command
                    {
                        Name = "Chat thread settings", Description = () => "Configure chat thread settings",
                        SubCommands = new List<Command>
                        {
                            new Command
                            {
                                Name = "show", Description = () => "Show current chat thread settings",
                                Action = () =>
                                {
                                    Console.WriteLine("Current Chat Thread Settings:");
                                    Console.WriteLine(Program.config.ChatThreadSettings.ToJson());
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "set root directory", Description = () => $"Set the root directory for chat threads [currently: {Program.config.ChatThreadSettings.RootDirectory}]",
                                Action = async () =>
                                {
                                    Console.Write("Enter new root directory for chat threads (note '.threads' always appended): ");
                                    var input = await User.ReadPathWithAutocompleteAsync(false);
                                    if (!string.IsNullOrWhiteSpace(input))
                                    {
                                        input = Path.Combine(input, ".threads");
                                        try
                                        {
                                            if (!Directory.Exists(input))
                                            {
                                                Directory.CreateDirectory(input);
                                            }
                                            Program.config.ChatThreadSettings.RootDirectory = input;
                                            Config.Save(Program.config, Program.ConfigFilePath);
                                            Console.WriteLine($"Chat thread root directory set to: {input}");
                                            return Command.Result.Success;
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Failed to set root directory: {ex.Message}");
                                            return Command.Result.Failed;
                                        }
                                    }
                                    return Command.Result.Cancelled;
                                }
                            }
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
                    new Command
                    {
                        Name = "set max steps", Description = () => $"Set maximum steps for planning [currently: {Program.config.MaxSteps}]",
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
            }
        };

        // Add provider/rag/tool/subsystem commands
        subCommands.Add(CreateProviderCommands());
        subCommands.Add(CreateRagConfigCommands());
        subCommands.Add(CreateRagFileTypeCommands());
        subCommands.Add(CreateADOConfigCommands());
        subCommands.Add(CreateSubsystemCommands());

        // Final items
        subCommands.Add(CreateToolsCommands());
        subCommands.Add(new Command
        {
            Name = "about", Description = () => "Show information about Console# Chat",
            Action = () =>
            {
                Console.WriteLine($"Console# Chat v{BuildInfo.GitVersion} ({BuildInfo.GitCommitHash})");
                Console.WriteLine("A console-based chat application with RAG capabilities.");
                Console.WriteLine("For more information, visit: https://github.com/wa-phil/cschat");
                return Task.FromResult(Command.Result.Success);
            }
        });

        return new Command { Name = "system", Description = () => "System commands", SubCommands = subCommands };
    }
}
