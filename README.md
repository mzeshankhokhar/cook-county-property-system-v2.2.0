# Cook County Property Information System v2.2.0

## Package Contents

This archive contains the complete source code, documentation, and database schema
for the Cook County Property Information System. It is organized into the following
folders for easy navigation by your development and QA teams.

### Folder Structure

```
README.md                          <- This file
INTEGRATION.md                     <- Full API + UI integration guide (start here)
DATABASE-SCHEMA.sql                <- PostgreSQL table definitions (4 tables)

ui-component/                      <- Standalone vanilla JS component (no build step)
  property-drawer.js               <- UI module v2.2.0 (~985 lines)
  property-drawer.css              <- Styles for dashboard + drawer modes (~850 lines)
  demo.html                        <- Working demo page

express-proxy/server/              <- Node.js/Express proxy layer (TypeScript)
  index.ts                         <- Express app entry point (port 5000)
  routes.ts                        <- API routes, PostgreSQL caching, import endpoints
  storage.ts                       <- Database storage interface (cache CRUD, imports, bids)
  db.ts                            <- Drizzle ORM database connection
  cook-proxy.ts                    <- .NET backend proxy helpers
  vite.ts                          <- Vite dev server integration (dev mode only)
  static.ts                        <- Static file serving (production only)

dotnet-api/                        <- .NET 8 / ASP.NET Core backend
  Program.cs                       <- App configuration, Swagger, CORS, in-memory cache
  CookCountyApi.csproj             <- Project file with NuGet dependencies
  Controllers/
    CookCountyController.cs        <- API controller (tax-portal, clerk, recorder, GIS)
  Services/
    CookCountyProxyService.cs      <- HTML fetching + parsing from county websites
    PropertySummaryService.cs      <- Combined property summary aggregation
  Models/
    ApiResponses.cs                <- All API response type definitions
  appsettings.json                 <- Production config
  appsettings.Development.json     <- Development config
  Properties/
    launchSettings.json            <- Launch profiles (port 5001)

database/                          <- Database schema definitions
  schema.ts                        <- Drizzle ORM schema (TypeScript)
  drizzle.config.ts                <- Drizzle Kit configuration

config/                            <- Build and runtime configuration
  package.json                     <- npm dependencies and scripts
  tsconfig.json                    <- TypeScript compiler configuration
  vite.config.ts                   <- Vite bundler configuration
  tailwind.config.ts               <- Tailwind CSS configuration
  postcss.config.js                <- PostCSS configuration
  build.ts                         <- Production build script (esbuild + Vite)
```

## Architecture Overview

```
Browser / Enterprise App
    |
    |  4 parallel fetch() calls
    v
Express Proxy (port 5000)
    |  - Serves UI component + static files
    |  - PostgreSQL cache layer (7-day TTL)
    |  - Proxies to .NET API on cache miss
    v
.NET 8 / ASP.NET Core (port 5001)
    |  - HTML parsing (HtmlAgilityPack + Playwright)
    |  - GIS map image composition
    |  - 5-minute in-memory cache
    v
Cook County Sources
    +-- cookcountypropertyinfo.com (Tax Portal)
    +-- taxdelinquent.cookcountyclerkil.gov (Clerk)
    +-- crs.cookcountyclerkil.gov (Recorder)
    +-- maps.cookcountyil.gov (CookViewer GIS)
```

## Quick Start

### Prerequisites

- Node.js 20+
- .NET 8 SDK
- PostgreSQL (any version supporting jsonb)

### Environment Variables

| Variable        | Required | Description |
|-----------------|----------|-------------|
| DATABASE_URL    | Yes      | PostgreSQL connection string |
| GOOGLE_API_KEY  | No       | Google Maps API key for satellite/Street View imagery |
| SESSION_SECRET  | No       | Express session secret |

### Running the Services

Two processes must run simultaneously:

1. **Express Proxy** (port 5000):
   ```bash
   npm ci
   npm run db:push          # Create/update database tables
   npm run dev              # Start Express + Vite dev server
   ```

