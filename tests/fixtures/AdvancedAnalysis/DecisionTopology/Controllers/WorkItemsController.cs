using Microsoft.AspNetCore.Mvc;
using AdvancedAnalysis.DecisionTopology.Models;
using AdvancedAnalysis.DecisionTopology.Services;

namespace AdvancedAnalysis.DecisionTopology.Controllers;

/// <summary>
/// Frozen accepted contract guard fixture controller. The service owns the guard/terminal topology; the controller
/// maps the exact service result to HTTP outcomes.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class WorkItemsController : ControllerBase
{
    private readonly WorkItemService _service;

    public WorkItemsController(WorkItemService service)
    {
        _service = service;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkItem>> Process(int id)
    {
        var result = await _service.ProcessAsync(id);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.ErrorMessage });
        }

        return Ok(result.Data);
    }
}
