# plugins/ConduitSharp.Plugin.PowerShell/src/ConduitSharp.Plugin.PowerShell/PowerShellPlugin.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PowerShellPlugin.Name

- (was line 23) External plugins register under Custom with a variant — routes declare { "name": "custom", "variant": "power-shell" }.

## PowerShellPlugin.ExecuteAsync

- (was line 54) Run the synchronous PS Invoke on a thread-pool thread so we don't block the ASP.NET request thread while the runspace initialises. ps.Stop() (fired from RequestAborted or the timeout) unblocks Invoke() so a hung script can't park the pool thread until it finishes on its own.
- (was line 75) aborted before Invoke started
- (was line 92) client gone — nothing to write
