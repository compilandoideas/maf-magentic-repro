// Minimal, offline repro for Magentic (.NET). No model or network: the manager and the
// participant are ChatClientAgents backed by scripted IChatClients.
//
//   dotnet run -- limit     -> issue A: the round limit is reported as a normal workflow output
//   dotnet run -- speaker   -> issue B: an unknown next_speaker jumps to the final answer
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

string scenario = args.FirstOrDefault() ?? "limit";
string nextSpeaker = scenario == "speaker" ? "Ghost" : "Worker";

AIAgent manager = new ScriptedClient(last =>
    last.Contains("is_request_satisfied", StringComparison.Ordinal)
        // Progress ledger: never satisfied, always making progress, delegate to `nextSpeaker`.
        ? $$$"""
          {"is_request_satisfied":{"reason":"not yet","answer":false},
           "is_in_loop":{"reason":"no","answer":false},
           "is_progress_being_made":{"reason":"yes","answer":true},
           "next_speaker":{"reason":"only one","answer":"{{{nextSpeaker}}}"},
           "instruction_or_question":{"reason":"keep going","answer":"Do the next step."}}
          """
        : last.Contains("final answer", StringComparison.OrdinalIgnoreCase)
            ? "FINAL ANSWER (task was never completed)"
            : "Facts/plan: ask Worker to do the steps.").AsAIAgent(name: "Manager");

AIAgent worker = new ScriptedClient(_ => "Worker did one step.").AsAIAgent(name: "Worker", description: "Does steps.");

Workflow workflow = AgentWorkflowBuilder.CreateMagenticBuilderWith(manager)
    .AddParticipants(worker)
    .RequirePlanSignoff(false)
    .WithMaxRounds(2)
    .Build();

await using Run run = await InProcessExecution.RunAsync(workflow, new List<ChatMessage> { new(ChatRole.User, "Do a three-step task.") });

foreach (WorkflowEvent evt in run.OutgoingEvents)
{
    switch (evt)
    {
        case WorkflowWarningEvent warning:
            Console.WriteLine($"[warning] {warning.Data}");
            break;
        case WorkflowErrorEvent error:
            Console.WriteLine($"[error]   {error.Data}");
            break;
        case WorkflowOutputEvent output:
            string text = output.Data is IEnumerable<ChatMessage> messages
                ? string.Join(" | ", messages.Select(m => m.Text))
                : output.Data?.ToString() ?? "";
            Console.WriteLine($"[output]  from={output.ExecutorId} type={output.Data?.GetType().Name} tags=[{string.Join(",", output.Tags)}]: {text}");
            break;
    }
}

Console.WriteLine($"[status]  {await run.GetStatusAsync()}");

sealed class ScriptedClient(Func<string, string> reply) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply(messages.LastOrDefault()?.Text ?? ""))));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await this.GetResponseAsync(messages, options, cancellationToken);
        foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
