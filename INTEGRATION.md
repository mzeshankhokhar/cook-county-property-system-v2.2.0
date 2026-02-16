# Cook County Property API - Integration Guide

## Overview

This project provides a property information system for Cook County, Illinois that aggregates data from four county sources into structured JSON via a REST API. A standalone vanilla JavaScript UI component renders the data as either a single-screen card dashboard or a slide-in drawer panel -- no React, no build step required.

**Live demo:** `/enterprise-drawer/demo.html`
**API documentation (Swagger):** `/api-docs`

### Key Capabilities

- **4 parallel API endpoints** returning structured JSON for any Cook County PIN
- **PostgreSQL cache layer** with 7-day TTL -- cached PINs respond in <5ms
- **Progressive loading** -- the UI fires all 4 requests in parallel and renders each card as its data arrives
- **Dual-mode UI component** -- dashboard grid or drawer panel, configurable at runtime
- **Bulk import** -- upload CSV files with thousands of PINs for background cache warming

---

## API Reference

All endpoints accept a `pin` query parameter in the format `XX-XX-XXX-XXX-XXXX` (14 digits with dashes). All responses follow a standard envelope:

```json
{
  "success": true,
  "data": { ... },
  "cached": true,            // only present when served from PostgreSQL cache
  "cachedAt": "2025-02-13T...", // only present when served from cache
  "stale": true              // only present when cache entry is expired but returned as fallback
}
```

When served from a live fetch (cache miss), the response contains only `success` and `data`. The `cached`, `cachedAt`, and `stale` fields are only added when the response comes from the PostgreSQL cache layer.

Error responses:

```json
{
  "success": false,
  "error": "Human-readable message",
  "code": "ERROR_CODE"
}
```

**Error Codes:**

| Code | HTTP Status | Meaning |
|------|-------------|---------|
| `INVALID_PIN` | 400 | PIN is missing or not in `XX-XX-XXX-XXX-XXXX` format |
| `FETCH_ERROR` | 502 | Failed to fetch data from the county source website |
| `DOTNET_UNAVAILABLE` | 502 | The .NET backend service is not running |

---

### 1. Tax Portal Data

**`GET /api/cook/tax-portal-data?pin=XX-XX-XXX-XXX-XXXX`**

Returns property information, characteristics, tax bills, tax sale history, and a property photo from the Cook County Property Info portal.

**Typical latency:** ~500ms live | <5ms cached

**Response `data` object:**

```json
{
  "pin": "01-01-120-006-0000",
  "propertyInfo": {
    "address": "525 ANDOVER ST",
    "city": "CHICAGO HEIGHTS",
    "zip": "60411",
    "township": "BLOOM",
    "mailingName": "SMITH, JOHN",
    "mailingAddress": "525 ANDOVER ST",
    "mailingCityStateZip": "CHICAGO HEIGHTS IL 60411"
  },
  "characteristics": {
    "assessedValue": "$5,432",
    "estimatedValue": "$54,320",
    "lotSize": "6,250",
    "buildingSize": "1,200",
    "propertyClass": "2-03",
    "propertyClassDescription": "One Story Residence, Any Age, up to 999 SF",
    "taxRate": "26.543",
    "taxCode": "31011",
    "assessmentPass": "PASS 1"
  },
  "taxBills": {
    "bills": [
      {
        "year": "2023",
        "amount": "$2,847.62",
        "paymentStatus": "Paid",
        "amountDue": "$0.00",
        "exemptionsReceived": 2
      }
    ]
  },
  "taxSaleDelinquencies": {
    "entries": [
      {
        "year": "2019",
        "status": "No Tax Sale",
        "details": null
      }
    ]
  },
  "propertyImageBase64": "data:image/jpeg;base64,/9j/4AAQ...",
  "error": null
}
```

The `error` field is `null` on success; if a partial failure occurs during fetching or parsing, it contains a descriptive error string while the remaining fields may still be populated.

