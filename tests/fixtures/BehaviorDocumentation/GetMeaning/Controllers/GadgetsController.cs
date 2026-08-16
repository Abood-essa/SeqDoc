using Microsoft.AspNetCore.Mvc;
using BehaviorDocumentation.GetMeaning.Services;

namespace BehaviorDocumentation.GetMeaning.Controllers;

/// <summary>
/// Unrelated Get flow controller. The action calls the DI-resolved service, branches on the result
/// IsSuccess member, and returns an exact 404 on the failure path and an exact 200 on the success
/// path, mirroring the admitted TicketReservation Get shape without any shared vocabulary.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class GadgetsController : ControllerBase
{
    private readonly IGadgetService _service;

    public GadgetsController(IGadgetService service)
    {
        _service = service;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<string>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.ErrorMessage });
        }

        return Ok(result.Data!.Label);
    }

    [HttpGet("token/{token}")]
    public async Task<ActionResult<string>> FindByToken(Guid token)
    {
        var result = await _service.FindByTokenAsync(token);

        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.ErrorMessage });
        }

        return Ok(result.Data!.Label);
    }
}
