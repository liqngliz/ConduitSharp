# tests/ConduitSharp.Architecture.Tests/DependencyBoundaryTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## DependencyBoundaryTests.Core_references_no_other_ConduitSharp_assembly

- (was line 46) Core is the only assembly external plugin authors compile against — anything it drags in becomes part of every plugin's dependency graph.

## DependencyBoundaryTests.Plugin_assembly_references_no_Yarp

- (was line 68) YARP is the gateway's forwarder choice, not part of the plugin contract — a plugin assembly referencing it couples every plugin author to YARP's types.