**Field Details:**

- `propertyInfo`: Address, city, ZIP, township, and mailing contact information
- `characteristics`: Assessed/estimated values, lot/building sizes, property class, tax rate/code
- `taxBills.bills[]`: Array of tax bill records with year, billed amount, payment status, amount due, and exemption count
- `taxSaleDelinquencies.entries[]`: Array of tax sale entries with year and status (e.g., "No Tax Sale", "Sold", "Not Occurred")
- `propertyImageBase64`: Base64-encoded JPEG property photo (prefixed with `data:image/jpeg;base64,`)

---

### 2. County Clerk Data

**`GET /api/cook/clerk-data?pin=XX-XX-XXX-XXX-XXXX`**

Returns sold tax and delinquent tax information from the Cook County Clerk's tax delinquency system.

**Typical latency:** ~370ms live | <5ms cached

**Response `data` object:**

```json
{
  "pin": "01-01-120-006-0000",
  "delinquentTaxes": {
    "dataAsOf": "01/15/2025",
    "soldTaxes": [
      {
        "taxSale": "2019 Annual",
        "fromYearToYear": "2018-2018",
        "status": "Redeemed",
        "statusDocNumber": "1234567",
        "date": "03/15/2021",
        "comment": null
      }
    ],
    "delinquentTaxes": [
      {
        "taxYear": "2022",
        "status": "Open",
        "forfeitDate": "05/01/2025",
        "firstInstallmentBalance": "$1,423.81",
        "secondInstallmentBalance": "$1,423.81",
        "type": "Regular",
        "warrantYear": null
      }
    ],
    "totalTaxBalanceDue1st": "$1,423.81",
    "totalTaxBalanceDue2nd": "$1,423.81"
  },
  "error": null
}
```

The `error` field is `null` on success; if a partial failure occurs it contains a descriptive error string.

**Field Details:**

- `delinquentTaxes.dataAsOf`: Date the data was last updated in the clerk's system
- `delinquentTaxes.soldTaxes[]`: Array of tax sale records with sale name, year range, status ("Redeemed", "Forfeited", etc.), document number, and date
- `delinquentTaxes.delinquentTaxes[]`: Array of unpaid tax years with status, forfeit date, installment balances, and type
- `totalTaxBalanceDue1st` / `totalTaxBalanceDue2nd`: Sum of outstanding 1st and 2nd installment balances

---

### 3. Recorder Data

**`GET /api/cook/recorder-data?pin=XX-XX-XXX-XXX-XXXX`**

Returns recorded documents (deeds, mortgages, liens, etc.) from the Cook County Recorder of Deeds.

**Typical latency:** ~400ms live | <5ms cached

**Response `data` object:**

```json
{
  "pin": "01-01-120-006-0000",
  "recorderDocuments": {
    "totalDocuments": 12,
    "propertyAddress": "525 ANDOVER ST",
    "city": "CHICAGO HEIGHTS",
    "zipcode": "60411",
    "documents": [
      {
        "docNumber": "2312345678",
        "dateRecorded": "06/15/2023",
        "dateExecuted": "06/10/2023",
        "docType": "WARRANTY DEED",
        "consideration": "$150,000.00",
        "viewUrl": "https://crs.cookcountyclerkil.gov/..."
      }
    ]
  },
  "error": null
}
```

The `error` field is `null` on success; if a partial failure occurs it contains a descriptive error string.

**Field Details:**

- `recorderDocuments.totalDocuments`: Total number of recorded documents for this PIN
- `recorderDocuments.documents[]`: Array of document records with doc number, recording/execution dates, document type (e.g., WARRANTY DEED, MORTGAGE, RELEASE), consideration amount, and a link to view the document
- `viewUrl`: Direct link to view the document on the Cook County Recorder website

---

### 4. CookViewer GIS + Google Maps Data

**`GET /api/cook/cookviewer-data?pin=XX-XX-XXX-XXX-XXXX`**

