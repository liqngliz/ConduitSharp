# tools/CodeMetrics/Program.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## (file)

- (was line 7) Cross-platform code metrics — the Visual Studio "Calculate Code Metrics" equivalent (cyclomatic complexity, source lines, maintainability index) computed with Roslyn.
- (was line 10) dotnet run --project tools/CodeMetrics -- <sourceRoot> <outputDir>
- (was line 12) Defaults: sourceRoot=src, outputDir=TestResults/metrics. Emits metrics.csv and metrics.html and prints a summary + the worst offenders.
- (was line 15) Several roots, separated by ';', so plugins are measured alongside src rather than silently left out of every report.
- (was line 22) Each file keeps the root it came from, so the project column stays the first segment under that root (ConduitSharp.Core under src, ConduitSharp.Plugin.X under plugins).

## SourceLines

- (was line 97) Non-blank, non-comment-only source lines within the member.

## HalsteadVolume

- (was line 113) Halstead volume V = N * log2(n): operators = punctuation + keyword tokens, operands = identifiers + literals.

## MaintainabilityIndex

- (was line 145) Microsoft's maintainability index, clamped to 0..100.
