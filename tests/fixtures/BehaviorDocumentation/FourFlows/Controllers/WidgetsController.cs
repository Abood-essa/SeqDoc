using Microsoft.AspNetCore.Mvc;
using BehaviorDocumentation.FourFlows.Models;
using BehaviorDocumentation.FourFlows.Services;

namespace BehaviorDocumentation.FourFlows.Controllers;

/// <summary>
/// Generic controller proving the four admitted flows. The Get flow branches on the result IsSuccess
/// member; Cancel, Reserve, and Update switch over the compiler-proven status enum and map each arm to
/// an exact ASP.NET Core outcome helper. The Reserve default arm returns CreatedAtAction targeting the
/// GetById action, proving the unique Get link.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class WidgetsController : ControllerBase
{
    private readonly IWidgetService _service;

    public WidgetsController(IWidgetService service)
    {
        _service = service;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Widget>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.ErrorMessage });
        }

        return Ok(result.Data);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<bool>> Cancel(int id, string reason)
    {
        var result = await _service.CancelAsync(id, reason);

        switch (result.Status)
        {
            case WidgetResultStatus.NotFound:
                return NotFound();
            case WidgetResultStatus.Conflict:
                return Conflict();
            default:
                return Ok(result.Data);
        }
    }

    [HttpPost("{id}/reservations")]
    public async Task<ActionResult<Reservation>> Reserve(int id, int quantity, DateTime forDate)
    {
        var result = await _service.ReserveAsync(id, quantity, forDate);

        switch (result.Status)
        {
            case WidgetResultStatus.NotFound:
                return NotFound();
            case WidgetResultStatus.ValidationError:
                return BadRequest();
            default:
                return CreatedAtAction(nameof(GetById), new { id = result.Data!.WidgetId }, result.Data);
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Widget>> Update(int id, UpdateWidgetCommand command)
    {
        var result = await _service.UpdateAsync(id, command);

        switch (result.Status)
        {
            case WidgetResultStatus.NotFound:
                return NotFound();
            case WidgetResultStatus.Conflict:
                return Conflict();
            case WidgetResultStatus.ValidationError:
                return BadRequest();
            default:
                return Ok(result.Data);
        }
    }
}
