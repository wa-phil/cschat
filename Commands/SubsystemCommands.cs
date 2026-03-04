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
            Name = "Subsystem", Description = () => "Enable/disable subsystem state",    
            Action = () => Log.MethodAsync(async ctx =>
            {
                ctx.OnlyEmitOnFailure();
                // use menu to select subsystem
                var subsystems = Program.SubsystemManager.Names.ToList();
                if (!subsystems.Any())
                {
                    ctx.Append(Log.Data.Message, "No subsystems available to toggle.");
                    return Command.Result.Success;
                }

                var selected = await Program.ui.RenderMenuAsync("select subsystem to toggle",subsystems.Select(s => $"{s} : [{(Program.SubsystemManager.IsEnabled(s)?"enabled":"disabled")}]").ToList());
                if (string.IsNullOrWhiteSpace(selected))
                {
                    ctx.Append(Log.Data.Message, "No subsystem selected.");
                    ctx.Succeeded();
                    return Command.Result.Cancelled;
                }
                var name = selected.Split(':').FirstOrDefault()?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    ctx.Append(Log.Data.Message, "Invalid subsystem name.");
                    return Command.Result.Cancelled;
                }
                Program.SubsystemManager.SetEnabled(name, !Program.SubsystemManager.IsEnabled(name));
                Config.Save(Program.config, Program.ConfigFilePath);
                ctx.Append(Log.Data.Message, $"Subsystem '{name}' is now {(Program.SubsystemManager.IsEnabled(name) ? "enabled" : "disabled")}.");
                ctx.Succeeded();
                return Command.Result.Success;
            })
        };
    }
}
 