2. **.NET Backend** (port 5001):
   ```bash
   cd dotnet-api
   dotnet restore
   dotnet run               # Start .NET API
   ```

### Production Build

```bash
npm run build              # Builds client (Vite) + server (esbuild)
npm run start              # Starts production server
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| /api/cook/tax-portal-data?pin= | GET | Property info, characteristics, tax bills, photos |
| /api/cook/clerk-data?pin= | GET | Sold tax and delinquent tax information |
| /api/cook/recorder-data?pin= | GET | Recorded documents (deeds, mortgages, liens) |
| /api/cook/cookviewer-data?pin= | GET | GIS map, parcel geometry, Google Maps imagery |
| /api/cook/property-summary?pin= | GET | All-in-one summary (all 4 sources combined) |
| /api/cook/clear-cache?pin= | POST | Clear cached data (specific PIN or all) |
| /api/import/upload | POST | Upload CSV for bulk cache warming |
| /api/import/jobs | GET | List import jobs |
| /api/pins/:pin/bids | GET/PUT | Read/save Bid and Overbid values |
| /api-docs | GET | Swagger UI for .NET API |

PIN format: `XX-XX-XXX-XXX-XXXX` (e.g., `01-01-120-006-0000`)

See INTEGRATION.md for full API response schemas, UI component setup, CSS tokens,
and detailed integration examples.

## UI Component

The standalone vanilla JS component (`property-drawer.js` v2.2.0) supports:

- **Dashboard Mode**: Full-page card grid with header (PIN, bid/overbid, nav arrows)
- **Drawer Mode**: Slide-in panel with built-in CSS animation, semi-transparent backdrop overlay, body scroll lock, and two-row header

Key drawer behaviors:
- Slide-in animation (0.3s cubic-bezier) on initial open only
- In-place content swap on prev/next navigation (no animation replay)
- Backdrop click-to-close with body scroll locking
- Keyboard navigation (Left/Right arrow keys)

No React, no build step. Just include the JS and CSS files.
See INTEGRATION.md > "UI Component Integration" for complete setup instructions.

## Database Schema

The system uses 4 PostgreSQL tables (see DATABASE-SCHEMA.sql):

| Table | Purpose |
|-------|---------|
| property_cache | Cached API responses per PIN+source (7-day TTL) |
| pin_bids | User-entered Bid and Overbid values per PIN |
| import_jobs | Bulk import job tracking (status, progress) |
| import_pins | Individual PINs within an import job |

## Testing / QA

- **Live demo**: Navigate to `/enterprise-drawer/demo.html` on the running server
- **Swagger API docs**: Navigate to `/api-docs` on the running server
- **Database**: Run `npm run db:push` to create tables, then use any PostgreSQL client
- **Cache warming**: Upload a CSV with PINs via `/api/import/upload` to pre-populate cache
- **API testing**: Use curl or Postman against the endpoints listed above
- **PIN for testing**: `32-19-324-024-0000` (used in the demo page)

### QA Checklist

- [ ] Both services start without errors (Express on 5000, .NET on 5001)
- [ ] `/api/health` returns status of both services
- [ ] Tax Portal, Clerk, Recorder, CookViewer endpoints return data for a valid PIN
- [ ] Invalid PIN format returns 400 with INVALID_PIN error code
- [ ] Cache hit returns `cached: true` and responds in <5ms
- [ ] Dashboard mode renders all 9 cards with progressive loading
- [ ] Drawer mode slides in with animation, backdrop appears
- [ ] Drawer close animates out, backdrop fades
- [ ] Arrow navigation swaps content without animation replay
- [ ] Keyboard Left/Right arrows navigate between PINs
- [ ] Bid/Overbid values save and persist across page reloads
- [ ] Search bar in drawer header loads new PIN data
- [ ] Bulk CSV import processes PINs in background
- [ ] `/api-docs` shows Swagger UI for .NET endpoints
