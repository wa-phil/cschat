using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CSChat.Tests
{
    public class AsyncProgressTests
    {
        private sealed class TestInputRouter : IInputRouter
        {
            public ConsoleKeyInfo? TryReadKey() => null;
        }

        private sealed class TestUi : CUiBase
        {
            public override Task<bool> ConfirmAsync(string question, bool defaultAnswer = false) => Task.FromResult(defaultAnswer);
            public override Task<IReadOnlyList<string>> PickFilesAsync(FilePickerOptions opt) => Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());
            public override Task RenderTableAsync(Table table, string? title = null) => Task.CompletedTask;
            public override Task RenderReportAsync(Report report) => Task.CompletedTask;
            public override IInputRouter GetInputRouter() => new TestInputRouter();
            public override Task<ConsoleKeyInfo> ReadKeyAsync(bool intercept) => Task.FromResult(new ConsoleKeyInfo());
            public override int CursorTop => 0; public override int Width => 80; public override int Height => 25; public override bool KeyAvailable => false; public override bool IsOutputRedirected => true;
            public override void SetCursorPosition(int left, int top) { }
            private ConsoleColor _fg = ConsoleColor.Gray; private ConsoleColor _bg = ConsoleColor.Black;
            public override ConsoleColor ForegroundColor { get => _fg; set => _fg = value; }
            public override ConsoleColor BackgroundColor { get => _bg; set => _bg = value; }
            public override void ResetColor() { }
            public override void Write(string text) { }
            public override void WriteLine(string? text = null) { }
            public override void Clear() { }
            public override Task RunAsync(Func<Task> appMain) => appMain();
            protected override Task PostSetRootAsync(UiNode root, UiControlOptions options) => Task.CompletedTask;
            protected override Task PostPatchAsync(UiPatch patch) => Task.CompletedTask;
            protected override Task PostFocusAsync(string key) => Task.CompletedTask;
        }

        [Fact]
        public async Task Cancel_MidFlight_DoesNotThrow_AndReturnsCanceledTrue()
        {
            // Arrange minimal environment for AsyncProgress
            Program.config = new Config();
            Program.ui = new TestUi();

            var items = Enumerable.Range(1, 50).ToList();
            using var externalCts = new CancellationTokenSource();
            externalCts.CancelAfter(150); // cancel mid-flight

            // Act: run with cancellation; should NOT throw
            var builder = AsyncProgress.For("test-run")
                                       .WithCancellation(externalCts)
                                       .MaxConcurrency(4);

            var result = await builder.Run<int, int>(
                () => items,
                i => $"Item {i}",
                async (i, pi, ct) =>
                {
                    // Simulate some work honoring cancellation
                    pi.SetTotal(5);
                    for (int step = 0; step < 5; step++)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(20, ct);
                        pi.Advance(1);
                    }
                    return i;
                });

            var (results, failures, canceled) = result;

            // Assert: no exception escaped, canceled reported true
            Assert.True(canceled, "Expected run to report canceled=true");
            // Some items may have completed before cancel; ensure no invalid state
            Assert.InRange(results.Count + failures.Count, 0, items.Count);
            // Failures should be either canceled or actual errors; verify canceled present when canceled
            Assert.True(failures.Any() || results.Any());
        }
    }
}
