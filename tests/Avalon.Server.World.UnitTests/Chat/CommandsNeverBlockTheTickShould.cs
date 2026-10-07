using Avalon.World.Chat;

namespace Avalon.Server.World.UnitTests.Chat;

/// <summary>
/// A command runs on the tick and must run to completion (spec 2026-09-30 section 5). `void Execute` makes
/// `await` impossible in the method itself; this catches the rest: an async helper, or a blocking wait.
/// </summary>
public class CommandsNeverBlockTheTickShould
{
    [Fact]
    public void Find_no_command_that_awaits_or_blocks()
    {
        List<Type> commands = typeof(ICommand).Assembly.GetTypes()
            .Where(t => typeof(ICommand).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false })
            .ToList();

        Assert.NotEmpty(commands);
        Assert.Empty(commands.SelectMany(TickBlockingScan.Violations));
    }

    [Fact]
    public void Catch_a_command_that_reads_a_task_result() =>
        Assert.NotEmpty(TickBlockingScan.Violations(typeof(ReadsResult)));

    [Fact]
    public void Catch_a_command_that_waits_on_a_task() =>
        Assert.NotEmpty(TickBlockingScan.Violations(typeof(Waits)));

    [Fact]
    public void Catch_a_command_that_blocks_on_an_awaiter() =>
        Assert.NotEmpty(TickBlockingScan.Violations(typeof(BlocksOnAwaiter)));

    [Fact]
    public void Catch_a_command_with_an_async_helper() =>
        Assert.NotEmpty(TickBlockingScan.Violations(typeof(HasAsyncHelper)));

    private sealed class ReadsResult : ICommand
    {
        public string Name => "x";
        public string[] Aliases => [];
        public void Execute(CommandContext ctx, string[] args) => _ = Task.FromResult(1).Result;
    }

    private sealed class Waits : ICommand
    {
        public string Name => "x";
        public string[] Aliases => [];
        public void Execute(CommandContext ctx, string[] args) => Task.CompletedTask.Wait();
    }

    private sealed class BlocksOnAwaiter : ICommand
    {
        public string Name => "x";
        public string[] Aliases => [];
        public void Execute(CommandContext ctx, string[] args) => Task.FromResult(1).GetAwaiter().GetResult();
    }

    private sealed class HasAsyncHelper : ICommand
    {
        public string Name => "x";
        public string[] Aliases => [];
        public void Execute(CommandContext ctx, string[] args) => _ = Helper();
        private static async Task Helper() => await Task.Yield();
    }
}
