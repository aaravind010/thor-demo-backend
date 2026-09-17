using Microsoft.AspNetCore.Mvc;
using Thor.TaskApi.Models;

namespace Thor.TaskApi.Controllers;

// Scaffold controller: exists so the Thor.Api /task-api proxy has a real downstream to
// hit end-to-end. Replace with real task/workflow-control endpoints.
[ApiController]
[Route("tasks")]
public class TasksController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<TaskSummary>> Get() =>
        Ok(Array.Empty<TaskSummary>());
}
