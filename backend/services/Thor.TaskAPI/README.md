# Thor Task API

API interface to handle everything related to scan server, its configuration, scheduling scan and get tasks for scan servers.

## API Versioning

Versioning is handled by `Asp.Versioning.Mvc`, configured in `Program.cs`.

- **Scheme:** URL segment (`/v{version}/...`), e.g. `/v1/tasks`, `/v2/tasks`.
- **Default version:** `1.0` — assumed when no version segment is present.
- **Supported versions:** `1.0` (`Controllers/V1`), `2.0` (`Controllers/V2`).
- Not all endpoints are versioned — e.g. `uploads` (`Controllers/V1/UploadsController.cs`) has no
  version segment and is served regardless of API version.

### Response headers

- `api-version` — the version that actually served the request (e.g. `1.0`).
- `api-supported-versions` / `api-deprecated-versions` — emitted automatically
  (`ReportApiVersions = true`) listing all versions the matched endpoint supports/deprecates.

### Adding a new version

1. Add a `Controllers/V{n}` folder with a controller decorated with `[ApiVersion("{n}.0")]`
   and `[Route("v{version:apiVersion}/...")]`.
2. Deprecate old versions via `[ApiVersion("1.0", Deprecated = true)]` rather than removing them.