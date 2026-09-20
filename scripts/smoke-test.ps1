<#
.SYNOPSIS
    Exercises every endpoint of the DeveloperStore API and asserts the results.

.DESCRIPTION
    The .http file next to the WebApi project is the readable catalogue of requests,
    but running it end to end means copying ids from one response into the next by
    hand. This script chains the same calls automatically and checks what comes back,
    so a reviewer can confirm the whole surface works with one command.

    It is a smoke test against a running instance, not a replacement for the test
    suite: `dotnet test` covers the same rules without needing a database. What this
    adds is proof that the wiring holds once PostgreSQL, the HTTP stack and the
    serializer are all in play.

    The script authenticates first and sends a bearer token with everything after
    that. It registers its own account to do so, because registration is the one
    write route that stays open - a token comes from POST /api/auth, which needs an
    account, which only POST /api/users can create.

    The assertions about refusing unauthenticated callers deliberately send no
    token, and the ones about bad tokens send a token this API never issued.

.PARAMETER BaseUrl
    Where the API is listening. Defaults to the plain-HTTP port used by `dotnet run`,
    which avoids the self-signed certificate that the HTTPS port presents.
    Use http://localhost:8080 for the docker compose stack.

.PARAMETER KeepData
    Leave the created sales and users in place instead of deleting them at the end.
    Useful when you want to inspect the rows, or the MongoDB audit trail, afterwards.

.EXAMPLE
    ./scripts/smoke-test.ps1

.EXAMPLE
    ./scripts/smoke-test.ps1 -BaseUrl http://localhost:8080 -KeepData

.NOTES
    Requires the API to be running with a reachable PostgreSQL database.
    Runs on Windows PowerShell 5.1 and on PowerShell 7+ (Linux and macOS included).
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5119',
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')

# ---------------------------------------------------------------------------
# Fixtures
#
# External Identities: the API stores a foreign aggregate's id alongside a
# denormalized description, and never resolves it against another service. Any
# Guid is therefore acceptable here - these are stand-ins for records that would
# live in a Customers, Branches and Products service.
# ---------------------------------------------------------------------------

$CustomerId     = '3fa85f64-5717-4562-b3fc-2c963f66afa6'
$BranchId       = '7c9e6679-7425-40de-944b-e07fc1f90ae7'
$ProductId      = '550e8400-e29b-41d4-a716-446655440000'
$OtherProductId = '550e8400-e29b-41d4-a716-446655440001'

# Scopes every sale number and email address to this run, so the script can be run
# repeatedly against the same database without tripping the uniqueness rules it is
# meant to be testing deliberately.
$RunId = (Get-Date).ToString('yyMMddHHmmss')

# ---------------------------------------------------------------------------
# Plumbing
# ---------------------------------------------------------------------------

$script:Passed = 0
$script:Failed = 0
$script:Failures = @()

# The bearer token, obtained in the Authentication section below and sent with every
# request after that. Empty until then, which is what lets the health check and the
# registration call run before there is anything to authenticate with.
$script:Token = $null

<#
.SYNOPSIS
    Sends a request and returns its status and parsed body without ever throwing.
.DESCRIPTION
    Invoke-WebRequest treats any non-2xx response as a terminating error, which is
    useless here: half of these assertions are about 400, 401, 404 and 409 responses
    and their bodies. This wrapper catches that and reports the status and payload
    the same way for every outcome.
