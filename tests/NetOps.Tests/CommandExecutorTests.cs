using Microsoft.Extensions.Logging.Abstractions;
using NetOps.Agent.Services;
using NetOps.Shared;
using Xunit;

namespace NetOps.Tests;

public class CommandExecutorTests
{
    [Fact]
    public async Task ExecuteCommandStreamsOutputAndCompletionCallback()
    {
        var executor = new CommandExecutor(NullLogger<CommandExecutor>.Instance);
        var outputs = new List<CommandOutput>();
        var gate = new object();

        var request = new CommandRequest
        {
            CommandId = "cmd-test-1",
            CommandText = "echo netops-signalr-ok",
            TimeoutSeconds = 15
        };

        await executor.ExecuteCommandAsync(
            request,
            output =>
            {
                lock (gate)
                {
                    outputs.Add(output);
                }
                return Task.CompletedTask;
            },
            "agent-test-1");

        Assert.Contains(outputs, o =>
            !o.IsCompleted
            && !o.IsError
            && o.AgentId == "agent-test-1"
            && o.CommandId == request.CommandId
            && o.OutputLine.Contains("netops-signalr-ok", StringComparison.Ordinal));

        var completion = Assert.Single(outputs, o => o.IsCompleted);
        Assert.Equal(0, completion.ExitCode);
        Assert.False(completion.IsError);
        Assert.Equal("agent-test-1", completion.AgentId);
        Assert.Equal(request.CommandId, completion.CommandId);
    }
}
