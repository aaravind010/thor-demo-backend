# Thor.TaskAPI

Thor.TaskApi is the connector-facing service for managing tasks from connectors and tenant-scoped data
upload — the downstream that `Thor.Api`'s `/task-api` proxy forwards to. TaskAPI handles task creation and exposes endpoints for connectors to retrieve tasks and update task status.

See the repository-root `CLAUDE.md` for stack-wide conventions, the tenant-isolation/
non-negotiable principles, and repo layout.

## Stack
- **Framework:** ASP.NET Core Web API, .NET 10 (`Microsoft.NET.Sdk.Web`)
- **Data:** Can connect to both master and tenant database using the (`Thor.DataConnectionManager`)
- **Uploads:** AWS S3 presigned URLs (`AWSSDK.S3`)

## Layout
```
Controllers/    Endpoints related to tasks (creating scan configs, polling for tasks, updating task status and generating file upload URLs)
Services/       Service layer functions for the endpoints
Models/         Request/response DTOs
Constants/      Constants that are used in the application
Exceptions/     Custom exceptions
Program.cs      Main entry point
```

## Architecture, components, and functionality

The TaskAPI will be dockerized and deployed in AWS ECS.
Connectors authenticate against Thor.Api's `/register` / `/register/refresh` endpoints (not TaskAPI) to obtain a JWT; all endpoints in the TaskAPI are protected by that JWT.

The Task API provides endpoint for:
- creating scan configurations
- fetching tasks
- updating status of tasks
- updating task progress
- generating S3 presigned URL for file uploads

The TaskAPI connects to both the master and tenant specific databases.
The request header contains `x-thor-tenant-id`, which points to which database it should resolve to. This header value is added by the authorize lambda and can be trusted. All connections to database should be made using the `Thor.DataConnectionManager` library.

The ADR document contains further information: `docs\architecture\ADR.md`

## Project references in Thor.TaskApi

From `Thor.TaskApi.csproj`:

- **`Thor.DataConnectionManager`** — tenant routing/connection resolution shared across services.
  Thor.TaskApi registers the full stack (`ITenantRoutingResolver`, `ITenantConnectionValidator`,
  `TenantConnectionCache`, `ITenantDbContextFactory`, `ITenantConnectionManager`) in `Program.cs`,
  mirroring Thor.Api. `UploadService` still only resolves routing to confirm a tenant is
  provisioned before issuing a presigned URL; any endpoint that needs to read/write tenant data
  should inject `ITenantConnectionManager` and call `GetTenantDbContextAsync(tenantId)` for a
  validated, tenant-scoped `TenantDbContext` (see `AuthenticationMethodService` in Thor.Api for
  the pattern).

Package reference: **`AWSSDK.S3`** — used directly in `UploadService` (`IAmazonS3`,
`GetPreSignedUrlRequest`) to generate presigned upload URLs.