#>
function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        $Body,
        # Sends the request without the bearer token, for the assertions that are
        # about what an unauthenticated caller gets.
        [switch]$Anonymous,
        # Sends this token instead of the one acquired at sign-in, for the
        # assertions about tokens the API should reject.
        [string]$BearerToken
    )

    $params = @{
        Method          = $Method
        Uri             = "$BaseUrl$Path"
        UseBasicParsing = $true
        ErrorAction     = 'Stop'
    }

    # Every sale route, and reading or deleting a user, requires a token. It is
    # attached here rather than at each call site so that a new request cannot
    # accidentally be written without it.
    $tokenToSend = if ($BearerToken) { $BearerToken } elseif (-not $Anonymous) { $script:Token } else { $null }

    if ($tokenToSend) {
        $params.Headers = @{ Authorization = "Bearer $tokenToSend" }
    }

    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        # Depth 10 because the default of 2 would flatten a sale's items into
        # the literal string "System.Collections.Hashtable".
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }

    $status  = 0
    $content = $null

    try {
        $response = Invoke-WebRequest @params
        $status   = [int]$response.StatusCode
        $content  = $response.Content
    }
    catch {
        $webResponse = $_.Exception.Response
        if ($webResponse) { $status = [int]$webResponse.StatusCode }

        # PowerShell 7 puts the response body in ErrorDetails; 5.1 usually does too,
        # but falls back to the raw stream when the content type confuses it.
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            $content = $_.ErrorDetails.Message
        }
        elseif ($webResponse -and $webResponse.PSObject.Methods.Name -contains 'GetResponseStream') {
            $reader  = New-Object System.IO.StreamReader($webResponse.GetResponseStream())
            $content = $reader.ReadToEnd()
            $reader.Dispose()
        }

        if ($status -eq 0) {
            throw "Could not reach $BaseUrl$Path. Is the API running? ($($_.Exception.Message))"
        }
    }

    $parsed = $null
    if ($content) {
        try { $parsed = $content | ConvertFrom-Json } catch { $parsed = $null }
    }

    [pscustomobject]@{
        Status = $status
        Body   = $parsed
        Raw    = $content
    }
}

<#
.SYNOPSIS
    Records one assertion.
#>
function Assert-That {
    param(
        [Parameter(Mandatory)][string]$Description,
        # Not mandatory, and defaulted: an expression that evaluates to $null - a
        # field the API did not return, say - would otherwise stall a mandatory
        # [bool] on a parameter prompt. Here it simply reads as a failure, which is
        # both true and non-blocking in CI.
        [bool]$Condition = $false,
        [string]$Detail = ''
    )

    if ($Condition) {
        $script:Passed++
        Write-Host '  [ok]   ' -ForegroundColor Green -NoNewline
        Write-Host $Description
    }
    else {
        $script:Failed++
        $script:Failures += $Description
        Write-Host '  [FAIL] ' -ForegroundColor Red -NoNewline
        Write-Host $Description
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
    }
}

function Write-Section {
    param([Parameter(Mandatory)][string]$Title)
    Write-Host ''
    Write-Host $Title -ForegroundColor Cyan
    Write-Host ('-' * $Title.Length) -ForegroundColor DarkCyan
}

<#
.SYNOPSIS
    Builds a create-sale body.
.NOTES
    The comma before @($Items) keeps a single-item list an array through
    ConvertTo-Json. Without it a one-line sale can serialize as an object and the
    request fails to bind.
#>
function New-SaleBody {
    param(
        [Parameter(Mandatory)][string]$SaleNumber,
        [Parameter(Mandatory)][array]$Items,
        [string]$CustomerName = 'Maria Silva',
        [string]$BranchName   = 'Downtown'
    )

    @{
        saleNumber   = $SaleNumber
        saleDate     = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        customerId   = $CustomerId
        customerName = $CustomerName
        branchId     = $BranchId
        branchName   = $BranchName
        items        = @($Items)
    }
}

function New-ItemBody {
    param(
        [Parameter(Mandatory)][string]$ProductId,
        [Parameter(Mandatory)][int]$Quantity,
        [decimal]$UnitPrice = 100.00,
        [string]$Title = 'Fjallraven Foldsack No. 1 Backpack'
    )

    @{
        productId    = $ProductId
        productTitle = $Title
        quantity     = $Quantity
        unitPrice    = $UnitPrice
    }
}

<#
.SYNOPSIS
    Compares two money or rate values.
.DESCRIPTION
    JSON numbers arrive as doubles, so an exact -eq against a decimal literal can
    fail on a value that is correct to the cent. Casting both sides to decimal and
    comparing exactly is safe at this scale.
#>
function Test-Amount {
    param($Actual, [decimal]$Expected)
    if ($null -eq $Actual) { return $false }
    return ([decimal]$Actual -eq $Expected)
}

# ---------------------------------------------------------------------------
# Health
# ---------------------------------------------------------------------------

Write-Host ''
Write-Host "DeveloperStore API smoke test  ->  $BaseUrl" -ForegroundColor White

Write-Section 'Health'

$health = Invoke-Api -Method GET -Path '/health'
Assert-That 'GET /health answers 200' ($health.Status -eq 200) "got $($health.Status)"

