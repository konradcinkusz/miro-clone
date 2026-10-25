# 0001 — Blazor Web App (interactive Server) over Next.js

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** blazor-web-app guide §1 ("Choosing Blazor or Next.js"); P14 (decisions recorded in the repository)

## Context

The original clone is Next.js with Convex and Liveblocks. This rebuild is an estate service set on .NET 10.
The audience is a bounded set of users (one workspace per organization), the team writes C#, and the page count
is modest. The estate's standard offers two front-end options that both keep tokens and backend addresses out of
the browser.

## Decision

`MiroClone.Web` is a Blazor Web App in interactive Server render mode. The circuit is the BFF: tokens stay
server-side, the browser holds an httpOnly SameSite=Strict cookie, and typed `HttpClient`s on the server call the
services. Blazor WebAssembly is not used, because it puts the API address and the token in the browser.

This is the row of the guide's table that decided it: *internal tools, dashboards, bounded audience, .NET-only team*.

## Consequences

- No Node toolchain and no JavaScript dependencies in the repository; the .NET SDK is the only prerequisite.
- Per-circuit state lives in server memory. Scale-to-zero costs open pages their state (guide §14); accepted for this audience.
- Browser code cannot call the services directly; any future browser-side call goes through the BFF.
