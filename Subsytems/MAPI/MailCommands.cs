using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public static class MailCommands
{
    public static Command Commands(MapiMailClient mail)
    {
        return new Command
        {
            Name = "Mail",
            Description = () => "Mail (MAPI)",
            SubCommands = new List<Command>
            {
                new Command {
                    Name = "list folders",
                    Description = () => "List folders (top-two levels)",
                    Action = async () => {
                        var provider = Program.SubsystemManager.Get<MapiMailClient>() as IMailProvider;
                        var folders = await provider!.ListFoldersAsync(null, 200);
                        foreach (var f in folders)
                        {
                            Console.WriteLine($"- {f.DisplayName} ({f.TotalItemCount}/{f.UnreadItemCount} unread)");
                            var subfolders = await provider.ListFoldersAsync(f.DisplayName, 50);
                            foreach (var sf in subfolders)
                            {
                                Console.WriteLine($"  - {sf.DisplayName} ({sf.TotalItemCount}/{sf.UnreadItemCount} unread)");
                            }
                        }
                        return Command.Result.Success;
                    }
                }
            }
        };
    }
}
