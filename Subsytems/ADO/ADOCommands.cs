
public static class ADOCommands
{
    public static Command Commands()
    {
        return new Command
        {
            Name = "ADO",
            Description = () => "Azure DevOps commands",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "workitem", Description = () => "Work item management commands",
                    SubCommands = new List<Command>
                    {
                        new Command
                        {
                            Name = "get", Description = () => "Get work item by ID",
                            Action = async () =>
                            {
                                var adoClient = Program.SubsystemManager.Get<AdoClient>();
                                Console.Write("Enter work item ID: ");
                                if (int.TryParse(Console.ReadLine(), out var id))
                                {
                                    var workItem = await Program.SubsystemManager.Get<AdoClient>().GetWorkItemSummaryById(id);
                                    Console.WriteLine(workItem);
                                    return Command.Result.Success;
                                }
                                Console.WriteLine("Invalid ID.");
                                return Command.Result.Cancelled;
                            }
                        },
                        new Command
                        {
                            Name = "get by query id", Description = () => "Query work items by query ID",
                            Action = async () =>
                            {
                                Console.Write("Enter query ID (GUID): ");
                                if (Guid.TryParse(Console.ReadLine(), out var queryId))
                                {
                                    var results = await Program.SubsystemManager.Get<AdoClient>().GetWorkItemSummariesByQueryId(queryId);
                                    foreach (var item in results)
                                    {
                                        Console.WriteLine(item);
                                    }
                                    return Command.Result.Success;
                                }
                                Console.WriteLine("Invalid query ID.");
                                return Command.Result.Cancelled;
                            }
                        }
                    }
                }
            }
        };
    }
}