Returns a composited GIS satellite map with parcel boundaries, parcel geometry coordinates, and (if `GOOGLE_API_KEY` is configured) Google satellite and Street View images.

**Typical latency:** ~2.1s live | <5ms cached

**Response `data` object:**

```json
{
  "pin": "01-01-120-006-0000",
  "cookViewerMap": {
    "mapImageBase64": "data:image/jpeg;base64,...",
    "mapImageUrl": "https://services.arcgisonline.com/...",
    "parcelOverlayBase64": "data:image/png;base64,...",
    "parcelAddress": "525 ANDOVER ST",
    "centerLat": 41.506,
    "centerLon": -87.635,
    "parcelRings": [[[41.506, -87.635], ...]],
    "parcelRingsWebMercator": [[[-9753000, 5081000], ...]],
    "mapBbox": [-9753100, 5080900, -9752900, 5081100],
    "mapWidth": 400,
    "mapHeight": 300,
    "googleSatelliteImageBase64": "data:image/png;base64,...",
    "googleStreetViewImageBase64": "data:image/jpeg;base64,..."
  },
  "error": null
}
```

The `error` field is `null` on success; if a partial failure occurs it contains a descriptive error string.

**Field Details:**

- `mapImageBase64`: ESRI World Imagery satellite tile (JPEG, base64-encoded)
- `mapImageUrl`: Direct URL to the ESRI tile (for debugging or alternative rendering)
- `parcelOverlayBase64`: Transparent PNG from Cook County GIS Layer 44 showing parcel boundary lines
- `centerLat` / `centerLon`: WGS84 centroid coordinates of the parcel
- `parcelRings`: Parcel polygon vertices in WGS84 (lat/lon) coordinates
- `parcelRingsWebMercator`: Parcel polygon vertices in Web Mercator projection (for SVG overlay rendering)
- `mapBbox`: Bounding box of the map image in Web Mercator coordinates `[xmin, ymin, xmax, ymax]`
- `mapWidth` / `mapHeight`: Pixel dimensions of the map image
- `googleSatelliteImageBase64`: Google Maps satellite image (requires `GOOGLE_API_KEY`)
- `googleStreetViewImageBase64`: Google Street View image (requires `GOOGLE_API_KEY`)

**GIS Map Composition:** The UI component composites three layers using CSS absolute positioning:
1. Base satellite image (`mapImageBase64`)
2. Parcel boundary overlay (`parcelOverlayBase64`)
3. SVG polygon highlight computed from `parcelRingsWebMercator` + `mapBbox`

---

### 5. Additional Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/cook/property-summary?pin=` | GET | All-in-one summary combining all 4 sources into a single response (slower than parallel, but one request) |
| `/api/cook/google-maps-data?lat=&lon=` | GET | Standalone Google Maps satellite + Street View imagery by latitude/longitude |
| `/api/cook/clear-cache?pin=` | POST | Clear cached data for a specific PIN. Omit `pin` to clear all cached data. |
| `/api/health` | GET | Service health check. Returns status of both Express proxy and .NET backend. |
| `/api/import/upload` | POST | Upload CSV of PINs for bulk cache warming. Multipart form upload, field name: `file`. Max file size: 10MB. |
| `/api/import/pins` | GET | Returns a sorted array of all distinct imported PINs. Use this to populate `pinList` for arrow navigation. |
| `/api/import/jobs` | GET | List all import jobs with their status and progress. |
| `/api/import/jobs/:jobId` | GET | Get detailed status of a specific import job. |
| `/api/pins/:pin/bids` | GET | Get saved Bid and Overbid values for a PIN. Returns `null` for fields not yet set. |
| `/api/pins/:pin/bids` | PUT | Save Bid and Overbid values for a PIN. Accepts JSON body `{ bid, overbid }`. Values are upserted (created or updated). |
| `/api-docs` | GET | Swagger UI with interactive API documentation for all .NET endpoints. |

