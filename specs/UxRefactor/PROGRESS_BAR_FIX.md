# Progress Bar Rendering - Visual Fill Implementation

## Problem

Progress updates were showing in the terminal, but the **progress bars** (colored background fill that moves left-to-right as percentage increases) were not visible. Only text was being rendered.

## Root Cause

In the Terminal `LayoutNode` implementation for `UiKind.Progress`, I was creating simple `TermLine` objects with just text and colors:

```csharp
lines.Add(new TermLine($"{indentStr}{composed}", fg, ConsoleColor.Black, TextAlign.Left));
```

This renders text with uniform foreground/background colors across the entire line. No visual "bar" effect.

## Solution: Use TermLine.Runs

The `TermLine` record supports an optional `Runs` property - a list of `TermRun` objects that allow **different colors for different segments** of the same line:

```csharp
public sealed record TermRun(string Text, ConsoleColor Foreground, ConsoleColor Background);

public sealed record TermLine(...)
{
    public IReadOnlyList<TermRun>? Runs { get; init; }
}
```

When `Runs` is set, the Terminal's `Apply()` method renders each run with its own colors, creating a **segmented multi-color line**.

## Implementation

### Progress Bar Formula

```csharp
var fillWidth = (int)Math.Round(Math.Clamp(r.percent, 0, 100) / 100.0 * contentWidth);
```

For a 50% complete item on an 80-column line:
- `fillWidth = 0.50 * 80 = 40 columns`

### Three Segments

Each progress row is divided into **three runs**:

1. **Indent** (if any): Black background
   ```csharp
   runs.Add(new TermRun(indentStr, fg, ConsoleColor.Black));
   ```

2. **Filled portion** (0 to fillWidth): Colored background (DarkGray, DarkRed for failed)
   ```csharp
   var seg1Text = composed.Substring(0, seg1Len);
   runs.Add(new TermRun(seg1Text, fg, barBack));
   ```

3. **Empty portion** (fillWidth to end): Black background
   ```csharp
   var seg2Text = composed.Substring(seg2Start);
   runs.Add(new TermRun(seg2Text, fg, ConsoleColor.Black));
   ```

### Visual Example

For a 60% complete item:

```
┌────────────────────────────────────────┐
│      Adding content to RAG             │
├────────────────────────────────────────┤
▶ file1.cs               60.0% (12/20)
└─────────────┬──────────────────────────┘
              ↑
              fillWidth = 24 columns
```

With Runs:
```
[▶ file1.cs        ]      60.0% (12/20)
 ↑                 ↑      ↑
 DarkGray BG       |      Black BG
 (filled 24 cols)  |      (empty rest)
                   fillWidth
```

### Color Coding

```csharp
var barBack = r.state switch
{
    ProgressState.Failed => ConsoleColor.DarkRed,    // Red bar for failures
    ProgressState.Canceled => ConsoleColor.DarkGray, // Gray bar
    ProgressState.Completed => ConsoleColor.DarkGray,// Gray bar
    ProgressState.Running => ConsoleColor.DarkGray,  // Gray bar (default)
    _ => ConsoleColor.DarkBlue,                      // Blue for queued
};
```

## Before vs After

### Before (No Runs)
```
▶ file1.cs               60.0% (12/20)
  ← All one color, no visual bar
```

### After (With Runs)
```
▶ file1.cs          ░░░░60.0% (12/20)
                    ↑
                    Bar moves as percent increases
```

Where `░` represents the colored background that fills from left to right.

## How Terminal.Apply() Renders Runs

From `TermDom.Apply()` method:

```csharp
if (edit.Line.Runs is { } runs)
{
    foreach (var r in runs)
    {
        Console.ForegroundColor = r.Foreground;
        Console.BackgroundColor = r.Background;
        Console.Write(r.Text);
    }
}
```

Each run is written with its own colors, creating the visual "fill" effect as the bar progresses.

## Testing

Progress bars should now:
1. ✅ Show colored background fill that grows left-to-right
2. ✅ Update every 100ms as items are processed
3. ✅ Use different colors for different states (DarkRed for failed, DarkGray for running)
4. ✅ Display glyphs, names, percentages, and step counts on top of the bar

Example during RAG ingest:
```
┌─────────────────────────────────────────────┐
│         Adding content to RAG               │
├─────────────────────────────────────────────┤
▶ file1.cs███████████░░░░░░   60.0% (12/20)
▶ file2.cs████░░░░░░░░░░░░░░   20.0% (4/20)
• file3.cs░░░░░░░░░░░░░░░░░░    0.0% (0/20)
in-flight: 2   queued: 1   completed: 0
ETA: 5s • Press ESC to cancel
```

Where `█` represents the filled (colored) background and `░` represents the empty (black) background.

## Files Changed

- **UX/Terminal.cs**: Updated `TermDom.LayoutNode` Progress rendering to use `TermRun` segments

## Related

- `TermLine` and `TermRun` defined at lines 2040-2049 in Terminal.cs
- `TermDom.Apply()` handles run rendering at lines ~770-840
- Old `Progress.DrawProgressRow()` showed original two-segment approach (lines 84-145)
