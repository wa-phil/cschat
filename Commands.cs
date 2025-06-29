using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class Command
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Func<Task> Action { get; set; }
}

public class CommandManager
{
    private readonly List<Command> _commands;

    public CommandManager(IEnumerable<Command> commands = null)
    {
        _commands = commands?.ToList() ?? new List<Command>();
    }

    public void Register(Command command)
    {
        _commands.Add(command);
    }

    public Command Find(string input)
    {
        return _commands.FirstOrDefault(c => $"/{c.Name}".Equals(input, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<Command> GetAll() => _commands;

    public IEnumerable<string> GetCompletions(string input)
    {
        return _commands
            .Select(c => $"/{c.Name}")
            .Where(cmd => cmd.StartsWith(input, StringComparison.OrdinalIgnoreCase));
    }

    public void ShowHelp()
    {
        Console.WriteLine("Commands:");
        foreach (var cmd in _commands)
        {
            Console.WriteLine($"  /{cmd.Name.PadRight(10)} - {cmd.Description}");
        }
    }
}