### Bid / Overbid API

Users can record a Bid and Overbid amount for each property PIN. These values persist in a `pin_bids` PostgreSQL table keyed by PIN.

**GET** `/api/pins/:pin/bids`

```json
{
  "success": true,
  "data": {
    "pin": "01-01-120-006-0000",
    "bid": "10.50",
    "overbid": "0.25",
    "updatedAt": "2025-02-13T12:00:00.000Z"
  }
}
```

Fields `bid` and `overbid` are `null` when no value has been saved yet.

**PUT** `/api/pins/:pin/bids`

Request body (JSON):

```json
{
  "bid": "10.50",
  "overbid": "0.25"
}
```

Both fields are optional. Send an empty string `""` or `null` to clear a value. Values must be valid numbers (integers or decimals). Returns the saved record on success.

---

## UI Component Integration

The UI component is a single vanilla JavaScript file (`property-drawer.js` v2.2.0) with a companion CSS file. No React, no build step, no bundler required. It works alongside Bootstrap 5 and the Lato font.

### Required Files

```
enterprise-drawer/property-drawer.js   (JS module, ~985 lines)
enterprise-drawer/property-drawer.css  (Styles for dashboard + drawer modes, ~850 lines)
```

### Prerequisites

Include these in your HTML `<head>`:

```html
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Lato:wght@300;400;600;700&display=swap">
<link rel="stylesheet" href="property-drawer.css">
```

Include the JS before `</body>`:

```html
<script src="property-drawer.js"></script>
```

### Design Tokens

The component reads these CSS custom properties from your enterprise stylesheet. Override them to match your brand:

```css
:root {
  --bg-primary-clr: #0056A1;     /* Primary brand color (header backgrounds, links) */
  --text-primary-clr: #444444;   /* Default text color */
  --text-secondary-clr: #888888; /* Secondary/muted text */
  --border-clr: #DDDDDD;         /* Border color for cards and tables */
}
```

---

### Option A: Dashboard Mode (Single-Screen Card Grid)

Renders a compact card grid inside a container element on your page. Best for full-page or main-content-area property views.

**Layout:** 5-card top row (Property Info, Characteristics, Tax Bills, GIS Map, Google Maps) | full-width Tax Sale row | 3-card bottom row (Sold Taxes, Delinquent Taxes, Recorded Documents).

**Minimal integration example:**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css">
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Lato:wght@400;600;700&display=swap">
  <link rel="stylesheet" href="property-drawer.css">
  <style>
    :root {
      --bg-primary-clr: #0056A1;
      --text-primary-clr: #444444;
      --text-secondary-clr: #888888;
      --border-clr: #DDDDDD;
    }
    body { font-family: 'Lato', sans-serif; }
  </style>
</head>
<body>
  <div id="pd-dashboard"></div>
  <script src="property-drawer.js"></script>
  <script>
    PropertyDrawer.configure({
      apiBaseUrl: 'https://your-api-host.com',
      mode: 'dashboard',
      dashboardContainerId: 'pd-dashboard'
    });
    PropertyDrawer.open('01-01-120-006-0000');
  </script>
