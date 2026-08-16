using Microsoft.AspNetCore.Mvc;
using BehaviorDocumentation.FourFlows.Models;
using BehaviorDocumentation.FourFlows.Services;

namespace BehaviorDocumentation.FourFlows.Controllers;

/// <summary>
/// Negative probes only: never admitted flows because the actions carry no HTTP verb attribute.
/// <see cref="AmbiguousSwitchProbe"/> reaches two admitted outcome helpers (NotFound and Conflict) in
/// one arm, so its switch must fail closed with no status-switch arm fact.
/// <see cref="UnusedTerminalProbe"/> has an admitted switch, but its discarded StatusCode(500)
/// invocation outside every arm body never flows to a method return, so it must never become a
/// direct terminal outcome.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProbeController : ControllerBase
{
    private readonly IWidgetService _service;

    public ProbeController(IWidgetService service)
    {
        _service = service;
    }

    public ActionResult<bool> AmbiguousSwitchProbe(int id)
    {
        var result = _service.CancelAsync(id, "probe").GetAwaiter().GetResult();

        switch (result.Status)
        {
            case WidgetResultStatus.NotFound:
                if (id > 0)
                {
                    return NotFound();
                }

                return Conflict();
            case WidgetResultStatus.Conflict:
                return NotFound();
            default:
                return Ok(true);
        }
    }

    /// <summary>
    /// Admitted status switch preceded by a discarded StatusCode(500) invocation whose result never
    /// flows to a method return. The unused helper must not be emitted as a DirectTerminalOutcome;
    /// only a helper compiler-proven to flow directly to a return may become a direct terminal.
    /// </summary>
    public ActionResult<bool> UnusedTerminalProbe(int id)
    {
        var result = _service.CancelAsync(id, "probe").GetAwaiter().GetResult();

        // Discarded: this StatusCode(500) result never flows to a return and is not an outcome.
        StatusCode(500);

        switch (result.Status)
        {
            case WidgetResultStatus.NotFound:
                return NotFound();
            case WidgetResultStatus.Conflict:
                return Conflict();
            default:
                return Ok(true);
        }
    }
}
