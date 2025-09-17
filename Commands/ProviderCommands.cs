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
                            Program.ui.WriteLine($"Switched to provider '{Program.config.Provider}'");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "model", Description = () => $"List and select available models [currently: {Program.config.Model}]",
                    Action = async () =>
                    {
                        Program.ui.WriteLine($"Current model: {Program.config.Model}");
                        var selected = await Engine.SelectModelAsync();
                        if (selected != null)
                        {
                            Program.config.Model = selected;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Switched to model '{selected}'");
                        }
                        return Command.Result.Success;
                    }
                },
                new Command
                {
                    Name = "host", Description = () => $"Change Ollama host [currently: {Program.config.Host}]",
                    Action = () =>
                    {
                        Program.ui.Write("Enter new Ollama host: ");
                        var hostInput = Program.ui.ReadLineWithHistory();
                        if (!string.IsNullOrWhiteSpace(hostInput))
                        {
                            Program.config.Host = hostInput.Trim();
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Switched to host '{Program.config.Host}'");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "system", Description = () => "Change system prompt",
                    Action = () =>
                    {
                        Program.ui.WriteLine($"Current system prompt: {Program.config.SystemPrompt}");
                        Program.ui.Write("Enter new system prompt (or press enter to keep current): ");
                        var promptInput = Program.ui.ReadLineWithHistory();
                        if (!string.IsNullOrWhiteSpace(promptInput))
                        {
                            Program.config.SystemPrompt = promptInput.Trim();
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine("System prompt updated.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "temp", Description = () => $"Set response temperature [currently: {Program.config.Temperature}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current temperature: {Program.config.Temperature}. Enter new value (0.0 to 1.0): ");
                        var tempInput = Program.ui.ReadLineWithHistory();
                        if (float.TryParse(tempInput, out var temp) && temp >= 0.0f && temp <= 1.0f)
                        {
                            Program.config.Temperature = temp;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Temperature set to {temp}");
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid temperature value. Must be between 0.0 and 1.0.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "max tokens", Description = () => $"Set maximum tokens for response [currently: {Program.config.MaxTokens}]",
                    Action = () =>
                    {
                        Program.ui.Write($"Current max tokens: {Program.config.MaxTokens}. Enter new value (1 to 10000): ");
                        var tokensInput = Program.ui.ReadLineWithHistory();
                        if (int.TryParse(tokensInput, out var tokens) && tokens >= 1 && tokens <= 32000)
                        {
                            Program.config.MaxTokens = tokens;
                            Config.Save(Program.config, Program.ConfigFilePath);
                            Program.ui.WriteLine($"Max tokens set to {tokens}");
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid max tokens value. Must be between 1 and 32000.");
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "azure auth logging enabled",
                    Description = () => $"Enable or disable verbose Azure logging [currently: {(Program.config.VerboseEventLoggingEnabled? "Enabled" : "Disabled")}]",
                    Action = () =>
                    {
                        var options = new List<string> { "true", "false" };
                        var currentSetting = Program.config.VerboseEventLoggingEnabled ? "true" : "false";
                        var selected = Program.ui.RenderMenu("Enable Azure Authentication verbose logging:", options, options.IndexOf(currentSetting));

                        if (!string.IsNullOrWhiteSpace(selected) && bool.TryParse(selected, out var result))
                        {
                            Program.config.VerboseEventLoggingEnabled = result;
                            Program.ui.WriteLine($"Azure Auth verbose logging set to {result}.");
                            Config.Save(Program.config, Program.ConfigFilePath);
                        }
                        else
                        {
                            Program.ui.WriteLine("Invalid selection.");
                        }

                        return Task.FromResult(Command.Result.Success);
                    }
                },
                new Command
                {
                    Name = "toggle event sources",
                    Description = () => "Toggle event sources for verbose logging",
                    Action = () =>
                    {
                        Program.ui.WriteLine("Select an event source to toggle:");
                        var sources = Program.config.EventSources.Select(kvp => $"{kvp.Key} : {(kvp.Value ? "Enabled" : "Disabled")}").ToList();
                        var selected = Program.ui.RenderMenu("Event sources:", sources, -1);
                        if (selected != null)
                        {
                            selected = selected.Split(':')[0].Trim(); // Get the source name
                            if (Program.config.EventSources.TryGetValue(selected, out var enabled))
                            {
                                Program.config.EventSources[selected] = !enabled;
                                Program.ui.WriteLine($"Event source '{selected}' toggled to {(Program.config.EventSources[selected] ? "Enabled" : "Disabled")}");
                                Config.Save(Program.config, Program.ConfigFilePath);
                            }
                            else
                            {
                                Program.ui.WriteLine($"Event source '{selected}' not found.");
                                return Task.FromResult(Command.Result.Failed);
                            }
                        }
                        return Task.FromResult(Command.Result.Success);
                    }
                }
            }
        };
    }
}
