using System;
using System.IO;
using System.Collections.Generic;

public static class ChatManager
{
    private static readonly string ChatFileName = "chat.json";
    public static IDisposable? Sub { get; private set; }

    public static void Initialize(UserManagedData umd)
    {
        Sub = umd.Subscribe(typeof(ChatThread), (type, change, item) =>
        {
            var t = item as ChatThread;
            if (t == null) return;

            switch (change)
            {
                case UserManagedData.ChangeType.Deleted:
                    OnThreadDeleted(t);
                    break;
                case UserManagedData.ChangeType.Added:
                case UserManagedData.ChangeType.Updated:
                    // optional: maintain indexes, etc.
                    break;
            }
        });
    }

    static void OnThreadDeleted(ChatThread t) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Input, t.Name);
        // 1) Delete storage on disk
        TryDeleteThreadFiles(t);

        // 2) If it was the active thread, create/switch to a fresh one
        if (Program.config.ChatThreadSettings.ActiveThreadName?.Equals(t.Name, StringComparison.OrdinalIgnoreCase) == true)
        {
            var replacement = new ChatThread { Name = Program.config.ChatThreadSettings.DefaultNewThreadName };
            Program.userManagedData.AddItem(replacement);
            SwitchTo(replacement);
        }
        Config.Save(Program.config, Program.ConfigFilePath);
        ctx.Succeeded();
    });

    public static void SwitchTo(ChatThread t) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Input, t.Name);
        Directory.CreateDirectory(ThreadPath(t));
        Program.Context.Clear();
        var fp = Path.Combine(ThreadPath(t), ChatFileName);
        if (File.Exists(fp))
        {
            Program.Context.Load(fp);
            Program.config.ChatThreadSettings.ActiveThreadName = t.Name;
            Config.Save(Program.config, Program.ConfigFilePath);
            Program.Context.Save(fp);
            ctx.Succeeded();
            return;
        }
    });

    static string ThreadPath(ChatThread t)
        => Path.Combine(Program.config.ChatThreadSettings.RootDirectory, t.Name);

    static void TryDeleteThreadFiles(ChatThread t) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Input, t.Name);
        var dir = ThreadPath(t);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
        ctx.Succeeded();
    });
    
    // Ensure the forked name doesn’t collide with existing threads
    private static string EnsureUniqueThreadName(string desired)
    {
        desired = string.IsNullOrWhiteSpace(desired)
            ? Program.config.ChatThreadSettings.DefaultNewThreadName
            : desired;

        var existing = new HashSet<string>(
            Program.userManagedData.GetItems<ChatThread>().Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(desired)) return desired;

        int i = 2;
        string candidate;
        do { candidate = $"{desired} ({i++})"; } while (existing.Contains(candidate));
        return candidate;
    }

    public static ChatThread CreateNewThread()
    {
        var items = Program.userManagedData.GetItems<ChatThread>().ToList();
        var currentName = Program.config.ChatThreadSettings.ActiveThreadName;
        if (!string.IsNullOrWhiteSpace(currentName))
        {
            var current = items.FirstOrDefault(t => t.Name.Equals(currentName, StringComparison.OrdinalIgnoreCase));
            if (current != null) ChatManager.SaveActiveThread(current);
        }

        if (0 == Program.Context.Messages().Count())
        {
            // no current context, just create a blank new thread
            var blank = new ChatThread { Name = EnsureUniqueThreadName(Program.config.ChatThreadSettings.DefaultNewThreadName) };
            Program.userManagedData.AddItem(blank);
            Program.config.ChatThreadSettings.ActiveThreadName = blank.Name;
            Config.Save(Program.config, Program.ConfigFilePath);
            return blank;
        }

        // 1) Propose name/description from current context
        var proposed = NameAndDescribeThread(Program.Context);
        proposed.Name = EnsureUniqueThreadName(string.IsNullOrWhiteSpace(proposed.Name)
            ? Program.config.ChatThreadSettings.DefaultNewThreadName
            : proposed.Name);

        // 2) Materialize the fork by saving the *current* context into the new thread's file
        var dir = ThreadPath(proposed);
        Directory.CreateDirectory(dir);
        var fp = Path.Combine(dir, ChatFileName);
        Program.Context.Save(fp);

        // 3) Persist the new thread metadata
        Program.userManagedData.AddItem(proposed);


        // 4) create new thread and switch to it
        var newThread = new ChatThread { Name = Program.config.ChatThreadSettings.DefaultNewThreadName };
        Program.Context = new Context(Program.config.SystemPrompt);
        Program.userManagedData.AddItem(newThread);
        Program.config.ChatThreadSettings.ActiveThreadName = newThread.Name;
        Config.Save(Program.config, Program.ConfigFilePath);

        return newThread;
    }

    public static ChatThread NameAndDescribeThread(Context current) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        var existingNames = String.Join(", ",
            Program.userManagedData.GetItems<ChatThread>()
                .OrderByDescending(t => t.LastUsedUtc)
                .Select(t => t.Name)
                .ToList());

        var prompt = @"
You are an AI assistant that helps users manage chat threads. 
Given the recent conversation, suggest a concise and unique name for the chat thread, along with an optional brief description of its purpose or topic.
Avoid using generic names like 'Chat' or 'Discussion'.
Ensure the name is meaningful yet distinct from existing thread names: " + existingNames;
        var working = current.Clone();
        working.SetSystemMessage(prompt);
        var response = TypeParser.GetAsync(working, typeof(ChatThread))?.Result;
        ctx.Succeeded(null != response);
        var result = response as ChatThread ?? new ChatThread { Name = Program.config.ChatThreadSettings.DefaultNewThreadName };
        ctx.Append(Log.Data.Result, result.Name);
        return result;
    });

    public static void SaveActiveThread(ChatThread t) => Log.Method(ctx =>
    {
        if (string.IsNullOrEmpty(t.Name))
        {
            // ask the LLM to come up with a name and description of the thread
            t = NameAndDescribeThread(Program.Context);
            Program.userManagedData.AddItem(t); // save it
        }
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Input, t.Name);
        var dir = ThreadPath(t);
        Directory.CreateDirectory(dir);
        var fp = Path.Combine(dir, ChatFileName);
        Program.Context.Save(fp);
        ctx.Succeeded();
    });

    public static void LoadThread(ChatThread t) => Log.Method(ctx =>
    {
        ctx.OnlyEmitOnFailure();
        ctx.Append(Log.Data.Input, t.Name);
        var dir = ThreadPath(t);
        Directory.CreateDirectory(dir);
        var fp = Path.Combine(dir, ChatFileName);
        Program.Context.Clear();
        if (File.Exists(fp))
        {
            Program.Context.Load(fp);
        }
        Program.Context.AddSystemMessage(Program.config.SystemPrompt);
        ctx.Succeeded();
    });
}