</body>
</html>
```

**Important:** The target container element (e.g., `<div id="pd-dashboard">`) must exist in the DOM before calling `PropertyDrawer.open()`. If the element is not found, the component silently does nothing.

The dashboard is responsive:
- Above 1200px: 5-card top row, full-width middle, 3-card bottom
- 768px - 1200px: top row collapses to 3 columns
- Below 768px: all cards stack vertically

To switch to a different property, call `PropertyDrawer.open()` again with a new PIN. The existing cards are replaced and new data loads progressively.

---

### Option B: Drawer Mode (Slide-In Panel)

Renders a slide-in drawer panel anchored to the right side of the viewport. Best for overlaying property details on top of a map or list view.

The component dynamically creates `.pd-drawer-backdrop`, `.drawer-main`, `.drawer-header`, and `.drawer-body` elements inside the drawer container. All animation CSS, backdrop overlay, and body scroll locking are built in -- no additional CSS or JavaScript is required from the host page.

**Built-in behaviors:**

- **Slide-in animation:** The drawer slides in from the right using a 0.3s CSS transform transition with `cubic-bezier(0.4, 0, 0.2, 1)` easing. Closing reverses the animation.
- **Backdrop overlay:** A semi-transparent backdrop (`rgba(0,0,0,0.35)`) appears behind the drawer at z-index 9998 (drawer is at 9999). Clicking the backdrop closes the drawer.
- **Body scroll lock:** When the drawer opens, `document.body.style.overflow` is set to `hidden` to prevent background scrolling. It is restored when the drawer closes.
- **In-place navigation:** When navigating between PINs using the prev/next arrows or keyboard shortcuts (Left/Right arrow keys), the drawer content swaps in place without replaying the slide-in animation. Only the initial open triggers the animation.
- **Close timer guard:** A tracked `closeTimer` prevents race conditions during rapid open/close cycles.

**Two-row drawer header:**

| Row | Contents |
|-----|----------|
| Top row | "Property Details" title, PIN search input with Search button, close (&times;) button |
| Bottom row | PIN display, "Last Updated" label, Bid/Overbid input fields, navigation arrows with counter |

**Minimal integration example:**

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css">
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Lato:wght@400;600;700&display=swap">
  <link rel="stylesheet" href="property-drawer.css">
  <style>
    :root {
      --bg-primary-clr: #0056A1;
      --text-primary-clr: #444444;
      --text-secondary-clr: #888888;
      --border-clr: #DDDDDD;
    }
    body { font-family: 'Lato', sans-serif; }
  </style>
</head>
<body>
  <!-- Drawer container: the component populates this element -->
  <div id="property-drawer-container"></div>

  <button onclick="PropertyDrawer.open('01-01-120-006-0000')">
    Open Property Drawer
  </button>

  <script src="property-drawer.js"></script>
  <script>
    PropertyDrawer.configure({
      apiBaseUrl: 'https://your-api-host.com',
      mode: 'drawer',
      onOpen: function(pin) {
        console.log('Drawer opened for PIN:', pin);
      },
      onClose: function() {
        console.log('Drawer closed');
      }
    });
  </script>
</body>
</html>
```

No custom CSS is needed for the drawer animation, backdrop, or header layout -- `property-drawer.css` handles everything. The drawer container element (`<div id="property-drawer-container">`) must exist in the DOM; the component fills it dynamically.

**Drawer search and navigation:** The drawer header includes a built-in search bar for looking up different PINs without closing the drawer. When `pinList` is configured, prev/next arrows and a counter appear in the bottom header row. Arrow key navigation (Left/Right) also works via keyboard.

**Drawer CSS classes (defined in `property-drawer.css`):**

| Selector | Description |
|----------|-------------|
| `.pd-drawer-backdrop` | Full-screen backdrop overlay (z-index 9998). Transitions from transparent to `rgba(0,0,0,0.35)` via `.open` class. |
| `.drawer-main.property-drawer` | Fixed-position drawer panel (z-index 9999). Slides in via `transform: translateX(100%)` / `.open` sets `translateX(0)`. Width: `calc(100% - 60px)`, collapses to `100%` below 576px. |
| `.drawer-header` | Primary-color header with `--bg-primary-clr` background. Contains `.drawer-header-top` and `.drawer-header-bottom`. |
| `.drawer-header-top` | Flex row: title, search input + button, close button. |
| `.drawer-header-bottom` | Flex row with darker tint (`rgba(0,0,0,0.12)`): PIN display, last-updated label, bid/overbid inputs, navigation arrows. |
| `.drawer-body` | Scrollable content area containing the card grid. |
| `.drawer-close-btn` | White &times; button, 22px, with hover opacity transition. |
| `.drawer-header-bottom .pd-bid-input` | Compact input (70px wide, 24px tall) with translucent white background for Bid/Overbid fields. |
| `.drawer-header-bottom .pd-nav-btn` | Compact arrow button (26x24px) with translucent white background. |

