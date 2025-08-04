using System;
using System.Collections.Generic;
using System.Linq;

namespace cschat
{
	public static class ColorizedConsoleLogger
	{
		private static readonly Dictionary<Log.Level, (ConsoleColor Color, string Label)> LevelInfo = new()
		{
			[Log.Level.Verbose] = (ConsoleColor.Gray, "VERB"),
			[Log.Level.Information] = (ConsoleColor.Cyan, "INFO"),
			[Log.Level.Warning] = (ConsoleColor.Yellow, "WARN"),
			[Log.Level.Error] = (ConsoleColor.Red, "ERRO")
		};

		private static readonly Dictionary<Log.Data, ConsoleColor> PropertyStyles = new()
		{
			[Log.Data.Timestamp] = ConsoleColor.Blue,
			[Log.Data.GitHash] = ConsoleColor.DarkGray,
			[Log.Data.Source] = ConsoleColor.DarkYellow,
			[Log.Data.Method] = ConsoleColor.Yellow,
			[Log.Data.Model] = ConsoleColor.Magenta,
			[Log.Data.Provider] = ConsoleColor.Magenta,
			[Log.Data.Count] = ConsoleColor.Green,
			[Log.Data.Progress] = ConsoleColor.Green,
			[Log.Data.ErrorCode] = ConsoleColor.Red,
			[Log.Data.Error] = ConsoleColor.Red,
			[Log.Data.Result] = ConsoleColor.DarkYellow,
			[Log.Data.Response] = ConsoleColor.DarkYellow
		};

		public static void WriteColorizedLog(Dictionary<Log.Data, object> data)
		{
			//var success = (bool)(data.GetValueOrDefault(Log.Data.Success) ?? false);

			// Write core log elements
			WriteOrderedProperties(data);
			//WriteColored(success ? ConsoleColor.Green : ConsoleColor.Red, success ? "✓" : "✗");
			//Console.Write(" | ");

			// Write message if present
			if (data.TryGetValue(Log.Data.Message, out var message))
			{
				WriteColoredMessage(message.ToString() ?? string.Empty);
				Console.Write(" | ");
			}

			// Write remaining properties
			WriteRemainingProperties(data);
			Console.WriteLine();

			// Write stack trace if present
			if (data.TryGetValue(Log.Data.Threw, out var stackTrace))
			{
				WriteColored(ConsoleColor.Red, $"Stack Trace: {stackTrace}");
				Console.WriteLine();
			}
		}

		private static void WriteOrderedProperties(Dictionary<Log.Data, object> data)
		{
			var orderedKeys = new[] { Log.Data.Timestamp, Log.Data.Success, Log.Data.Level, Log.Data.GitHash, Log.Data.Source, Log.Data.Method };

			foreach (var key in orderedKeys)
			{
				if (!data.TryGetValue(key, out var value)) continue;

				if (key == Log.Data.Level)
				{
					WriteColoredLevel((Log.Level)value);
				}
				else if (key == Log.Data.Success)
				{
					var success = (bool)(data.GetValueOrDefault(Log.Data.Success) ?? false);
					WriteColored(success ? ConsoleColor.Green : ConsoleColor.Red, success ? "✓" : "✗");
				}
				else if (PropertyStyles.TryGetValue(key, out var style))
				{
					WriteColored(style, value.ToString() ?? string.Empty);
				}
				Console.Write(" | ");
			}
		}

		private static void WriteRemainingProperties(Dictionary<Log.Data, object> data)
		{
			var skipProperties = new HashSet<Log.Data>
			{
				Log.Data.Timestamp, Log.Data.Level, Log.Data.GitHash, Log.Data.Source,
				Log.Data.Method, Log.Data.Success, Log.Data.Message, Log.Data.Threw
			};

			foreach (var kv in data.Where(x => !skipProperties.Contains(x.Key)))
			{
				WriteColored(ConsoleColor.Cyan, $"{kv.Key}:");

				if (PropertyStyles.TryGetValue(kv.Key, out var style))
				{
					WriteColored(style, kv.Value?.ToString() ?? "null");
				}
				else
				{
					WriteColored(GetValueTypeColor(kv.Value?.ToString() ?? "null"), kv.Value?.ToString() ?? "null");
				}
				Console.Write(" | ");
			}
		}

		private static void WriteColoredLevel(Log.Level level)
		{
			var (color, label) = LevelInfo[level];
			var originalColors = (Console.ForegroundColor, Console.BackgroundColor);

			Console.ForegroundColor = ConsoleColor.Black;
			Console.BackgroundColor = color;
			Console.Write($" {label} ");

			(Console.ForegroundColor, Console.BackgroundColor) = originalColors;
		}

		private static void WriteColoredMessage(string message)
		{
			var color = message.ToLower() switch
			{
				var m when m.Contains("successfully") || m.Contains("completed") => ConsoleColor.Green,
				var m when m.Contains("failed") || m.Contains("error") => ConsoleColor.Red,
				var m when m.Contains("warning") || m.Contains("retry") => ConsoleColor.Yellow,
				_ => ConsoleColor.DarkYellow
			};
			WriteColored(color, message);
		}

		private static ConsoleColor GetValueTypeColor(string value) => value.ToLower() switch
		{
			"true" => ConsoleColor.Green,
			"false" => ConsoleColor.Red,
			var v when v.StartsWith('"') && v.EndsWith('"') => ConsoleColor.DarkYellow,
			var v when bool.TryParse(v, out _) => ConsoleColor.Blue,
			var v when int.TryParse(v, out _) || double.TryParse(v, out _) => ConsoleColor.Green,
			var v when v.StartsWith('{') || v.StartsWith('[') => ConsoleColor.DarkCyan,
			_ => ConsoleColor.Gray
		};

		private static void WriteColored(ConsoleColor color, string text)
		{
			var original = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.Write(text);
			Console.ForegroundColor = original;
		}
	}
}