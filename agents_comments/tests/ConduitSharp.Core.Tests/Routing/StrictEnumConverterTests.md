# tests/ConduitSharp.Core.Tests/Routing/StrictEnumConverterTests.cs

Inline commentary for the source file above, keyed by symbol. Read this before
changing that file; update it in the same change. Line numbers are where each note
sat when it was moved and are a hint only — the symbol is the anchor.

## StrictEnumConverterTests.Read_AcceptsKebabAndPascalCase

- (was line 23) kebab-case
- (was line 25) PascalCase case-insensitive

## StrictEnumConverterTests.Read_InvalidValue_ThrowsJsonExceptionListingValidNames

- (was line 44) The message must name what *was* valid — a typo in routes.json should be self-correcting.

## StrictEnumConverterTests.Read_LeadingDashes_ProduceAStableResult_NotACrash

- (was line 51) "--cache" splits to ["", "", "cache"], so the empty-segment guard inside KebabToPascal fires. It must yield "Cache" rather than throwing on the empty segments.