Assert-That '  ... without a token' ($health.Status -eq 200) `
    'a liveness probe that needed credentials would report the service down whenever authentication broke'

if ($health.Status -ne 200) {
    Write-Host ''
    Write-Host 'The API is not healthy. Start it first:' -ForegroundColor Yellow
    Write-Host '  docker compose up -d' -ForegroundColor Yellow
    Write-Host '  dotnet run --project src/Ambev.DeveloperEvaluation.WebApi' -ForegroundColor Yellow
    exit 1
}

# ---------------------------------------------------------------------------
# Authentication
#
# This runs first because everything after it needs a token. The sale routes and
# the user read/delete routes carry [Authorize]; registration and this exchange
# are the two ways in.
# ---------------------------------------------------------------------------

Write-Section 'Authentication'

# Before the token exists, the protected routes must say so - and say it in the
# documented shape rather than as an empty 401.
$refused = Invoke-Api -Method GET -Path '/api/sales' -Anonymous
Assert-That 'a sale route refuses an unauthenticated caller' ($refused.Status -eq 401) `
    "got $($refused.Status)"
Assert-That '  ... with the documented error body, not an empty 401' `
    ($null -ne $refused.Body -and $refused.Body.type -eq 'AuthenticationError') `
    "body=$($refused.Raw)"

$email = "smoke-$RunId@example.com"

# Registration is deliberately open: a token comes from /api/auth, which needs an
# account, which only this route can create.
$bootstrap = Invoke-Api -Method POST -Path '/api/users' -Anonymous -Body @{
    username = 'smokerunner'
    email    = $email
    phone    = '+5511999998888'
    password = 'Str0ng!Pass1'
    status   = 'Active'
    role     = 'Customer'
}

Assert-That 'registration is open to an unauthenticated caller' ($bootstrap.Status -eq 201) `
    "got $($bootstrap.Status): $($bootstrap.Raw)"

$bootstrapUserId = $bootstrap.Body.data.id

$login = Invoke-Api -Method POST -Path '/api/auth' -Anonymous -Body @{
    email    = $email
    password = 'Str0ng!Pass1'
}

Assert-That 'POST /api/auth issues a token' ($login.Status -eq 200) "got $($login.Status): $($login.Raw)"

$script:Token = $login.Body.data.token

Assert-That '  ... shaped like a JWT' (($script:Token -split '\.').Count -eq 3) `
    'the token is not header.payload.signature'

if (-not $script:Token) {
    Write-Host ''
    Write-Host 'No token was issued, so the protected routes cannot be exercised.' -ForegroundColor Yellow
    exit 1
}

# Same route, same verb. Only the Authorization header changed.
$allowed = Invoke-Api -Method GET -Path '/api/sales'
Assert-That 'the same route succeeds once the token is sent' ($allowed.Status -eq 200) `
    "got $($allowed.Status)"

# Structurally a JWT and genuinely signed - just not by this application. Proves
# the signature is verified rather than the token merely being parsed.
$foreign = Invoke-Api -Method GET -Path '/api/sales' -BearerToken (
    'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9' +
    '.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ' +
    '.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c')

Assert-That 'a token signed with another key is refused' ($foreign.Status -eq 401) `
    "got $($foreign.Status)"

$garbage = Invoke-Api -Method GET -Path '/api/sales' -BearerToken 'not.a.real.token'
Assert-That 'a malformed token is refused' ($garbage.Status -eq 401) "got $($garbage.Status)"

# ---------------------------------------------------------------------------
# Discount tiers - the brief's acceptance criteria
#
# 1-3   no discount      "purchases below 4 items cannot have a discount"
# 4-9   10%              "4+ items: 10%"
# 10-20 20%              "between 10 and 20 identical items: 20%"
# 21+   refused          "it's not possible to sell above 20 identical items"
#
# The boundaries are what matter, so every one of them is checked rather than a
# sample from the middle of each band.
# ---------------------------------------------------------------------------

Write-Section 'Discount tiers'

$tiers = @(
    @{ Quantity = 1;  Rate = 0.00; Discount = 0.00;   Total = 100.00  }
    @{ Quantity = 3;  Rate = 0.00; Discount = 0.00;   Total = 300.00  }
    @{ Quantity = 4;  Rate = 0.10; Discount = 40.00;  Total = 360.00  }
    @{ Quantity = 9;  Rate = 0.10; Discount = 90.00;  Total = 810.00  }
    @{ Quantity = 10; Rate = 0.20; Discount = 200.00; Total = 800.00  }
    @{ Quantity = 20; Rate = 0.20; Discount = 400.00; Total = 1600.00 }
)

