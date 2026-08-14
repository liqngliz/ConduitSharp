# plugins/ConduitSharp.Plugin.PowerShell/tests/ConduitSharp.Plugin.PowerShell.Tests/PowerShellPluginTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PowerShellPluginTests.EmbeddedRuntime_CanExecuteScript

- (was line 24) Verifies the embedded Microsoft.PowerShell.SDK runtime works in-process without any system pwsh installation.

## PowerShellPluginTests.EmbeddedRuntime_CanRunErpReportScript

- (was line 39) Runs the actual Get-ErpReport.ps1 content through the embedded runtime and confirms it returns valid JSON with the expected fields.

## PowerShellPluginTests.ExecuteAsync_100ConcurrentInvocations_AllComplete_NoThreadPoolStarvation

- (was line 66) ps.Invoke() runs synchronously inside Task.Run, parking a pool thread per request. Under a burst that far exceeds the core count, the pool's slow thread injection is the risk: requests queue behind blocked threads and the gateway appears hung. This holds 100 concurrent invocations to a deadline and demands every one succeeds.

## PowerShellPluginTests.ExecuteAsync_ClientAborts_ReturnsPromptly_DoesNotLeakThread

- (was line 100) A hung script (Start-Sleep 60) with the client already gone: context.RequestAborted is signalled but ExecuteAsync never observes it, so the call blocks on ps.Invoke() for the full script duration, parking a thread-pool thread the whole time. A correct implementation registers RequestAborted -> ps.Stop() and returns once the token fires.
- (was line 115) client already gone before the script finishes

## PowerShellPluginTests.ExecuteAsync_ScriptExceedsTimeout_Returns504

- (was line 133) No client abort — the configured timeoutMs must stop a slow script on its own.
