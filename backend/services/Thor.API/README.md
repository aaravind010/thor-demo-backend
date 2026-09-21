# Thor Backend APIs

API interface for handling all authenticated incoming traffic from clients.

## API Versioning

Versioning is handled by `Asp.Versioning.Mvc`, configured in `Program.cs`.

- **Scheme:** URL segment (`/v{version}/...`), e.g. `/v1/authentication-methods`.
- **Default version:** `1.0` — assumed when no version segment is present.
- **Supported versions:** `1.0` (`Controllers/V1`).

### Response headers

- `api-version` — the version that actually served the request (e.g. `1.0`).
- `api-supported-versions` / `api-deprecated-versions` — emitted automatically
  (`ReportApiVersions = true`) listing all versions the matched endpoint supports/deprecates.

### Adding a new version

1. Add a `Controllers/V{n}` folder with a controller decorated with `[ApiVersion("{n}.0")]`
   and `[Route("v{version:apiVersion}/...")]`.
2. Deprecate old versions via `[ApiVersion("1.0", Deprecated = true)]` rather than removing them.
