# 0035 — The circuit's hub client uses server-sent events and long polling, reached through the service-discovery handler

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §5 (one resilience and discovery handler per client; the service name, not an address, is the client's target), §7 (the token is read on the server and sent only to the service); ADR 0016 (the hub reads the bearer from the `Authorization` header for HTTP transports and from `access_token` for websockets; the websocket query parameter exists for browsers, and the circuit is not one); ADR 0017 (the estate's topology: services are reached by name inside the estate)

## Context

The SignalR client can use three transports: WebSockets, server-sent events and long polling. The circuit is the only client, and it runs on the server, so no browser opens a socket to the hub.

WebSockets is the default, and it is the only one that does not use the .NET `HttpMessageHandler` chain: the client opens its own socket to the URI's host. That host is the service's logical name (`canvas-service`), which only service discovery resolves, and discovery is applied in the handler chain. A WebSocket would therefore not resolve the name. The unit tests cannot show what SignalR does on that failure, so the choice does not rely on it.

## Decision

- **The circuit's hub client uses `ServerSentEvents` and `LongPolling`.** Both go through an `HttpMessageHandler`, built by `IHttpMessageHandlerFactory` for the `canvas-hub` client. ServiceDefaults adds service discovery to every client, so the name resolves.
- **The token is sent in the `Authorization` header.** The access token provider reads the session service on each request, so a refreshed token is used on the next request. The hub validates it when the connection opens (ADR 0016).
- **WebSockets are not used by the circuit.** A browser never connects to the hub, because the page's data reaches it through the circuit. A websocket path is an optimisation for a later WP, and it would need its own resolution of the service's address.
- **Tests run the same transports on TestServer**, as the service's own hub tests do, so no socket is opened.

## Consequences

- Each open board holds one long-poll or event-stream request to the service per circuit. At the estate's size that is small; it is the first thing to measure if it is not.
- Service discovery is not exercised by the unit tests, which use TestServer directly. The production path through the discovery handler is verified by reading the ServiceDefaults wiring and by the Aspire topology. It is not run in this session, because there is no container host here. See the PR's "Not done".
