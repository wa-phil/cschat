using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class CommandManager
{
    private static Command CreateSubsystemCommands()
    {
        return new Command
        {
            Name = "subsystem", Description = () => "Subsystem management commands",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "list", Description = () => "List all available subsystems",
                    Action = () =>
                    {
                        var subsystems = Program.SubsystemManager.Names.ToList();
                        if (!subsystems.Any())
                        {
                            Program.ui.WriteLine("No subsystems available.");
                            return Task.FromResult(Command.Result.Success);
                        }
                        Program.ui.WriteLine("Available subsystems:");
                        foreach (var subsystem in subsystems)
                        {
                            Program.ui.WriteLine($"- {subsystem} : Type:{Program.SubsystemManager.GetType(subsystem).ToString()} [{(Program.SubsystemManager.IsEnabled(subsystem) ? "enabled" : "disabled")}]");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "enable/disable", Description = () => "Toggle a subsystem's enabled/disabled state",
                    Action = () =>
                    {
                        // use menu to select subsystem
                        var subsystems = Program.SubsystemManager.Names.ToList();
                        if (!subsystems.Any())
                        {
                            Program.ui.WriteLine("No subsystems available to toggle.");
                            return Task.FromResult(Command.Result.Success);
                        }
                        Program.ui.WriteLine("Select a subsystem to toggle:");

                        var selected = Program.ui.RenderMenu("select subsystem to toggle",subsystems.Select(s => $"{s} : [{(Program.SubsystemManager.IsEnabled(s)?"enabled":"disabled")}]").ToList());
                        if (string.IsNullOrWhiteSpace(selected))
                        {
                            Program.ui.WriteLine("No subsystem selected.");
                            return Task.FromResult(Command.Result.Cancelled);
                        }
                        var name = selected.Split(':').FirstOrDefault()?.Trim();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            Program.ui.WriteLine("Invalid subsystem name.");
                            return Task.FromResult(Command.Result.Cancelled);
                        }
                        Program.SubsystemManager.SetEnabled(name, !Program.SubsystemManager.IsEnabled(name));
                        Config.Save(Program.config, Program.ConfigFilePath);
                        Program.ui.WriteLine($"Subsystem '{name}' is now {(Program.SubsystemManager.IsEnabled(name) ? "enabled" : "disabled")}.");
                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}
 