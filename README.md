# DeveloperStore Sales API

A complete CRUD API for sales records, built on the DeveloperStore evaluation
template. Written in C# on .NET 8 with a DDD layering, CQRS handlers over MediatR,
EF Core against PostgreSQL, a MongoDB audit trail and a Redis read cache.

The original brief is preserved verbatim in [.doc/assignment.md](/.doc/assignment.md).

---

## Contents

- [Quick start](#quick-start)
- [Business rules](#business-rules)
- [API](#api)
- [Architecture](#architecture)
- [Domain events](#domain-events)
- [Running the tests](#running-the-tests)
- [Configuration](#configuration)
- [Git Flow](#git-flow)
- [Design decisions](#design-decisions)

---

## Quick start

### Option A — Docker Compose (everything)

Brings up the API, PostgreSQL, MongoDB and Redis, with migrations applied on
startup.

```bash
docker compose up --build
```

Then open <http://localhost:8080/swagger>.

### Option B — run the API locally

Needs the **.NET 8 SDK**. Start only the backing services, then run the API from
the shell:

```bash
docker compose up -d ambev.developerevaluation.database
dotnet run --project src/Ambev.DeveloperEvaluation.WebApi
```

Then open <https://localhost:7181/swagger>.

MongoDB and Redis are **off by default** in `appsettings.json`, so this works with
PostgreSQL alone. See [Configuration](#configuration) to switch them on.

### No Docker at all?

Point `ConnectionStrings:DefaultConnection` at any reachable PostgreSQL instance and
run option B. The test suite needs no database whatsoever — see
[Running the tests](#running-the-tests).

### Health

| Endpoint | Purpose |
|---|---|
| `GET /health` | overall status |
| `GET /health/live` | liveness probe |
| `GET /health/ready` | readiness probe |

---

## Business rules

Discounts are decided by the quantity of a single product on a sale:

| Quantity | Discount | Rule |
|---|---|---|
| 1–3 | none | "Purchases below 4 items cannot have a discount" |
| 4–9 | **10%** | "4+ items: 10% discount" |
| 10–20 | **20%** | "Purchases between 10 and 20 identical items have a 20% discount" |
| 21+ | **rejected** (HTTP 400) | "It's not possible to sell above 20 identical items" |

Two points in the brief are ambiguous. Both are resolved deliberately, and the
reasoning is written into
[`QuantityTierDiscountPolicy`](/src/Ambev.DeveloperEvaluation.Domain/Services/QuantityTierDiscountPolicy.cs):

1. **The 10% tier starts at exactly 4.** The prose says "above 4 identical items",
   which would start it at 5, while the restated tier list says "4+". They cannot both
   hold. The tier list wins: it is the more precise of the two, and the fourth rule —
   "below 4 items cannot have a discount" — only reads as the complement of a tier
   beginning at 4.

2. **"Identical items" means per product across the whole sale, not per line.** A sale
   refuses to hold the same product on two lines. Without that, a caller could sell 40
   units as two lines of 20, and could dodge the tiers by splitting 10 units into three
   lines that each earn nothing.

Discounts and totals are always computed by the server. No request can supply them.

---

## API

Base path `/api/sales`. Full interactive documentation is in Swagger;
[.doc/sales-api.md](/.doc/sales-api.md) has the request and response shapes.

| Verb | Route | Purpose |
|---|---|---|
| `POST` | `/api/sales` | register a sale |
| `GET` | `/api/sales` | list, with paging, ordering and filtering |
| `GET` | `/api/sales/{id}` | retrieve one sale |
| `PUT` | `/api/sales/{id}` | replace a sale |
| `DELETE` | `/api/sales/{id}` | erase a sale |
| `PATCH` | `/api/sales/{id}/cancel` | cancel a sale, keeping the record |
| `PATCH` | `/api/sales/{id}/items/{itemId}/cancel` | cancel one line |

`DELETE` erases; `PATCH .../cancel` voids while keeping the record auditable.
Cancelling is the business operation a real deployment would use.

### Paging, ordering and filtering

Implemented as [.doc/general-api.md](/.doc/general-api.md) specifies.

```http
GET /api/sales?_page=2&_size=20
GET /api/sales?_order=saleDate desc, saleNumber asc
GET /api/sales?branch.name=Downtown&isCancelled=false
GET /api/sales?customer.name=Maria*
GET /api/sales?_minSaleDate=2024-01-01&_maxSaleDate=2024-12-31
GET /api/sales?_minTotalAmount=100&_maxTotalAmount=500
```

- `_page` defaults to 1, `_size` to 10 and is capped at 100.
- Dotted paths reach into the external identities (`customer.name`, `branch.name`,
  `items.product.title`).
- `value*`, `*value` and `*value*` are starts-with, ends-with and contains,
  case-insensitively.
- `_minField` and `_maxField` bound numeric and date fields, inclusively.
- An unrecognised field is ignored rather than rejected, so a stray query parameter
  does not fail the request.

### Error responses

Every failure uses the shape from `.doc/general-api.md`:

```json
{
  "type": "BusinessRuleViolation",
  "error": "Operation rejected by a business rule",
  "detail": "Cannot sell more than 20 identical items. Requested 21."
}
```

| `type` | Status | Meaning |
|---|---|---|
| `ValidationError` | 400 | the request is malformed |
| `BusinessRuleViolation` | 400 | well-formed, but a domain rule refused it |
| `AuthenticationError` | 401 | authentication failed |
| `ResourceNotFound` | 404 | no such sale or item |
| `ResourceConflict` | 409 | the sale number is already taken |
| `InternalServerError` | 500 | a defect; details are logged, never returned |

---

## Architecture

```
root
├── src/
│   ├── Ambev.DeveloperEvaluation.Domain/       entities, value objects, rules, events
│   ├── Ambev.DeveloperEvaluation.Application/  CQRS commands, handlers, validators
│   ├── Ambev.DeveloperEvaluation.ORM/          EF Core, repositories, Mongo store
│   ├── Ambev.DeveloperEvaluation.WebApi/       controllers, middleware, contracts
│   ├── Ambev.DeveloperEvaluation.IoC/          composition root
│   └── Ambev.DeveloperEvaluation.Common/       logging, security, validation, caching
├── tests/
│   ├── Ambev.DeveloperEvaluation.Unit/         domain and handler tests
│   ├── Ambev.DeveloperEvaluation.Integration/  repository and event dispatch tests
│   └── Ambev.DeveloperEvaluation.Functional/   end-to-end HTTP tests
└── .doc/                                       specifications
```

Dependencies point inwards. The domain references only FluentValidation; it knows
nothing of EF Core, MediatR, ASP.NET Core or MongoDB. Repository and event-publisher
interfaces are declared in the domain and implemented in the infrastructure.

### The Sale aggregate

[`Sale`](/src/Ambev.DeveloperEvaluation.Domain/Entities/Sale.cs) is the aggregate
root and `SaleItem` lives inside it. Items are never loaded or saved on their own,
which is what lets the sale guarantee rules no single item could: one line per
product, and a total that always equals the sum of the lines that still count.

Every property has a private setter and the item collection is exposed read-only, so
a sale cannot be put into an invalid state from outside.

### External Identities

Customer, branch and product belong to other bounded contexts. Each is referenced by
its identifier **plus a denormalized description** captured at the time of sale,
exactly as the brief asks:

```json
"customer": { "id": "9cb05fda-...", "description": "Maria Silva" }
```

The copy is intentionally never refreshed. A sale is a historical record: a customer
renamed tomorrow must not change the name on a sale made today. It also means a sale
can be read when the customer service is unavailable.

---

## Domain events

Four events are raised by the aggregate and published after the database transaction
commits, so a rolled-back operation never announces a change that did not happen.

| Event | Raised when |
|---|---|
| `SaleCreated` | a sale is registered |
| `SaleModified` | a sale is updated — once per request, not per field |
| `SaleCancelled` | a whole sale is cancelled |
| `ItemCancelled` | one line is cancelled |

Two sinks receive them, and a failure in either is logged and swallowed — the sale is
already committed, so an unreachable sink must not report a successful operation as
an error:

- **Application log** (always on). Structured Serilog output, so `EventType` and
  `SaleNumber` are queryable fields. This satisfies the brief's "log a message in the
  application log or however you find most convenient".
- **MongoDB** (optional). An append-only `sale_events` collection. PostgreSQL remains
  the system of record; losing this collection loses the audit trail, not the sales.

Publishing sits behind `IDomainEventPublisher`, which names no messaging library, so
swapping in a real broker is a registration change.

---

## Running the tests

```bash
dotnet test
```

**226 tests, no database or container required.** The integration and functional
suites run against SQLite in memory, so `dotnet test` works on a clean machine.

| Suite | Count | Covers |
|---|---|---|
| Unit | 179 | discount tiers, aggregate invariants, handlers, caching, query building |
| Integration | 26 | EF Core mapping, repository queries, event dispatch ordering |
| Functional | 21 | the real HTTP pipeline end to end |

Coverage report:

```bash
./coverage-report.sh      # or coverage-report.bat on Windows
# → TestResults/CoverageReport/index.html
```

### Verifying the business rules by hand

With the API running, via Swagger or
[the .http file](/src/Ambev.DeveloperEvaluation.WebApi/Ambev.DeveloperEvaluation.WebApi.http):

| # | Request | Expected |
|---|---|---|
| 1 | `POST /api/sales`, one item × **3** | 201, discount `0`, total = 3 × price |
| 2 | `POST /api/sales`, one item × **5** | 201, discount **10%** |
| 3 | `POST /api/sales`, one item × **15** | 201, discount **20%** |
| 4 | `POST /api/sales`, one item × **21** | **400**, `type: BusinessRuleViolation` |

Then `PATCH /api/sales/{id}/items/{itemId}/cancel` and confirm the sale total is
recalculated and an `ItemCancelled` entry appears in the console log.

---

## Configuration

`src/Ambev.DeveloperEvaluation.WebApi/appsettings.json`, overridable by environment
variables using `__` for nesting (`Cache__Enabled=true`).

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | localhost:5432 | PostgreSQL |
| `Cache:Enabled` | `false` | Redis read cache |
| `Cache:ConnectionString` | localhost:6379 | Redis |
| `Mongo:Enabled` | `false` | MongoDB audit trail |
| `Mongo:ConnectionString` | localhost:27017 | MongoDB |
| `Jwt:SecretKey` | — | JWT signing key |

Redis and MongoDB default to off so the API runs against PostgreSQL alone. Compose
switches both on. When either is enabled but unreachable, the API degrades rather
than failing: reads fall through to the database, and events still reach the log.

### Migrations

Applied automatically on startup in Development. To run them by hand:

```bash
dotnet ef database update \
  --project src/Ambev.DeveloperEvaluation.ORM \
  --startup-project src/Ambev.DeveloperEvaluation.WebApi
```

---

## Git Flow

The history is part of the deliverable. `main` holds the pristine template as its
first commit, so everything after it is visibly the work done on top.

```
main ─────●───────────────────────────────────────────────● v1.0.0
          │ clean template                               ╱
develop   └──●──●──●──●──●──●──●──●──●──●─────────────────
              feature branches, each merged with --no-ff
```

| Branch | Delivered |
|---|---|
| `feature/repo-restructure` | move the template to the documented layout |
| `feature/infrastructure-alignment` | make the project actually run |
| `feature/api-error-contract` | error, paging and filtering contract |
| `feature/sale-domain` | the Sale aggregate and the discount rules |
| `feature/sale-persistence` | EF Core mapping, repository, migration |
| `feature/sale-application` | CQRS use cases |
| `feature/sale-api` | HTTP endpoints |
| `feature/domain-events` | event publishing and the Mongo audit trail |
| `feature/redis-cache` | read caching |
| `feature/documentation` | this README and the API docs |

Commits follow [Conventional Commits](https://www.conventionalcommits.org)
(`feat(domain):`, `fix:`, `test:`, `docs:`, `refactor:`). Each message explains *why*
the change was made, not only what changed.

```bash
git log --graph --oneline --all
```

---

## Design decisions

Recorded here because they are the parts a reader is most likely to question. Each is
argued at length in the code and in the relevant commit message.

**A product may appear only once per sale.** Merging duplicates would be friendlier,
but rejecting them makes "identical items" unambiguous and closes both the 20-item cap
bypass and the discount-tier split.

**Discounts are stored as amounts, not rates.** The amount is what was actually
charged. Storing only the rate would silently rewrite historical totals whenever the
policy changed.

**Cancellation is soft; deletion is hard.** A voided sale must stay auditable, so
`cancel` flags and zeroes while `delete` erases.

**Value objects are mapped as EF Core complex properties, not owned types.** An owned
type carries its own identity keyed by its owner, so replacing a sale's customer made
EF delete and insert at the same key and fail. A complex property has no identity, so
replacing it is an ordinary column update — which is what a value object should be.

**Updates reconcile items rather than rebuilding them.** Discarding and recreating
every line would give each surviving item a new id on every update, invalidating any
item id a client holds for the cancel endpoint.

**Aggregate keys are `ValueGeneratedNever`.** The domain assigns identity at
construction. EF's default treats a non-default `Guid` key as store-generated and
would classify a new item as an update to a row that does not exist.

**The cache stores read models, never aggregates.** A deserialized aggregate would
have been written past its own setters and could not enforce its invariants.

**`ResourceConflictException` rather than `InvalidOperationException` for 409.** The
BCL throws `InvalidOperationException` for unresolvable dependencies and other
ordinary defects; mapping it to 409 reported genuine faults as business conflicts and
kept them out of the logs.

### Known limitations

- **No optimistic concurrency token on `Sale`.** Two concurrent updates to the same
  sale can interleave. The fix is a row version on the aggregate rather than
  per-field locking.
- **Event publishing is not transactional with the commit.** A crash between the two
  loses the announcement. An outbox would close this.
- **`BaseController.Ok<T>` shadows `ControllerBase.Ok`** and double-wraps an
  already-built envelope. `SalesController` avoids it by constructing results
  explicitly; the template's `UsersController` still hits it and is left as found,
  since Sales is the graded scope.
