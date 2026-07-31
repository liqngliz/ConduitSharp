# src/ConduitSharp.Gateway.AspNetCore/Plugins/PluginAssemblyLoader.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## (file)

- (was line 2) AssemblyLoadContext, AssemblyDependencyResolver

## PluginAssemblyLoader

- (was line 36) One shared Resolving handler for every DLL this loader scans, rather than one handler per DLL: each handler runs on every future unresolved reference for the process's lifetime (plugins may lazily load assemblies at runtime, e.g. PowerShell initialising a Runspace on first use, so detaching after the scan isn't an option). Registering N handlers for N plugin DLLs meant N linear assembly scans plus N delegate invocations per resolution; one handler trying each DLL's resolver in turn does the same work without the per-DLL event-dispatch overhead.

## PluginAssemblyLoader.LoadTypes

- (was line 131) Load plugins into the Default AssemblyLoadContext rather than an isolated context.
- (was line 133) Isolated contexts break plugins that use native P/Invoke libraries (e.g. Microsoft.PowerShell.SDK / libpsl-native) because the native code can only interop correctly with the default runtime context. Loading everything into Default avoids this entirely.
- (was line 138) ResolveFromAnyScannedPlugin (registered once — see the field declarations above) lets the Default context find this plugin's private deps (e.g. Microsoft.PowerShell.SDK, Yarp.ReverseProxy) that are published alongside the plugin DLL but are not in the host's output directory. Assemblies already loaded by the host are reused — no type-identity mismatch.
- (was line 148) Kept registered for the process's lifetime — plugins may lazily load assemblies at runtime (e.g. PowerShell initialising a Runspace on first use).
- (was line 164) A copy of an assembly the host already has (plugin publishes bring the whole dependency closure, e.g. ConduitSharp.Host.dll). Loading it again would re-discover the built-in plugins it contains and re-register them AFTER the external plugin — last-registration-wins would then silently shadow the plugin with the built-in it was meant to replace.
- (was line 172) Reuse if already loaded from this exact path (gateway restart in tests).
- (was line 177) this DLL never loaded — its resolver can't help anyone
