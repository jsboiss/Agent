# React OpenAPI Workflow

Use this when a C# dashboard DTO or `/api/dashboard/*` endpoint changes.

## One-command path

From the repo root:

```powershell
cd src\Agent\ClientApp
.\update-api.ps1
```

The script builds the ASP.NET Core app, starts it on `http://localhost:5213` if needed, downloads the OpenAPI document, regenerates the Orval TypeScript client, type-checks React, and rebuilds the static UI assets.

## Normal change flow

1. Change the C# dashboard DTOs, services, or endpoint signatures.
2. Make sure endpoint names stay stable with `.WithName(...)`; Orval uses those names for generated hook names.
3. Run `src\Agent\ClientApp\update-api.ps1`.
4. Fix any TypeScript errors in handwritten TSX.
5. Do not edit `src\Agent\ClientApp\src\api\generated.ts` manually.

## Mental model

C# owns the contract and application behavior. OpenAPI publishes that contract. Orval turns the OpenAPI document into typed TypeScript fetch hooks. TSX imports the generated hooks and focuses on rendering, browser state, layout state, and interactions.

If a field appears wrong in React, fix the C# DTO or endpoint first, then regenerate the client.