$createdSaleIds = @()

foreach ($tier in $tiers) {
    $quantity = $tier.Quantity
    $number   = "SMOKE-$RunId-Q$quantity"
    $body     = New-SaleBody -SaleNumber $number -Items (New-ItemBody -ProductId $ProductId -Quantity $quantity)
    $response = Invoke-Api -Method POST -Path '/api/sales' -Body $body

    if ($response.Status -ne 201) {
        Assert-That "$quantity x 100.00 is accepted" $false "got $($response.Status): $($response.Raw)"
        continue
    }

    $sale = $response.Body.data
    $item = $sale.items[0]
    $createdSaleIds += $sale.id

    $rateOk  = Test-Amount $item.discountRate $tier.Rate
    $discOk  = Test-Amount $item.discount     $tier.Discount
    $totalOk = Test-Amount $sale.totalAmount  $tier.Total

    Assert-That ("{0,2} x 100.00  ->  rate {1:P0}, discount {2}, total {3}" -f $quantity, $tier.Rate, $tier.Discount, $tier.Total) `
        ($rateOk -and $discOk -and $totalOk) `
        "rate=$($item.discountRate) discount=$($item.discount) total=$($sale.totalAmount)"
}

# Above the ceiling. The domain refuses this rather than silently capping it.
$overLimit = Invoke-Api -Method POST -Path '/api/sales' `
    -Body (New-SaleBody -SaleNumber "SMOKE-$RunId-Q21" -Items (New-ItemBody -ProductId $ProductId -Quantity 21))

Assert-That '21 x 100.00  ->  refused with 400' ($overLimit.Status -eq 400) "got $($overLimit.Status)"
Assert-That '  ... and names the rule that refused it' `
    ($overLimit.Body.type -in @('BusinessRuleViolation', 'ValidationError')) `
    "type=$($overLimit.Body.type) detail=$($overLimit.Body.detail)"

# ---------------------------------------------------------------------------
# Business rules beyond the discount table
# ---------------------------------------------------------------------------

Write-Section 'Business rules'

# The 20-unit ceiling is per product across the whole sale, not per line. Two
# lines of 20 would sell 40 units of one product, so the sale is refused.
$splitLines = Invoke-Api -Method POST -Path '/api/sales' -Body (New-SaleBody `
    -SaleNumber "SMOKE-$RunId-SPLIT" `
    -Items @(
        (New-ItemBody -ProductId $ProductId -Quantity 20 -UnitPrice 10.00),
        (New-ItemBody -ProductId $ProductId -Quantity 20 -UnitPrice 10.00)
    ))

Assert-That 'the same product twice in one sale is refused' ($splitLines.Status -eq 400) `
    "got $($splitLines.Status): $($splitLines.Raw)"

# A sale number identifies the sale to the business, so it cannot repeat.
$duplicate = Invoke-Api -Method POST -Path '/api/sales' `
    -Body (New-SaleBody -SaleNumber "SMOKE-$RunId-Q3" -Items (New-ItemBody -ProductId $ProductId -Quantity 1))

Assert-That 'a repeated sale number answers 409' ($duplicate.Status -eq 409) "got $($duplicate.Status)"
Assert-That '  ... typed as ResourceConflict' ($duplicate.Body.type -eq 'ResourceConflict') `
    "type=$($duplicate.Body.type)"

