# 0010 — EF Core migration files are generated code, excluded from the style rules

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P4 (the schema is migrated with EF Core tooling); AGENTS.md "Warnings are errors. Do not suppress a warning to get green;
  fix it or record why in an ADR."

## Context

`dotnet ef migrations add` writes migration files in its own layout, with a block-scoped namespace. The repository's
`csharp_style_namespace_declarations = file_scoped:warning` rule, enforced in build as an error (IDE0161), fails the build on them.
Restyling a generated file by hand is lost at the next `migrations add`, so the fix belongs to the rule's scope, not to the file.

## Decision

- `.editorconfig` marks `**/Persistence/Migrations/*.cs` as `generated_code = true`. Roslyn then skips the style rules for those files.
- Hand-written code in every other path keeps the file-scoped namespace rule and every other rule.
- No warning is suppressed by a pragma or a `NoWarn` entry.

## Consequences

- The migration files are not style-checked. Their content is produced by the EF tooling and reviewed as a schema change, which is the
  review that matters for them.