---

### Last Updated Indicator

Both dashboard and drawer modes display a "Last Updated: MM/DD/YYYY" label next to the PIN. This date tells the user how recent the displayed data is:

- **Cached data:** The date reflects when the data was originally fetched and stored in the PostgreSQL cache (from the `cachedAt` field in the API response envelope).
- **Live data:** If the data was fetched live (cache miss), the current date is used.
- **Multiple sources:** Since 4 API calls are made in parallel, the component displays the **oldest** date among all successful responses -- giving the user a worst-case freshness indicator.
- **All errors:** If all 4 requests fail, no date is shown.

The label updates progressively as each API response arrives.

---

### Public JavaScript API

```javascript
PropertyDrawer.configure(options)   // Set configuration (call once at page load)
PropertyDrawer.open(pin)            // Load and display data for a PIN
PropertyDrawer.close()              // Close/clear the current view
PropertyDrawer.search(pin)          // Alias for open()
PropertyDrawer.setPinList(pins)     // Set or update the list of PINs for arrow navigation
PropertyDrawer.navigatePrev()       // Navigate to the previous PIN in pinList
PropertyDrawer.navigateNext()       // Navigate to the next PIN in pinList
PropertyDrawer.version              // Returns "2.2.0"
```

**Configuration Options:**

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `apiBaseUrl` | string | `''` | Base URL for the API server. Use `''` for same-origin. |
| `mode` | string | `'drawer'` | Rendering mode: `'dashboard'` or `'drawer'` |
| `dashboardContainerId` | string | `'pd-dashboard'` | DOM element ID where dashboard cards render (dashboard mode only) |
| `containerId` | string | `'property-drawer-container'` | DOM element ID for the drawer container (drawer mode only) |
| `pinList` | string[] | `[]` | Array of PINs for prev/next arrow navigation. When non-empty, navigation arrows appear. |
| `onOpen` | function | `null` | Callback fired when a property is loaded. Receives `pin` as argument. |
| `onClose` | function | `null` | Callback fired when the view is closed. No arguments. |
| `onNavigate` | function | `null` | Callback fired when navigating via arrows. Receives `(pin, index, total)`. |

**PIN Format:** Must match `XX-XX-XXX-XXX-XXXX` (e.g., `01-01-120-006-0000`). The component validates the format before making API calls and logs a warning to the console for invalid PINs.

---

### Security Measures

The UI component applies these protections to all rendered content:

- **`escapeHtml()`** -- All user-supplied and API-returned strings are HTML-escaped before DOM insertion. Escapes `&`, `<`, `>`, `"`, and `'` to prevent XSS attacks.
- **`sanitizeImageSrc()`** -- Image `src` attributes only accept `data:image/*` base64 strings or `https://` URLs. Blocks `javascript:`, `http:`, `data:text/*`, and all other schemes.
- **PIN validation** -- Regex `^\d{2}-\d{2}-\d{3}-\d{3}-\d{4}$` is enforced before any API call is made. Invalid PINs are silently rejected with a console warning.
- **AbortController** -- When a new PIN is opened, any in-flight requests for the previous PIN are automatically cancelled to prevent stale data from rendering.

---

## Caching & Performance

### Two-Layer Cache Architecture

1. **PostgreSQL persistent cache** (Express proxy layer, `server/storage.ts`)
   - **7-day TTL** -- cached entries older than 7 days are considered stale
   - **Cache-first:** if valid (non-stale) cached data exists for a PIN+source, it is returned immediately without contacting the .NET backend
   - **Write-through:** live fetch results from the .NET backend are stored in the cache via UPSERT for concurrent write safety
   - **Stale fallback:** if a live fetch fails but stale cached data exists, the stale data is returned with `"stale": true` in the response envelope
   - **Response envelope additions:** cached responses include `"cached": true` and `"cachedAt": "<ISO timestamp>"`

