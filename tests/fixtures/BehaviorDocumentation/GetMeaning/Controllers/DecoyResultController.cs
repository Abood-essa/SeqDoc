using Microsoft.AspNetCore.Mvc;
using BehaviorDocumentation.GetMeaning.Models;
using BehaviorDocumentation.GetMeaning.Services;

namespace BehaviorDocumentation.GetMeaning.Controllers;

/// <summary>
/// Decoy Get flow controller that branches on a non-IsSuccess Boolean member of the fully-shaped
/// opposite-polarity result type and reaches exact outcome helpers. The structural-result projection
/// must prove the exact IsSuccess property and compiler-proven factory returned state; this shape
/// must never project success/data or failure/status meaning. The exact ASP.NET NonController
/// attribute keeps the type available to structural-result projection tests while deliberately
/// excluding it from admitted HTTP entry points and generated presentation.
/// </summary>
[ApiController]
[NonController]
[Route("api/decoy")]
public sealed class DecoyResultController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<string> GetById(int id)
    {
        var result = OppositePolarityResult<Gadget>.Success(new Gadget());

        if (result.HasError)
        {
            return NotFound("decoy missing");
        }

        return Ok("decoy found");
    }
}
