using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
public partial class CommandManager
{
    private static Command CreateProviderCommands()
    {
        return new Command
        {
            Name = "provider", Description = () => "provider related configuration settings",
            SubCommands = new List<Command>
            {
                new Command
                {
                    Name = "select", Description = () => $"Select the LLM Provider [currently: {Program.config.Provider}]",
                    Action = () =>
                    {
                        var providers = Program.Providers.Keys.ToList();
                        var selected = Program.ui.RenderMenu("Select a provider:", providers, providers.IndexOf(Program.config.Provider));
                        if (!string.IsNullOrWhiteSpace(selected) && !selected.Equals(Program.config.Provider, StringComparison.OrdinalIgnoreCase))
                        {
                            Engine.SetProvider(selected);
                            Config.Save(Program.config, Program.ConfigFilePath);
                            return Task.FromResult(Command.Result.Success);
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                },
                new Command
                {
                    Name = "model", Description = () => $"List and select available models [currently: {Program.config.Model}]",
                    Action = async () =>
                    {
                        var selected = await Engine.SelectModelAsync();
                        if (selected != null)
                        {
                            Program.config.Model = selected;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            return Command.Result.Success;
                        }
                        return Command.Result.Cancelled;
                    }
                },
                new Command
                {
                    Name = "host", Description = () => $"Change host [currently: {Program.config.Host}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Change host URL", Program.config);
                        form.AddString<Config>("Host", c => c.Host, (c,v) => c.Host = v)
                            .WithHelp("The base URL of the LLM provider host.");

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
                    Name = "system", Description = () => "Change system prompt",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Change System Prompt", Program.config);
                        form.AddString<Config>("System Prompt", c => c.SystemPrompt, (c,v) => c.SystemPrompt = v)
                            .WithHelp("The system prompt sets the behavior of the AI. Be clear and specific.");

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
                    Name = "temp", Description = () => $"Set response temperature [currently: {Program.config.Temperature}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Set Temperature", Program.config);
                        form.AddFloat<Config>("Temperature (0-1)", c => c.Temperature, (c,v) => c.Temperature = v)
                            .IntBounds(min: 0, max: 1)
                            .WithHelp("Controls randomness in responses. Use a floating-point value between 0 and 1, where 0 = most deterministic, 1 = very random.");

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
                    Name = "max tokens", Description = () => $"Set maximum tokens for response [currently: {Program.config.MaxTokens}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Set Max Tokens", Program.config);
                        form.AddInt<Config>("Max Tokens (1-32000)", c => c.MaxTokens, (c,v) => c.MaxTokens = v)
                            .IntBounds(min: 1, max: 32000)
                            .WithHelp("Sets the maximum number of tokens the model can generate in a response, range is 1 to 32000.");

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
                    Name = "azure auth logging enabled",
                    Description = () => $"Enable or disable verbose Azure logging [currently: {(Program.config.VerboseEventLoggingEnabled? "Enabled" : "Disabled")}]",
                    Action = async () =>
                    {
                        var form = UiForm.Create("Azure Auth Verbose Logging", Program.config);
                        form.AddBool<Config>("Enable verbose logging", c => c.VerboseEventLoggingEnabled, (c,v) => c.VerboseEventLoggingEnabled = v)
                            .WithHelp("Enables or disables verbose logging for Azure authentication.");

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
                    Name = "toggle event sources",
                    Description = () => "Toggle event sources for verbose logging",
                    Action = () =>
                    {
                        var sources = Program.config.EventSources.Select(kvp => $"{kvp.Key} : {(kvp.Value ? "Enabled" : "Disabled")}").ToList();
                        var selected = Program.ui.RenderMenu("Select an event source to toggle:", sources, -1);
                        if (selected != null)
                        {
                            selected = selected.Split(':')[0].Trim(); // Get the source name
                            if (Program.config.EventSources.TryGetValue(selected, out var enabled))
                            {
                                Program.config.EventSources[selected] = !enabled;
                                Config.Save(Program.config, Program.ConfigFilePath);
                                return Task.FromResult(Command.Result.Success);
                            }
                        }
                        return Task.FromResult(Command.Result.Cancelled);
                    }
                }
            }
        };
    }
}