2. **.NET in-memory cache** (backend layer)
   - **5-minute TTL** for rapid access
   - Prevents repeated fetches to county websites during burst traffic for the same PIN

### Typical Response Times

| Source | Live Fetch | Cached (PostgreSQL) |
|--------|-----------|---------------------|
| Tax Portal | ~500ms | <5ms |
| County Clerk | ~370ms | <5ms |
| Recorder | ~400ms | <5ms |
| CookViewer + Google Maps | ~2.1s | <5ms |

### Cache Management

**Clear cache for a specific PIN:**
```bash
curl -X POST "https://your-host.com/api/cook/clear-cache?pin=01-01-120-006-0000"
```

**Clear all cached data:**
```bash
curl -X POST "https://your-host.com/api/cook/clear-cache"
```

### Bulk Import (Cache Warming)

Upload a CSV file with PINs to pre-populate the cache for all 4 data sources:

```bash
curl -X POST https://your-host.com/api/import/upload \
  -F "file=@pins.csv"
```

The CSV can contain PINs in any column, in `XX-XX-XXX-XXX-XXXX` or raw 14-digit format. The system automatically extracts valid PINs from any text content and processes them in the background with controlled concurrency.

**Import job tracking:**
```bash
# List all import jobs
curl https://your-host.com/api/import/jobs

# Check status of a specific job
curl https://your-host.com/api/import/jobs/<jobId>
```

---

## System Architecture

```
Browser / Enterprise App
    |
    |  4 parallel fetch() calls
    v
Express Proxy (port 5000)
    |  - Serves frontend + static files
    |  - PostgreSQL cache layer (7-day TTL)
    |  - Proxies to .NET API on cache miss
    v
.NET 8 / ASP.NET Core (port 5001)
    |  - HTML parsing (HtmlAgilityPack)
    |  - GIS map image composition
    |  - 5-minute in-memory cache
    v
Cook County Sources
    +-- cookcountypropertyinfo.com (Tax Portal)
    +-- taxdelinquent.cookcountyclerkil.gov (Clerk)
    +-- crs.cookcountyclerkil.gov (Recorder)
    +-- maps.cookcountyil.gov (CookViewer GIS)
```

### Running the Services

Two workflows must be running simultaneously:

1. **Start application** (`npm run dev`) -- Express proxy on port 5000, serves frontend and proxies API calls
2. **Start .NET API** (`cd dotnet-api/CookCountyApi && dotnet run`) -- .NET backend on port 5001, fetches and parses county data

### Key Files

| File | Purpose |
|------|---------|
| `enterprise-drawer/property-drawer.js` | Standalone UI component v2.2.0 |
| `enterprise-drawer/property-drawer.css` | Component styles for both dashboard and drawer modes |
| `enterprise-drawer/demo.html` | Working demo page (dashboard mode) |
| `enterprise-drawer/INTEGRATION.md` | This integration guide |
| `server/routes.ts` | Express proxy routes, cache logic, import endpoints |
| `server/storage.ts` | PostgreSQL cache and import job storage interface |
| `dotnet-api/CookCountyApi/` | .NET backend -- controllers, services, HTML parsers |
| `dotnet-api/CookCountyApi/Models/ApiResponses.cs` | All API response type definitions |

### Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `DATABASE_URL` | Yes | PostgreSQL connection string (auto-configured on Replit) |
| `GOOGLE_API_KEY` | No | Google Maps API key for satellite and Street View imagery. If not set, the GIS card still works (uses ESRI tiles) but Google Maps imagery will be unavailable. |
| `SESSION_SECRET` | No | Session secret for any session-based features |
