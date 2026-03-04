using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager
{
    private class LogSaveModel { public string Path { get; set; } = string.Empty; }
    private class WorkingDirectoryModel { public string Directory { get; set; } = string.Empty; }
    private class ChatRootDirectoryModel { public string Root { get; set; } = string.Empty; }

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
                        Action = async () =>
                        {
                            await Log.GenerateTableAsync();
                            return Command.Result.Success;
                        }
                    },
                    new Command
                    {
                        Name = "clear", Description = () => "Clear the contents of the log",
                        Action = () =>
                        {
                            Log.ClearOutput();
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "save", Description = () => "Save the contents of the log to a file",
                        Action = () => 
                        {
                            var form = UiForm.Create("Save log", new LogSaveModel { Path = "log.txt" });
                            form.AddPath<LogSaveModel>("File path", m => m.Path, (m,v)=> m.Path = v)
                                .WithPathMode(PathPickerMode.SaveFile)
                                .WithHelp("Destination file to write log lines.");

                            var result = Program.ui.ShowFormAsync(form).ContinueWith(t => {
                                if (!t.Result) { return Command.Result.Cancelled; }
                                var path = ((LogSaveModel)form.Model!).Path;
                                if (string.IsNullOrWhiteSpace(path)) { return Command.Result.Cancelled; }
                                try
                                {
                                    var logEntries = Log.GetOutput();
                                    File.WriteAllLines(path, logEntries);
                                    return Command.Result.Success;
                                }
                                catch (Exception ex)
                                {
                                    Log.Method(ctx=>{
                                        ctx.Failed("Failed to save log", ex);
                                    });
                                    return Command.Result.Failed;
                                }
                            });
                            return result;
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
                            using var output = Program.ui.BeginRealtime("Current Configuration");
                            output.WriteLine(Program.config.ToJson());
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "save", Description = () => "Save the current configuration",
                        Action = () =>
                        {
                            Config.Save(Program.config, Program.ConfigFilePath);
                            return Task.FromResult(Command.Result.Success);
                        }
                    },
                    new Command
                    {
                        Name = "factory reset", Description = () => "Delete the current configuration and reset everything to defaults",
                        Action = async () =>
                        {
                            using var output = Program.ui.BeginRealtime("Resetting back to factory...");
                            File.Delete(Program.ConfigFilePath);
                            Program.config = new Config(); // Reset to default config
                            await Program.InitProgramAsync(output);
                            return Command.Result.Success;
                        }
                    },
                    new Command
                    {
                        Name = "current working directory", Description = () => $"Show the current working directory [currently: {Environment.CurrentDirectory}]",
                        Action = async () =>
                        {
                            // Use a UiForm to allow user to edit the working directory with path picker support
                            var model = new WorkingDirectoryModel { Directory = Program.config.DefaultDirectory ?? Environment.CurrentDirectory };
                            var form = UiForm.Create("Set Working Directory", model);
                            form.AddPath<WorkingDirectoryModel>("Working directory", m => m.Directory, (m,v)=> m.Directory = v)
                                .WithHelp("Select a folder to become the new current working directory.")
                                .WithPlaceholder(model.Directory)
                                .WithPathMode(PathPickerMode.OpenExisting)
                                .MakeOptional(); // allow cancelling with blank

                            if (await Program.ui.ShowFormAsync(form))
                            {
                                var updated = (WorkingDirectoryModel)form.Model!;
                                var chosen = updated.Directory?.Trim();
                                if (!string.IsNullOrWhiteSpace(chosen))
                                {
                                    if (!Directory.Exists(chosen))
                                    {
                                        Log.Method(ctx=> ctx.Append(Log.Data.Message, $"Directory does not exist: {chosen}"));
                                        return Command.Result.Cancelled;
                                    }
                                    try
                                    {
                                        Program.config.DefaultDirectory = chosen;
                                        Environment.CurrentDirectory = chosen;
                                        Config.Save(Program.config, Program.ConfigFilePath);
                                        return Command.Result.Success;
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Method(ctx=> ctx.Failed("Failed to change directory", ex));
                                        return Command.Result.Failed;
                                    }
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
                                    using var output = Program.ui.BeginRealtime("Chat Thread Settings");
                                    output.WriteLine(Program.config.ChatThreadSettings.ToJson());
                                    return Task.FromResult(Command.Result.Success);
                                }
                            },
                            new Command
                            {
                                Name = "set root directory", Description = () => $"Set the root directory for chat threads [currently: {Program.config.ChatThreadSettings.RootDirectory}]",
                                Action = async () =>
                                {
                                    var current = Program.config.ChatThreadSettings.RootDirectory;
                                    // Strip trailing .threads if present so user selects parent folder; we'll append after.
                                    string initialBase = current?.EndsWith(Path.DirectorySeparatorChar + ".threads") == true
                                        ? Path.GetDirectoryName(current) ?? Environment.CurrentDirectory
                                        : (current ?? Environment.CurrentDirectory);

                                    var model = new ChatRootDirectoryModel { Root = initialBase };
                                    var form = UiForm.Create("Set Chat Root Directory", model);
                                    form.AddPath<ChatRootDirectoryModel>("Base directory", m => m.Root, (m,v)=> m.Root = v)
                                        .WithHelp("Select the base folder where the '.threads' directory will be created/used.")
                                        .WithPlaceholder(initialBase)
                                        .WithPathMode(PathPickerMode.OpenExisting)
                                        .MakeOptional();

                                    if (await Program.ui.ShowFormAsync(form))
                                    {
                                        var updated = (ChatRootDirectoryModel)form.Model!;
                                        var chosen = updated.Root?.Trim();
                                        if (!string.IsNullOrWhiteSpace(chosen))
                                        {
                                            if (!Directory.Exists(chosen))
                                            {
                                                Log.Method(ctx=> ctx.Append(Log.Data.Message, $"Directory does not exist: {chosen}"));
                                                return Command.Result.Cancelled;
                                            }
                                            var full = Path.Combine(chosen, ".threads");
                                            try
                                            {
                                                if (!Directory.Exists(full)) Directory.CreateDirectory(full);
                                                Program.config.ChatThreadSettings.RootDirectory = full;
                                                Config.Save(Program.config, Program.ConfigFilePath);
                                                return Command.Result.Success;
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Method(ctx=> ctx.Failed("Failed to set chat thread root directory", ex));
                                                return Command.Result.Failed;
                                            }
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
            Action = () => Log.Method(ctx=>
            {
                ctx.OnlyEmitOnFailure();
                using var output = Program.ui.BeginRealtime("About CSChat");
                ctx.Append(Log.Data.Message, "Realtime output started");
                output.WriteLine($"CSChat v{BuildInfo.GitVersion} ({BuildInfo.GitCommitHash})");
                output.WriteLine("A chat application with RAG capabilities.");
                output.WriteLine("For more information, visit: https://github.com/wa-phil/cschat");
                ctx.Succeeded();
                return Task.FromResult(Command.Result.Success);
            })
        });

        return new Command { Name = "system", Description = () => "System commands", SubCommands = subCommands };
    }
}
