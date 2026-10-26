# 0028 — Board covers are static SVG drawn only in the token colours; the dashboard adds no JavaScript

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §4 (dependency budget: the framework only, no JS interop packages), §9 (colour only through tokens:
  a stylesheet test enforces it); WP09 (the only JavaScript module in the app is the canvas pointer module, budgeted there)

## Context

Each board shows a cover picture chosen by `imageSeed` (1..10). The Next.js reference uses raster and SVG files whose colours
are not the Blazor token contract. The contract forbids colour outside the token blocks, and `StyleContractTests` reads only
`app.css` and the scoped stylesheets, so the picture files would escape the test.

## Decision

- **Ten SVG pictures under `wwwroot/boards/seed-1..10.svg`,** each with a 100 × 127 viewBox, drawn with shapes only. Their fills
  use only the hex values the light token block defines (`#ffffff`, `#f4f5f8`, `#e5e9fb`, `#3550c8`, `#2a40a6`, `#6b7180`).
  They were generated for this repository; none is copied from the reference.
- `PlaceholderContractTests` reads each file and fails on a hex colour that is not a token colour, on a missing seed, and on a
  colour named by keyword. A restyle that changes a token changes the pictures only when the same value is also in the token block.
- The pictures are plain `<img alt="">`: the title is text beside them, so the image is decorative.
- **No JavaScript.** The dashboard uses only Blazor components, the framework's forms and links, and scoped CSS. The one JavaScript
  module in the app stays in WP09 (WP09 records its own dependency-budget ADR).

## Consequences

- Dark mode keeps the light artwork. The pictures are fixed artwork, not text, and a contrast test covers only text pairs.
- A new cover means a new file, a seed range change in `BoardImage.Count`, and a test run; the test fails if a colour strays.
