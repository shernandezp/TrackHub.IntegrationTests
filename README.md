# TrackHub.IntegrationTests

In-process integration tests for the GraphQL contracts that couple the TrackHub services together. One `dotnet test` gives a deterministic red/green signal for every service-to-service call — **no Docker, no database, no running services required.**

---

## Overview

The suite proves two things, in two layers:

- **Layer A — contract validation.** Every GraphQL query string a consumer ships (the exact `internal const` production sends, exposed via `InternalsVisibleTo`) is validated against the producer's real, in-process-built schema. A renamed or removed field, argument or input property fails the matching test with a message naming the call.
- **Layer B — round-trip execution.** For the critical and complex flows, the consumer's real reader or writer executes against the producer's real resolvers over an in-process `IGraphQLClient`; only the mediator (`ISender`) behind the resolvers is faked. This catches serialization drift Layer A cannot: enum, UUID and `DateTime` coercion, casing, and field-to-property mapping on both sides.

The suite also exports each producer's SDL to `TrackHub/schemas/<service>.graphql`, which is what the portal's `npm run codegen` validates every frontend operation against.

**Covered pairs**: Router→Manager, Router→Telemetry, Router→Geofence, Router→TripManagement, Reporting→Manager, Reporting→Telemetry, Reporting→Router, Reporting→Geofence, Reporting→TripManagement, Manager→Security, Manager→Router, Security→Manager, Geofencing→Manager, TripManagement→Manager, TripManagement→Telemetry.

Full detail: **[Testing Strategy](https://github.com/shernandezp/TrackHub/wiki/Testing-Strategy)** in the wiki.

---

## Quick start

### Prerequisites

The projects reference the service source by **relative path**, so all TrackHub repositories must be cloned **side by side** with this one: `TrackHub.Manager`, `TrackHub.Telemetry`, `TrackHubRouter`, `TrackHubSecurity`, `TrackHub.Reporting`, `TrackHub.Geofencing`, `TrackHub.TripManagement` — plus the local `TrackHubCommon.*` NuGet feed the services already use.

### Run

```bash
dotnet test TrackHub.IntegrationTests.slnx
```

It runs in seconds. **Run it after any edit to a service's GraphQL surface or to a reader/writer client** — a failure names the exact broken call.

---

## Layout

| Project | Purpose |
|---|---|
| `src/TrackHub.ServiceContracts.Harness` | Test-support library: `InProcessGraphQLClient` (an `IGraphQLClient` over a producer `IRequestExecutor`), the client factory, and the producer schema/executor builder that reuses the production `AddTrackHubGraphQLServer` configuration |
| `tests/TrackHub.ServiceContracts.Tests` | Contract and round-trip tests for every producer/consumer pair |

---

## Project-specific notes

- **The harness tracks the `TrackHubCommon` version through a direct `PackageReference`**, not `Directory.Packages.props`. A repo-wide props sweep after a Common version bump **misses it**, and restore then fails here. Bump it by hand.
- **Never re-type a GraphQL query string in a test.** Reference the production `internal const`, or the test validates a copy that has already drifted from what ships.
- **Enum values travel in GraphQL variables and are invisible to Layer A.** Every enum-typed literal a client sends (`checkType`, `triggerType`, `result`, …) needs a Layer B case asserting it coerces into the producer's enum. This is the most common coverage gap.
- Adding a new inter-service call means adding a Layer A test; complex or critical flows also get a Layer B round-trip.

---

## Documentation

- **Technical** — the [TrackHub wiki](https://github.com/shernandezp/TrackHub/wiki): [Testing Strategy](https://github.com/shernandezp/TrackHub/wiki/Testing-Strategy), [Inter-Service Communication](https://github.com/shernandezp/TrackHub/wiki/Inter-Service-Communication), [Coding Standards](https://github.com/shernandezp/TrackHub/wiki/Coding-Standards)

---

## License

Apache License 2.0. See the [LICENSE file](https://www.apache.org/licenses/LICENSE-2.0) for more information.
