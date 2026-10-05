using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Thor.Api.Constants;
using Thor.Api.Models;
using Thor.Api.Services;
using Thor.DataConnectionManager.Exceptions;

namespace Thor.Api.Controllers.V1;

/// <summary>Graph (Neptune) reads of a group's relationships to other entities.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/groups/{id:guid}")]
public class GroupRelationshipsController(GroupRelationshipService relationshipService) : ControllerBase
{
    // The Lambda authorizer validates the caller and sets X-THOR-TENANT-ID; requests reaching
    // Thor.Api are trusted, so the header is read directly rather than re-verified here.

    /// <summary>Directly connected entities, with the edges linking them to this group.</summary>
    /// <param name="direction"><c>out</c> (edges from the group), <c>in</c> (edges to it) or <c>both</c>.</param>
    /// <param name="relTypes">Edge labels to include (repeatable); all labels when omitted.</param>
    [HttpGet("relationships")]
    public async Task<ActionResult<CursorPage<RelationshipResponse>>> GetRelationships(
        Guid id,
        [FromQuery] string direction = "both",
        [FromQuery(Name = "relType")] string[]? relTypes = null,
        [FromQuery] Guid? after = null,
        [FromQuery] int limit = PagingConstants.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetTenantId(out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        // All three run (not a ?? chain) so both out values are definitely assigned.
        var error = new[]
        {
            RelationshipQueryValidator.TryParseDirection(direction, out var graphDirection),
            RelationshipQueryValidator.TryParseRelTypes(relTypes, out var parsedRelTypes),
            RelationshipQueryValidator.ValidateLimit(limit),
        }.FirstOrDefault(e => e is not null);
        if (error is not null)
        {
            return BadRequest(error);
        }

        return await Run(() => relationshipService.GetRelationshipsAsync(
            tenantId, id, graphDirection, parsedRelTypes, after, limit, cancellationToken));
    }

    /// <summary>Accounts and groups that are members of this group.</summary>
    /// <param name="transitive">Also include members of nested member groups, up to <paramref name="maxDepth"/> levels.</param>
    [HttpGet("members")]
    public async Task<ActionResult<CursorPage<ReachableEntityResponse>>> GetMembers(
        Guid id,
        [FromQuery] bool transitive = false,
        [FromQuery] int? maxDepth = null,
        [FromQuery] Guid? after = null,
        [FromQuery] int limit = PagingConstants.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetTenantId(out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        var error = RelationshipQueryValidator.TryResolveDepth(transitive, maxDepth, out var depth)
            ?? RelationshipQueryValidator.ValidateLimit(limit);
        if (error is not null)
        {
            return BadRequest(error);
        }

        return await Run(() => relationshipService.GetMembersAsync(tenantId, id, depth, after, limit, cancellationToken));
    }

    /// <summary>Groups this group is a member of.</summary>
    /// <param name="transitive">Also follow the parents' own memberships, up to <paramref name="maxDepth"/> levels.</param>
    [HttpGet("parents")]
    public async Task<ActionResult<CursorPage<ReachableEntityResponse>>> GetParents(
        Guid id,
        [FromQuery] bool transitive = false,
        [FromQuery] int? maxDepth = null,
        [FromQuery] Guid? after = null,
        [FromQuery] int limit = PagingConstants.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetTenantId(out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        var error = RelationshipQueryValidator.TryResolveDepth(transitive, maxDepth, out var depth)
            ?? RelationshipQueryValidator.ValidateLimit(limit);
        if (error is not null)
        {
            return BadRequest(error);
        }

        return await Run(() => relationshipService.GetParentsAsync(tenantId, id, depth, after, limit, cancellationToken));
    }

    /// <summary>Assets the group can access, directly or through its own (nested) parent groups.</summary>
    /// <param name="maxDepth">Levels of group nesting to follow.</param>
    [HttpGet("access")]
    public async Task<ActionResult<CursorPage<AccessResponse>>> GetAccess(
        Guid id,
        [FromQuery] int? maxDepth = null,
        [FromQuery] Guid? after = null,
        [FromQuery] int limit = PagingConstants.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetTenantId(out var tenantId))
        {
            return BadRequest($"Missing or invalid '{TenantConstants.TenantHeaderName}' header.");
        }

        var error = RelationshipQueryValidator.TryResolveDepth(transitive: true, maxDepth, out var depth)
            ?? RelationshipQueryValidator.ValidateLimit(limit);
        if (error is not null)
        {
            return BadRequest(error);
        }

        return await Run(() => relationshipService.GetAccessAsync(tenantId, id, depth, after, limit, cancellationToken));
    }

    private bool TryGetTenantId(out Guid tenantId)
    {
        tenantId = Guid.Empty;
        return Request.Headers.TryGetValue(TenantConstants.TenantHeaderName, out var tenantHeaderValue) &&
            Guid.TryParse(tenantHeaderValue, out tenantId);
    }

    // A null page means the group doesn't exist in this tenant.
    private async Task<ActionResult<T>> Run<T>(Func<Task<T?>> read) where T : class
    {
        try
        {
            var response = await read();
            return response is null ? NotFound() : Ok(response);
        }
        catch (TenantNotFoundException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
    }
}
