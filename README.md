# Magentic (.NET) repro for microsoft/agent-framework

Minimal, offline console app that reproduces two behaviors of the Magentic orchestration in
`Microsoft.Agents.AI.Workflows` 1.27.0. No model, no network, no keys: the manager and the
participant are `ChatClientAgent`s backed by a scripted `IChatClient` (see `Program.cs`).

Requires the .NET 10 SDK.

```sh
dotnet run -- limit     # issue A
dotnet run -- speaker   # issue B
```

## A. Hitting `MaxRoundCount` is reported as a normal workflow output

The progress ledger never reports the request as satisfied and `WithMaxRounds(2)` is set.

```text
$ dotnet run -- limit
[output]  from=Worker_... type=AgentResponseUpdate tags=[]: Worker did one step.
[output]  from=Worker_... type=AgentResponseUpdate tags=[]: Worker did one step.
[output]  from=MagenticOrchestrator type=List`1 tags=[]: Task execution stopped due to hitting the maximum round count limit.
[status]  Idle
```

The limit message has the same shape as a real final answer (`List<ChatMessage>` from
`MagenticOrchestrator`, no tag, run ends `Idle`), and no warning or error event is raised.

## B. An unknown `next_speaker` jumps straight to the final answer

The progress ledger names `Ghost`, which is not a participant.

```text
$ dotnet run -- speaker
[warning] Invalid next speaker: Ghost
[output]  from=MagenticOrchestrator type=List`1 tags=[]: FINAL ANSWER (task was never completed)
[status]  Idle
```

The run completes with a final answer for a task that was never done. The Python side retries
the progress ledger in this case (#9178, fixed by #9181).

Verified with .NET SDK 10.0.102 on macOS; the `repro` GitHub Actions workflow runs both scenarios on Linux (see the Actions tab for the output).
