# tests/ConduitSharp.Integration.Tests/Gateway/PluginAssemblyLoaderTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## PluginAssemblyLoaderTests.DiscoverPluginTypes_NonExistentDirectory_ReturnsEmpty

- (was line 20) Non-existent directory

## PluginAssemblyLoaderTests.DiscoverPluginTypes_EmptyDirectory_ReturnsEmpty

- (was line 32) Existing but empty directory

## PluginAssemblyLoaderTests.DiscoverPluginTypes_DllWithNoPlugins_ReturnsEmpty

- (was line 48) Real DLL with no IPipelinePlugin implementations → covers PluginLoadContext and the GetExportedTypes / filter path
- (was line 55) ConduitSharp.Core.dll is always in the test output dir and has no plugins.

## PluginAssemblyLoaderTests.DiscoverPluginTypes_CorruptDll_SkipsFileAndReturnsEmpty

- (was line 70) Corrupt / unreadable DLL → covers the load-failure catch branch
- (was line 79) Valid PE header magic (MZ) but truncated — LoadFromAssemblyPath will throw.

## PluginAssemblyLoaderTests.DiscoverServiceType_MissingDirectory_ReturnsNull

- (was line 92) DiscoverServiceType — global service backends dropped in the plugins root

## PluginAssemblyLoaderTests.DiscoverServiceType_NoImplementationInRoot_ReturnsNull

- (was line 110) A DLL with no ICacheService implementation → nothing discovered.

## PluginAssemblyLoaderTests.DiscoverPluginTypes_CorruptDllInSubdirectory_SkipsAndReturnsEmpty

- (was line 123) Corrupt DLL where it is actually scanned → covers the load-failure catch on both scan paths (subdirectory scan and root scan). The earlier corrupt test drops the DLL in the root, which DiscoverPluginTypes never scans, so it did not reach the catch.
- (was line 132) DiscoverPluginTypes scans per-route SUBDIRECTORIES. A corrupt DLL dropped into one must be logged and skipped, not crash the scan — the appliance drop-in model has to survive a bad artifact.

## PluginAssemblyLoaderTests.DiscoverServiceType_CorruptDllInRoot_ReturnsNull

- (was line 151) DiscoverServiceType scans the plugins ROOT directly. A corrupt DLL there must be skipped, leaving the built-in default in place rather than failing the gateway.

## PluginAssemblyLoaderTests.CreateTempDir

- (was line 166) Helper
