# 0038 — Compliance evidence and the open register live in one document, and a run that never happened is recorded as not run

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** P14 (documentation lives in the repository and records reasoning, not just steps); reference architecture §3 (the compliance checklist: "Any service claiming to follow this blueprint answers yes to all of these") and §3a (each repo keeps a dated register of its unfixed deviations); `repo-baseline` §8 (a stale README is a review finding)

## Context

WP14 asks for a checklist with evidence per item, and the standard says each repository keeps its own register of deviations, with
every row dated. This repository has to answer the checklist honestly, and some of the answers depend on runs that have never
happened: the AppHost with its containers, the browser flows and the Fly deploy.

Two choices needed recording:

1. Where the evidence and the register live. Options were a separate file per standard, or one `COMPLIANCE.md`.
2. How to write up a check that was never run. A row could say "verified" from a model test, or say "not run".

## Decision

- **One document:** `docs/architecture/COMPLIANCE.md`. Section 1 answers the 19 items of §3 with a file path and, where known,
  a line. Section 3a is the open register, one row per unfixed deviation, with the principle it breaks and the date it started.
  A fixed deviation leaves the register. An accepted one stays, with its ADR.
- **Evidence is a file and a line, or a justified "not applicable".** A model test is evidence of the model only. A CI run is
  evidence of the commit it ran on. A run that never happened is written as "never run" in the row, and never as a pass.
- **The register is not a security report.** Rows name the deviation and its principle. They do not describe how to exploit one.
- **Renumbering the existing ADRs is not part of this record.** Two files carry 0011 and several headings disagree with their file
  names. Fixing them moves every link, so it is a separate PR. The register holds the item (R13).

## Consequences

- The README, the architecture page and the privacy page link to the register. Those links must be kept when the register changes.
- A reviewer can see at once which items are met by code, which by a run, and which by nothing. A row with "not run" is open until
  someone runs it and records the result.
- A new deviation is added to the register in the PR that introduces it, with its date.
