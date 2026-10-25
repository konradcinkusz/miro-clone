# 0018 — Forwarded headers from the Fly proxy are applied, one hop, before the origin guard

- **Status:** Accepted. Refines ADR 0006 (the guard runs first, and forwarded headers are configured before it).
- **Date:** 2026-10-25
- **Principles:** `blazor-web-app` §8 (origin guard); `fly-io-deployment` §2 (listen on configured port, read configuration from the environment),
  §6 (public URL); P5 (tokens never reach the browser)

## Context

On Fly, TLS ends at the proxy. The app sees `http` on its own host, while the browser's `Origin` is `https://miroclone-web-dev.fly.dev`. The origin
guard (ADR 0006) compares `Origin` with `request.Scheme://request.Host`, so without forwarded headers every browser request is refused. Forwarded
headers were named as a requirement by ADR 0006 but were not wired anywhere until this work package.

The default ASP.NET configuration trusts only loopback proxies. Fly's proxy address is not fixed, so the default would ignore the headers.

## Decision

- `MiroClone.ServiceDefaults/ForwardedHeadersRegistration.cs` adds `AddProxyForwardedHeaders()` and `UseProxyForwardedHeaders()`. Each of the four
  entry points (Web, Boards, Canvas, and the two test hosts) calls both, and the use comes before `UseOriginGuard()`.
- Headers: `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host`. `ForwardLimit = 1`.
- `KnownProxies` and `KnownIPNetworks` are cleared, so the middleware trusts the proxy in front of the app. Trust is safe because the only routes
  to a service are the Fly proxy and the private network. The proxy appends the address it saw, so with one hop the last `X-Forwarded-For`
  entry is the client, and entries a client adds before it are ignored.
- Local runs and tests send no forwarded headers, so their behaviour is unchanged.

## Consequences

- A client that connects straight to a machine's address could set these headers. The origin guard still refuses a foreign `Origin` on a
  cross-site request, because it cannot match the victim's `Origin` against a forged host. A non-browser client sends no `Origin`, and the guard
  already allows that case (ADR 0006).
- `ForwardedHeadersTests` pins three cases: the public origin is allowed with the proxy's headers, refused without them, and a foreign origin is
  refused even with them.
- Forwarded headers are part of the kernel, so the kernel grows by about 30 lines. The ceiling of 800 lines (ADR 0005) still holds.
