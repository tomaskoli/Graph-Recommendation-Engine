# Recommendation Web Frontend

React-based frontend for browsing products and viewing graph-based recommendations.

## Tech Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| React | 19.x | UI framework |
| Vite | 7.x | Build tool & dev server |
| TypeScript | 5.9 | Type safety |
| openapi-typescript-codegen | 0.30.0 | API client generation |

## Project Structure

```
src/Recommendation.Web/
├── src/
│   ├── api/                    # Generated API client
│   │   ├── core/               # Request handling
│   │   ├── models/             # TypeScript interfaces
│   │   └── services/           # Service classes
│   ├── components/
│   │   ├── CategoryPanel/      # Expandable category sidebar
│   │   ├── ProductCard/        # Product card with IDs
│   │   ├── RecommendationList/ # Horizontal product list
│   │   └── SearchBox/          # Global search autocomplete
│   ├── pages/
│   │   ├── HomePage.tsx        # Category panel + products grid
│   │   └── ProductPage.tsx     # Product detail + parameters + recommendations
│   ├── services/
│   │   └── api.ts              # API facade with config
│   ├── App.tsx                 # Main app component
│   ├── App.css                 # App styles
│   ├── index.css               # Global styles + theme
│   └── main.tsx                # Entry point
├── package.json
├── vite.config.ts
└── tsconfig.json
```

---

## API Client Generation

The TypeScript API client is auto-generated from the OpenAPI spec.

### Generate Client

```bash
# Ensure API is running first
npm run generate-api
```

This runs two steps:
1. **Fetch spec**: Downloads `openapi.json` from the API
2. **Generate**: Creates TypeScript client in `src/api/`

### Generated Structure

```
src/api/
├── core/
│   ├── OpenAPI.ts          # Configuration (BASE URL)
│   ├── request.ts          # HTTP request handling
│   └── CancelablePromise.ts
├── models/
│   ├── ProductDto.ts
│   ├── CategoryDto.ts
│   ├── RecommendationDto.ts
│   ├── SearchResultDto.ts
│   └── ...
└── services/
    ├── ProductsService.ts
    ├── CategoriesService.ts
    ├── RecommendationsService.ts
    ├── SearchService.ts
    └── SegmentsService.ts
```

## Scripts

| Script | Description |
|--------|-------------|
| `npm run dev` | Start dev server (port 5173) |
| `npm run build` | Production build |
| `npm run preview` | Preview production build |
| `npm run lint` | ESLint check |
| `npm run generate-api` | Regenerate API client |
