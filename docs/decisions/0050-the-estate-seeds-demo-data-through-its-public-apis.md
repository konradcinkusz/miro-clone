# 0050. The estate seeds demo data through its public APIs, and the authservice container is proven by CI

Status: accepted. Applies the reference architecture's demo-data rule (seed through the application's own interfaces, deterministic, idempotent, namespaced, development only) and "verify what you ship" (register item R1).

## Context

The owner asked that every app has a seed at the Aspire level and that the local authservice container really works. Before this change the only seed was
`scripts/seed-dev-users.sh`, a manual script that stopped halfway on a first run because authservice returns the invitation token only in its dev
outbox log. The AppHost seeded nothing. The authservice container in the AppHost path (`ghcr.io/konradcinkusz/authservice:v0.3.4` and a generated key)
had never run end to end in the session that wrote it, because that session could not pull the image.

## Decisions

1. **One seed program, `MiroClone.Seed`.** A console project that signs in or registers `owner@dev.test` and `member@dev.test` (password `Dev-Password-123!`,
   a published development value), finds or creates the `Dev Workspace` organization, brings the member in through an invitation, finds or creates three
   boards titled `Demo: ...` and adds their shapes through the canvas SignalR hub. It uses only HTTP and SignalR, never a database, and references no service
   project: the wire contracts are restated and the seed's tests pin the behaviour. The AppHost, compose and the script all run this one program.
2. **Idempotent and deterministic.** Registration happens only when sign-in is refused; the organization and boards are found by name and title; shapes have ids derived
   from fixed names and an upsert of a present id changes nothing, so the seed adds only the missing ones. A second run registers, creates and adds nothing (tested).
3. **A reserved namespace.** `dev.test` addresses, the organization name, the `Demo: ` title prefix and the derived layer ids. `--reset` removes only boards whose
   title starts with `Demo: ` in the demo workspace (tested: other boards and other workspaces survive).
4. **Development only, with an explicit switch.** The program reads `ASPNETCORE_ENVIRONMENT` first and exits 1 outside `Development`. The AppHost adds the `seed`
   resource only in Development; `Seed:Enabled` defaults to true there and false elsewhere; `Seed:Enabled=true` outside Development stops the AppHost. `Seed:Enabled=false`
   removes the resource. Compose passes `Seed__Enabled=${SEED_ENABLED:-true}`.
5. **Start order.** The seed waits for authservice, boards-service and canvas-service (AppHost `WaitFor`; compose `depends_on: service_healthy`). The web app does
   not wait for it in the AppHost, so the dashboard comes up at once. In compose the web app starts after the seed completes, so `docker compose up --wait` returns
   only when the estate is seeded.
6. **The invitation token.** authservice has no way to add a member without a token it prints only to its console in Development. The seed reads the container's log over the Docker
   Engine API (unix socket), so it needs no Docker CLI. The AppHost names the container `miroclone-authservice-apphost` (`WithContainerName`), compose names it
   `miroclone-authservice-1`. Compose mounts the socket into the seed container and runs it as root, which is acceptable only because the container is development-only.
   `Seed__AuthServiceLogFile` and `Seed__InvitationToken` replace the socket where there is none.
7. **The two definitions of authservice cannot drift.** `ComposeParityTests` compares the AppHost model with `compose.yaml`: image and tag, port, health probe, the exact set of environment variable
   names and the values that must match, the PKCS#8 PEM key (`openssl genpkey`, not `genrsa`), and the seed's settings. A change to one side fails the test until the other follows.
8. **CI.** `compose-e2e` now also starts the seed container, prints authservice's startup log and `/health/ready`, runs `scripts/verify-seed.sh`, and runs the seeded journeys
   (owner and member sign in, see the demo boards, the member sees the owner's edit live). `apphost-e2e` starts the AppHost itself on the runner as an experiment on the claim in ADR 0049 that Aspire's DCP does not run there.

## Result of the DCP experiment

Aspire's DCP **does run on a GitHub-hosted runner** (ubuntu): the AppHost started PostgreSQL and the real `ghcr.io/konradcinkusz/authservice:v0.3.4` container
(`RS256`, `/health/ready` 200) and the services. ADR 0049's claim that it does not was wrong for the `dotnet run --project MiroClone.AppHost` path.
The experiment found a real defect: the AppHost starts the services and the seed as parallel `dotnet run` builds, which collide on the shared
`ServiceDefaults` deps file (`MSB4018 GenerateDepsFile ... file in use`) and leave a service unstarted. `scripts/run-apphost.sh` and the CI job build the solution once first.

Further defects the same job found, all fixed here: the AppHost gave authservice a container-network address as `Jwt__PublicBaseUrl`, so the host-run services could not
fetch its keys (now `http://localhost:5080`); it gave canvas no reference to boards, so every canvas read answered 503; and it started every project as **Production**
whatever its own environment (Aspire passes `--no-launch-profile` and does not forward the environment), so the web app served HTTP 500 for its stylesheet and Blazor script
and the seed refused to run. `EstateExtensions.WithHostEnvironment` now passes the AppHost's environment name to each project.

## Consequences

- Every start of the estate yields the same two logins. The seed is a Development tool and ships in no deployment: no Fly config mentions it, and the production images are unchanged.
- The Docker socket in the compose seed container is root-equivalent on the host. It is for a developer's machine and CI runners only.
- A reset-and-seed on an estate whose authservice database was kept but whose authservice container was recreated cannot find the old invitation token. Remove the volume, or
  set `Seed__InvitationToken`.
