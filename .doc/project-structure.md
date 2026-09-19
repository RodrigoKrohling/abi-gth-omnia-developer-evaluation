[Back to README](../README.md)

## Project Structure

The required structure is:

```
root
├── src/
├── tests/
└── README.md
```

which this repository follows. In full:

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
│   ├── Ambev.DeveloperEvaluation.Unit/
│   ├── Ambev.DeveloperEvaluation.Integration/
│   └── Ambev.DeveloperEvaluation.Functional/
├── .doc/                                       these specifications
├── docker-compose.yml
├── Dockerfile
├── Ambev.DeveloperEvaluation.sln
└── README.md
```

The template shipped nested under `template/backend/`; the first feature branch
promoted it to the layout above.

### Layering

Dependencies point inwards:

```
WebApi ──▶ IoC ──▶ Application ──▶ Domain ──▶ Common
                        │              ▲
                       ORM ────────────┘
```

- **Domain** references only FluentValidation. It knows nothing of EF Core, MediatR,
  ASP.NET Core or MongoDB.
- **Application** holds one folder per use case, each with its command, handler,
  validator and result.
- **ORM** implements the repository and event-publisher interfaces that the domain
  declares, which is what keeps the arrow pointing inwards.
- **WebApi** translates HTTP to commands and back, and owns the error contract.
- **IoC** is the only place that decides which implementations are used.

### Sale aggregate

`Sale` is the aggregate root; `SaleItem` lives inside it and is never loaded or
saved on its own. Customer, branch and product are External Identity value objects,
mapped as EF Core complex properties into columns on the parent tables.

<br/>
<div style="display: flex; justify-content: space-between;">
  <a href="./sales-api.md">Previous: Sales API</a>
  <a href="../README.md">Back to README</a>
</div>
