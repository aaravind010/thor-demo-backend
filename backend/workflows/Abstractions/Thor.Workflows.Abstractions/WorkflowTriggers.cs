namespace Thor.Workflows.Abstractions;

/// <summary>Known <see cref="Thor.DataLayer.Models.Tenants.WorkflowEntity.Trigger"/> values.</summary>
public static class WorkflowTriggers
{
    /// <summary>Scan-driven chain (ingestion → ATRE → ownership → PIA → control engine), orchestrated by Step Functions.</summary>
    public const string StepFunctions = "step_functions";

    /// <summary>A single workflow invoked standalone via API, with no scan/manifest context.</summary>
    public const string Api = "api";
}
