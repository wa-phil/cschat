using System;

public interface IUi
{
    // input

    Task<string?> ReadPathWithAutocompleteAsync(bool isDirectory);
    Task<string?> ReadInputWithFeaturesAsync(CommandManager commandManager);
    string? RenderMenu(string header, List<string> choices, int selected = 0);
    string? ReadLineWithHistory();
    string ReadLine();
    ConsoleKeyInfo ReadKey(bool intercept);

    // output
    void RenderChatMessage(ChatMessage message);
    void RenderChatHistory(IEnumerable<ChatMessage> messages);

    // lifecycle helpers
    void BeginUpdate();
    void EndUpdate();

    int CursorTop { get; }
    int CursorLeft { get; }
    int Width { get; }
    int Height { get; }
    bool CursorVisible { set; }
    bool KeyAvailable { get; }
    bool IsOutputRedirected { get; }
    void SetCursorPosition(int left, int top);
    ConsoleColor ForegroundColor { get; set; }
    ConsoleColor BackgroundColor { get; set; }
    void ResetColor();

    void Write(string text);
    void WriteLine(string? text = null);
    void Clear();

    // lets each UI decide how to run/pump itself
    Task RunAsync(Func<Task> appMain);
}