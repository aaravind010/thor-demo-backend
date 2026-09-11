using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Models.Tenants;

/// <summary>
/// A parent/child edge between two <see cref="WorkflowEntity"/> rows, modeling
/// workflow orchestration as a DAG. Lives in the per-tenant database.
/// </summary>
[Table("workflow_graph")]
[PrimaryKey(nameof(ParentWorkflowId), nameof(ChildWorkflowId))]
public sealed class WorkflowGraph
{
    [Column("parent_workflow_id")]
    public Guid ParentWorkflowId { get; set; }

    [Column("child_workflow_id")]
    public Guid ChildWorkflowId { get; set; }

    [ForeignKey(nameof(ParentWorkflowId))]
    public WorkflowEntity ParentWorkflow { get; set; } = null!;

    [ForeignKey(nameof(ChildWorkflowId))]
    public WorkflowEntity ChildWorkflow { get; set; } = null!;
}
