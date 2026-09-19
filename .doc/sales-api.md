[Back to README](../README.md)

### Sales

The sales resource. Customers, branches and products are referenced with the
**External Identities** pattern: each carries its identifier in the owning domain
plus a denormalized description captured at the time of sale.

Discounts and totals are always calculated by the server from the quantities and
are never accepted from a request.

#### POST /sales
- Description: Register a new sale
- Request Body:
  ```json
  {
    "saleNumber": "string",
    "saleDate": "string (date-time, UTC)",
    "customerId": "string (uuid)",
    "customerName": "string",
    "branchId": "string (uuid)",
    "branchName": "string",
    "items": [
      {
        "productId": "string (uuid)",
        "productTitle": "string",
        "quantity": "integer (1-20)",
        "unitPrice": "number"
      }
    ]
  }
  ```
- Response (201 Created), with a `Location` header pointing at the new sale:
  ```json
  {
    "data": {
      "id": "string (uuid)",
      "saleNumber": "string",
      "saleDate": "string (date-time)",
      "customer": { "id": "string (uuid)", "description": "string" },
      "branch": { "id": "string (uuid)", "description": "string" },
      "items": [
        {
          "id": "string (uuid)",
          "product": { "id": "string (uuid)", "description": "string" },
          "quantity": "integer",
          "unitPrice": "number",
          "discountRate": "number",
          "discount": "number",
          "totalAmount": "number",
          "isCancelled": "boolean",
          "cancelledAt": "string (date-time) | null"
        }
      ],
      "totalAmount": "number",
      "isCancelled": "boolean",
      "createdAt": "string (date-time)",
      "updatedAt": "string (date-time) | null",
      "cancelledAt": "string (date-time) | null"
    },
    "success": true,
    "message": "Sale created successfully",
    "errors": []
  }
  ```
- Errors:
  - `400 ValidationError` — the request is malformed
  - `400 BusinessRuleViolation` — more than 20 identical items, or a duplicated product
  - `409 ResourceConflict` — the sale number is already taken

#### GET /sales
- Description: Retrieve a page of sales
- Query Parameters:
  - `_page` (optional): Page number (default: 1)
  - `_size` (optional): Items per page (default: 10, maximum: 100)
  - `_order` (optional): Ordering, e.g. `"saleDate desc, saleNumber asc"`
  - any field name: filter by value, with `*` wildcards on strings
  - `_minField` / `_maxField`: inclusive bounds on numeric and date fields
- Response:
  ```json
  {
    "data": [ "…sale objects…" ],
    "totalCount": "integer",
    "currentPage": "integer",
    "totalPages": "integer",
    "success": true,
    "message": "Sales retrieved successfully",
    "errors": []
  }
  ```

Examples:

```
GET /sales?_page=2&_size=20&_order=saleDate desc
GET /sales?branch.name=Downtown&isCancelled=false
GET /sales?customer.name=Maria*
GET /sales?_minSaleDate=2024-01-01&_maxSaleDate=2024-12-31
GET /sales?_minTotalAmount=100&_maxTotalAmount=500
```

Dotted paths reach into the external identities. An unrecognised field is ignored
rather than rejected.

#### GET /sales/{id}
- Description: Retrieve a specific sale by ID
- Path Parameters:
  - `id`: Sale ID (uuid)
- Response: the sale object, as in POST
- Errors:
  - `404 ResourceNotFound` — no sale has that identifier

#### PUT /sales/{id}
- Description: Replace a specific sale
- Path Parameters:
  - `id`: Sale ID (uuid)
- Request Body: as POST, but **without** `saleNumber` — the sale number identifies
  the sale to the business and appears on receipts already issued, so an update
  cannot change it.
- Response: the updated sale, with discounts and totals recalculated
- Notes:
  - The resulting item set is exactly what `items` lists. A product currently on the
    sale but absent from the request is removed; one already present keeps its item
    id and has its quantity and price updated.
  - Cancelled items are preserved and cannot be changed.
- Errors:
  - `400 BusinessRuleViolation` — the sale is cancelled, or an item is not acceptable
  - `404 ResourceNotFound` — no sale has that identifier

#### DELETE /sales/{id}
- Description: Permanently remove a sale and its items
- Path Parameters:
  - `id`: Sale ID (uuid)
- Response:
  ```json
  { "success": true, "message": "Sale deleted successfully", "errors": [] }
  ```
- Errors:
  - `404 ResourceNotFound` — no sale has that identifier

To void a sale while keeping it auditable, prefer `PATCH /sales/{id}/cancel`.

#### PATCH /sales/{id}/cancel
- Description: Cancel a whole sale, keeping the record
- Path Parameters:
  - `id`: Sale ID (uuid)
- Response: the cancelled sale, with `isCancelled: true` and `totalAmount: 0`
- Notes: the items are kept so the record of what was ordered survives. Raises
  `SaleCancelled`.
- Errors:
  - `400 BusinessRuleViolation` — the sale is already cancelled
  - `404 ResourceNotFound` — no sale has that identifier

#### PATCH /sales/{id}/items/{itemId}/cancel
- Description: Cancel one item, leaving the rest of the sale active
- Path Parameters:
  - `id`: Sale ID (uuid)
  - `itemId`: Sale item ID (uuid), as returned in the sale's `items`
- Response: the sale, with its total recalculated without the cancelled line
- Notes: the item is kept and flagged rather than removed. Raises `ItemCancelled`.
- Errors:
  - `400 BusinessRuleViolation` — the sale or the item is already cancelled
  - `404 ResourceNotFound` — no such sale, or the sale holds no such item

### Discount rules

Applied per product, across the whole sale:

| Quantity | Discount |
|---|---|
| 1–3 | none |
| 4–9 | 10% |
| 10–20 | 20% |
| 21+ | rejected |

A product may appear only once per sale, which is what makes "identical items"
unambiguous.

### Events

| Event | Raised when |
|---|---|
| `SaleCreated` | a sale is registered |
| `SaleModified` | a sale is updated |
| `SaleCancelled` | a whole sale is cancelled |
| `ItemCancelled` | one line is cancelled |

Published after the transaction commits, to the application log and, when enabled,
to the MongoDB `sale_events` collection.

<br/>
<div style="display: flex; justify-content: space-between;">
  <a href="./general-api.md">Previous: General API</a>
  <a href="./project-structure.md">Next: Project Structure</a>
</div>
