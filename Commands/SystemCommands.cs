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
                            Program.ui.WriteLine("Log cleared.");
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "save", Description = () => "Save the contents of the log to a file",
                        Action = () =>
                        {
                            Program.ui.Write("Enter file path to save the log: ");
                            var filePath = Program.ui.ReadLineWithHistory();
                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                try
                                {
                                    var logEntries = Log.GetOutput();
                                    System.IO.File.WriteAllLines(filePath, logEntries);
                                    Program.ui.WriteLine($"Log saved to '{filePath}'.");
                                }
                                catch (Exception ex)
                                {
                                    Program.ui.WriteLine($"Failed to save log: {ex.Message}");
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
                    Program.ui.Clear();
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
                            Program.ui.WriteLine("Current Configuration:");
                            Program.ui.WriteLine(Program.config.ToJson());
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "save", Description = () => "Save the current configuration",
                        Action = () =>
                        {
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine("Configuration saved.");
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
                            Program.ui.WriteLine("Configuration reset to default.");
                            return Command.Result.Success;
                        }
                    },
                    new Command
                    {
                        Name = "current working directory", Description = () => $"Show the current working directory [currently: {Environment.CurrentDirectory}]",
                        Action = async () =>
                        {
                            Program.ui.WriteLine($"Current working directory: {Environment.CurrentDirectory}");
                            Program.ui.Write("Enter new working directory (or leave blank to keep current): ");
                            var input = await Program.ui.ReadPathWithAutocompleteAsync(true);
                            if (!string.IsNullOrWhiteSpace(input) && Directory.Exists(input))
                            {
                                try
                                {
                                    Program.config.DefaultDirectory = input;
                                    Environment.CurrentDirectory = Program.config.DefaultDirectory;
                                    Config.Save(Program.config, Program.ConfigFilePath);
                                    Program.ui.WriteLine($"Working directory changed to: {Environment.CurrentDirectory}");
                                    return Command.Result.Success;
                                }
                                catch (Exception ex)
                                {
                                    Program.ui.WriteLine($"Failed to change directory: {ex.Message}");
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
                                    Program.ui.WriteLine("Current Chat Thread Settings:");
                                    Program.ui.WriteLine(Program.config.ChatThreadSettings.ToJson());
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "set root directory", Description = () => $"Set the root directory for chat threads [currently: {Program.config.ChatThreadSettings.RootDirectory}]",
                                Action = async () =>
                                {
                                    Program.ui.Write("Enter new root directory for chat threads (note '.threads' always appended): ");
                                    var input = await Program.ui.ReadPathWithAutocompleteAsync(false);
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
                                            Program.ui.WriteLine($"Chat thread root directory set to: {input}");
                                            return Command.Result.Success;
                                        }
                                        catch (Exception ex)
                                        {
                                            Program.ui.WriteLine($"Failed to set root directory: {ex.Message}");
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
                        Action = async () =>
                        {
                            var form = UiForm.Create("Configure menu items", Program.config);
                form.AddInt<Config>("Max menu items", c => c.MaxMenuItems, (c,v) => c.MaxMenuItems = v)
                                    .IntBounds(min: 1, max: 200)
                                    .WithHelp("Controls how many choices are rendered at once, range is 1 to 200.");
                            if (await Program.ui.ShowFormAsync(form))
                            {
                                Program.config = (Config)form.Model!;        // commit the edited clone
                                Config.Save(Program.config, Program.ConfigFilePath);
                                return Command.Result.Success;
                            }
                            return Command.Result.Cancelled;
                        }
                    },
                    new Command
                    {
                        Name = "set max steps", Description = () => $"Set maximum steps for planning [currently: {Program.config.MaxSteps}]",
                        Action = async () =>
                        {
                            var form = UiForm.Create("Configure maximum steps", Program.config);
                form.AddInt<Config>("Max steps", c => c.MaxSteps, (c,v) => c.MaxSteps = v)
                                    .IntBounds(min: 1, max: 100)
                                    .WithHelp("Controls how many steps the planner can take, range is 1 to 100.");
                            if (await Program.ui.ShowFormAsync(form))
                            {
                                Program.config = (Config)form.Model!;        // commit the edited clone
                                Config.Save(Program.config, Program.ConfigFilePath);
                                return Command.Result.Success;
                            }
                            return Command.Result.Cancelled;
                        }
                    }                    
                }
            }
        };

        // Add provider/rag/tool/subsystem commands
        subCommands.Add(CreateProviderCommands());
        subCommands.Add(CreateRagConfigCommands());
        subCommands.Add(CreateADOConfigCommands());
        subCommands.Add(CreateSubsystemCommands());

        // Final items
        subCommands.Add(CreateToolsCommands());
        subCommands.Add(new Command
        {
            Name = "about", Description = () => "Show information about Console# Chat",
            Action = () =>
            {
                Program.ui.WriteLine($"Console# Chat v{BuildInfo.GitVersion} ({BuildInfo.GitCommitHash})");
                Program.ui.WriteLine("A console-based chat application with RAG capabilities.");
                Program.ui.WriteLine("For more information, visit: https://github.com/wa-phil/cschat");
                return Task.FromResult(Command.Result.Success);
            }
        });

        return new Command { Name = "system", Description = () => "System commands", SubCommands = subCommands };
    }
}