$noItems = Invoke-Api -Method POST -Path '/api/sales' `
    -Body @{
        saleNumber   = "SMOKE-$RunId-EMPTY"
        saleDate     = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        customerId   = $CustomerId
        customerName = 'Maria Silva'
        branchId     = $BranchId
        branchName   = 'Downtown'
        items        = @()
    }

Assert-That 'a sale with no items is refused' ($noItems.Status -eq 400) "got $($noItems.Status)"

# ---------------------------------------------------------------------------
# Error contract
#
# .doc/general-api.md fixes one shape for every failure. A second shape leaking
# through - a bare array of validation failures, or ASP.NET's ProblemDetails -
# is the defect these assertions exist to catch.
# ---------------------------------------------------------------------------

Write-Section 'Error contract'

Assert-That 'a validation failure is an object, not an array' `
    ($noItems.Body -isnot [array] -and $null -ne $noItems.Body.type) `
    "body=$($noItems.Raw)"

Assert-That '  ... carrying type, error and detail' `
    ($null -ne $noItems.Body.type -and $null -ne $noItems.Body.error -and $null -ne $noItems.Body.detail) `
    "body=$($noItems.Raw)"

Assert-That '  ... and no ProblemDetails fields' `
    ($null -eq $noItems.Body.title -and $null -eq $noItems.Body.traceId) `
    "body=$($noItems.Raw)"

$unknown = Invoke-Api -Method GET -Path '/api/sales/11111111-1111-1111-1111-111111111111'
Assert-That 'an unknown sale answers 404 as ResourceNotFound' `
    ($unknown.Status -eq 404 -and $unknown.Body.type -eq 'ResourceNotFound') `
    "status=$($unknown.Status) type=$($unknown.Body.type)"

# ---------------------------------------------------------------------------
# Reading back
# ---------------------------------------------------------------------------

Write-Section 'Read and list'

$sampleId = $createdSaleIds[0]
$read = Invoke-Api -Method GET -Path "/api/sales/$sampleId"

Assert-That 'GET /api/sales/{id} answers 200' ($read.Status -eq 200) "got $($read.Status)"
Assert-That '  ... with the payload directly under data, not wrapped twice' `
    ($null -ne $read.Body.data.id -and $null -eq $read.Body.data.data) `
    "body=$($read.Raw)"
Assert-That '  ... and the External Identities carry their descriptions' `
    ($read.Body.data.customer.description -eq 'Maria Silva' -and $read.Body.data.branch.description -eq 'Downtown') `
    "customer=$($read.Body.data.customer.description) branch=$($read.Body.data.branch.description)"

# Served from Redis the second time when the cache is enabled, from PostgreSQL
# otherwise. Either way the answer must be identical - that is the whole contract
# of the cache, and the assertion that would catch a stale or malformed entry.
$reread = Invoke-Api -Method GET -Path "/api/sales/$sampleId"
Assert-That '  ... and a repeat read returns the same sale' `
    ($reread.Status -eq 200 -and $reread.Body.data.saleNumber -eq $read.Body.data.saleNumber) `
    "first=$($read.Body.data.saleNumber) second=$($reread.Body.data.saleNumber)"

$list = Invoke-Api -Method GET -Path '/api/sales?_page=1&_size=5'
Assert-That 'GET /api/sales answers 200' ($list.Status -eq 200) "got $($list.Status)"
Assert-That '  ... with the documented pagination envelope' `
    ($null -ne $list.Body.data -and $null -ne $list.Body.currentPage -and `
     $null -ne $list.Body.totalPages -and $null -ne $list.Body.totalCount) `
    "keys=$($list.Body.PSObject.Properties.Name -join ', ')"
Assert-That '  ... honouring _size' (@($list.Body.data).Count -le 5) `
    "returned $(@($list.Body.data).Count)"

# The space between field and direction is encoded, because a raw space in a URI
# is not something Invoke-WebRequest is obliged to fix up.
$ordered = Invoke-Api -Method GET -Path '/api/sales?_order=totalAmount%20desc&_size=50'
Assert-That 'GET /api/sales?_order sorts descending' `
    ($ordered.Status -eq 200 -and (
        @($ordered.Body.data).Count -lt 2 -or
        [decimal]$ordered.Body.data[0].totalAmount -ge [decimal]$ordered.Body.data[1].totalAmount)) `
    "status=$($ordered.Status)"

# A dotted path reaches into an External Identity value object.
$filtered = Invoke-Api -Method GET -Path '/api/sales?branch.name=Downtown&isCancelled=false&_size=50'
Assert-That 'GET /api/sales filters on a nested field' ($filtered.Status -eq 200) "got $($filtered.Status)"
Assert-That '  ... returning only matching rows' `
    (@($filtered.Body.data | Where-Object { $_.branch.description -ne 'Downtown' }).Count -eq 0) `
    'a row with a different branch came back'

# A trailing asterisk is a case-insensitive starts-with match.
$wildcard = Invoke-Api -Method GET -Path '/api/sales?customer.name=Maria*&_size=50'
Assert-That 'GET /api/sales supports a wildcard match' ($wildcard.Status -eq 200) "got $($wildcard.Status)"

$range = Invoke-Api -Method GET -Path '/api/sales?_minTotalAmount=400&_maxTotalAmount=1300&_size=50'
Assert-That 'GET /api/sales supports _min and _max bounds' ($range.Status -eq 200) "got $($range.Status)"
Assert-That '  ... with inclusive bounds' `
    (@($range.Body.data | Where-Object {
        [decimal]$_.totalAmount -lt 400 -or [decimal]$_.totalAmount -gt 1300
    }).Count -eq 0) `
    'a row outside the range came back'

# ---------------------------------------------------------------------------
# Update, cancel, delete - and the events each of them raises
# ---------------------------------------------------------------------------

Write-Section 'Update, cancel and delete'

$workingNumber = "SMOKE-$RunId-WORK"
$created = Invoke-Api -Method POST -Path '/api/sales' -Body (New-SaleBody `
    -SaleNumber $workingNumber `
    -Items (New-ItemBody -ProductId $ProductId -Quantity 5))

Assert-That 'a sale to work on was created' ($created.Status -eq 201) "got $($created.Status)"

$workingId     = $created.Body.data.id
$originalItemId = $created.Body.data.items[0].id

# PUT replaces the whole sale. Keeping the same productId updates that line in
# place rather than dropping and re-adding it, which is what preserves the item id
# and keeps any reference to it valid.
$updated = Invoke-Api -Method PUT -Path "/api/sales/$workingId" -Body @{
    saleDate     = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    customerId   = $CustomerId
    customerName = 'Maria Silva Santos'
    branchId     = $BranchId
    branchName   = 'Uptown'
    items        = @(
        (New-ItemBody -ProductId $ProductId -Quantity 12),
        (New-ItemBody -ProductId $OtherProductId -Quantity 2 -UnitPrice 25.00 -Title 'Leather Wallet')
    )
}

Assert-That 'PUT /api/sales/{id} answers 200' ($updated.Status -eq 200) "got $($updated.Status): $($updated.Raw)"
Assert-That '  ... rebanding the quantity to the 20% tier' `
    (Test-Amount ($updated.Body.data.items | Where-Object { $_.product.id -eq $ProductId }).discountRate 0.20) `
    "rate=$(($updated.Body.data.items | Where-Object { $_.product.id -eq $ProductId }).discountRate)"
Assert-That '  ... keeping the id of the line whose product did not change' `
    (($updated.Body.data.items | Where-Object { $_.product.id -eq $ProductId }).id -eq $originalItemId) `
    'the item was replaced rather than updated in place'
Assert-That '  ... appending the new product as a second line' `
    (@($updated.Body.data.items).Count -eq 2) `
    "items=$(@($updated.Body.data.items).Count)"
Assert-That '  ... and stamping updatedAt' ($null -ne $updated.Body.data.updatedAt) `
    'updatedAt is still null after an update'

# Cancelling one line leaves the sale active and recalculates its total.
# Raises ItemCancelled.
$itemToCancel = ($updated.Body.data.items | Where-Object { $_.product.id -eq $OtherProductId }).id
$totalBefore  = [decimal]$updated.Body.data.totalAmount

$itemCancelled = Invoke-Api -Method PATCH -Path "/api/sales/$workingId/items/$itemToCancel/cancel"

Assert-That 'PATCH .../items/{itemId}/cancel answers 200' ($itemCancelled.Status -eq 200) "got $($itemCancelled.Status)"
Assert-That '  ... leaving the sale itself active' ($itemCancelled.Body.data.isCancelled -eq $false) `
    'the whole sale was cancelled'
Assert-That '  ... marking only that line cancelled' `
    ((($itemCancelled.Body.data.items | Where-Object { $_.id -eq $itemToCancel }).isCancelled) -eq $true) `
    'the line is not marked cancelled'
Assert-That '  ... and recalculating the total without it' `
    ([decimal]$itemCancelled.Body.data.totalAmount -lt $totalBefore) `
    "before=$totalBefore after=$($itemCancelled.Body.data.totalAmount)"

# Cancelling the sale voids the whole transaction but keeps the record, which is
# what an audit trail needs. Raises SaleCancelled.
$saleCancelled = Invoke-Api -Method PATCH -Path "/api/sales/$workingId/cancel"

Assert-That 'PATCH /api/sales/{id}/cancel answers 200' ($saleCancelled.Status -eq 200) "got $($saleCancelled.Status)"
Assert-That '  ... marking the sale cancelled' ($saleCancelled.Body.data.isCancelled -eq $true) `
    'isCancelled is still false'
Assert-That '  ... and stamping cancelledAt' ($null -ne $saleCancelled.Body.data.cancelledAt) `
    'cancelledAt is null'

$cancelTwice = Invoke-Api -Method PATCH -Path "/api/sales/$workingId/cancel"
Assert-That 'cancelling twice is refused' `
    ($cancelTwice.Status -eq 400 -and $cancelTwice.Body.type -eq 'BusinessRuleViolation') `
    "status=$($cancelTwice.Status) type=$($cancelTwice.Body.type)"

Write-Host ''
Write-Host '  note   SaleCreated, SaleModified, SaleCancelled and ItemCancelled were all' -ForegroundColor DarkGray
Write-Host '         raised by the calls above. Confirm them in the application log, and' -ForegroundColor DarkGray
Write-Host '         in the MongoDB sale_events collection when Mongo__Enabled is true.' -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
# Users
# ---------------------------------------------------------------------------

Write-Section 'Users'

# A second account, distinct from the one signed in with above, so that the
# create-response assertions are about a fresh registration rather than about the
# row the rest of the run depends on.
$secondEmail = "smoke-$RunId-second@example.com"
$userCreated = Invoke-Api -Method POST -Path '/api/users' -Anonymous -Body @{
    username = 'mariasilva'
    email    = $secondEmail
    phone    = '+5511999999999'
    password = 'Str0ng!Pass1'
    status   = 'Active'
    role     = 'Customer'
}

Assert-That 'POST /api/users answers 201' ($userCreated.Status -eq 201) "got $($userCreated.Status): $($userCreated.Raw)"

$userId = $userCreated.Body.data.id

Assert-That '  ... returning every field the response declares' `
    ($null -ne $userCreated.Body.data.id -and
     $userCreated.Body.data.name  -eq 'mariasilva' -and
     $userCreated.Body.data.email -eq $secondEmail -and
     $userCreated.Body.data.phone -eq '+5511999999999') `
    "body=$($userCreated.Raw)"

# Ordinals here would mean the API neither accepts nor produces the format its
# own documentation specifies.
Assert-That '  ... with enums as names, not ordinals' `
    ($userCreated.Body.data.role -eq 'Customer' -and $userCreated.Body.data.status -eq 'Active') `
    "role=$($userCreated.Body.data.role) status=$($userCreated.Body.data.status)"

Assert-That '  ... and never echoing the password' `
    ($null -eq $userCreated.Body.data.password) `
    'the response carried a password field'

$badUser = Invoke-Api -Method POST -Path '/api/users' -Anonymous -Body @{
    username = 'ab'
    email    = 'not-an-email'
    phone    = '123'
    password = 'weak'
    status   = 'Active'
    role     = 'Customer'
}

Assert-That 'an invalid user is refused with the error contract' `
    ($badUser.Status -eq 400 -and $badUser.Body -isnot [array] -and $badUser.Body.type -eq 'ValidationError') `
    "status=$($badUser.Status) body=$($badUser.Raw)"

$userRead = Invoke-Api -Method GET -Path "/api/users/$userId"
Assert-That 'GET /api/users/{id} answers 200' ($userRead.Status -eq 200) "got $($userRead.Status)"
Assert-That '  ... sourcing name from the entity username' ($userRead.Body.data.name -eq 'mariasilva') `
    "name=$($userRead.Body.data.name)"
Assert-That '  ... without double-wrapping the envelope' `
    ($null -ne $userRead.Body.data.id -and $null -eq $userRead.Body.data.data) `
    "body=$($userRead.Raw)"

$userMissing = Invoke-Api -Method GET -Path '/api/users/11111111-1111-1111-1111-111111111111'
Assert-That 'an unknown user answers 404 as ResourceNotFound' `
    ($userMissing.Status -eq 404 -and $userMissing.Body.type -eq 'ResourceNotFound') `
    "status=$($userMissing.Status) type=$($userMissing.Body.type)"

# Reading a user is protected even though registering is not.
$userReadAnonymous = Invoke-Api -Method GET -Path "/api/users/$userId" -Anonymous
Assert-That 'reading a user without a token answers 401' ($userReadAnonymous.Status -eq 401) `
    "got $($userReadAnonymous.Status)"

$userDeleteAnonymous = Invoke-Api -Method DELETE -Path "/api/users/$userId" -Anonymous
Assert-That 'deleting a user without a token answers 401' ($userDeleteAnonymous.Status -eq 401) `
    "got $($userDeleteAnonymous.Status)"

# ---------------------------------------------------------------------------
# Credentials
#
# The token exchange itself was covered in the Authentication section above.
# What is left is how the endpoint answers credentials it should refuse.
# ---------------------------------------------------------------------------

Write-Section 'Credentials'

$auth = Invoke-Api -Method POST -Path '/api/auth' -Anonymous -Body @{
    email    = $secondEmail
    password = 'Str0ng!Pass1'
}

Assert-That 'POST /api/auth answers 200' ($auth.Status -eq 200) "got $($auth.Status): $($auth.Raw)"
Assert-That '  ... returning a token' (-not [string]::IsNullOrWhiteSpace($auth.Body.data.token)) `
    'token is empty'
Assert-That '  ... alongside the user details' `
    ($auth.Body.data.email -eq $secondEmail -and $auth.Body.data.name -eq 'mariasilva') `
    "email=$($auth.Body.data.email) name=$($auth.Body.data.name)"

$wrongPassword = Invoke-Api -Method POST -Path '/api/auth' -Anonymous -Body @{
    email    = $secondEmail
    password = 'Wr0ng!Pass1'
}

Assert-That 'a wrong password answers 401 as AuthenticationError' `
    ($wrongPassword.Status -eq 401 -and $wrongPassword.Body.type -eq 'AuthenticationError') `
    "status=$($wrongPassword.Status) type=$($wrongPassword.Body.type)"

# 401 rather than 404, so the response does not reveal which addresses are
# registered.
$unknownAccount = Invoke-Api -Method POST -Path '/api/auth' -Anonymous -Body @{
    email    = "nobody-$RunId@example.com"
    password = 'Str0ng!Pass1'
}

Assert-That 'an unknown account answers 401, not 404' ($unknownAccount.Status -eq 401) `
    "got $($unknownAccount.Status)"

# ---------------------------------------------------------------------------
# Cleanup
# ---------------------------------------------------------------------------

if ($KeepData) {
    Write-Section 'Cleanup'
    Write-Host '  skipped (-KeepData). Sale numbers and the email are prefixed ' -NoNewline -ForegroundColor DarkGray
    Write-Host "SMOKE-$RunId" -ForegroundColor DarkGray
}
else {
    Write-Section 'Cleanup'

    # Delete is a hard delete. Cancelling is the business operation and is what a
    # real deployment would use; this is here so the script leaves no residue.
    $deleted = Invoke-Api -Method DELETE -Path "/api/sales/$workingId"
    Assert-That 'DELETE /api/sales/{id} answers 200' ($deleted.Status -eq 200) "got $($deleted.Status)"

    $afterDelete = Invoke-Api -Method GET -Path "/api/sales/$workingId"
    Assert-That '  ... and the sale is gone' ($afterDelete.Status -eq 404) "got $($afterDelete.Status)"

    foreach ($id in $createdSaleIds) {
        Invoke-Api -Method DELETE -Path "/api/sales/$id" | Out-Null
    }

    $userDeleted = Invoke-Api -Method DELETE -Path "/api/users/$userId"
    Assert-That 'DELETE /api/users/{id} answers 200' ($userDeleted.Status -eq 200) "got $($userDeleted.Status)"

    # The account signed in with, deleted last. The token stays usable after this -
    # a JWT is self-contained and is not checked against the table on each request -
    # which is why this can be the final call rather than needing special ordering.
    Invoke-Api -Method DELETE -Path "/api/users/$bootstrapUserId" | Out-Null
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Host ''
Write-Host ('=' * 60) -ForegroundColor DarkGray

if ($script:Failed -eq 0) {
    Write-Host "All $($script:Passed) assertions passed." -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "$($script:Passed) passed, $($script:Failed) failed." -ForegroundColor Red
Write-Host ''
Write-Host 'Failed:' -ForegroundColor Red
foreach ($failure in $script:Failures) {
    Write-Host "  - $failure" -ForegroundColor Red
}
Write-Host ''
exit 1
