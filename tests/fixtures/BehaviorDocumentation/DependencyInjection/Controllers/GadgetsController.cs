using Microsoft.AspNetCore.Mvc;
using BehaviorDocumentation.DependencyInjection.Services;

namespace BehaviorDocumentation.DependencyInjection.Controllers;

// Generic framework-shape fixture used to prove the accepted ASP.NET Core controller facts compose in
// production analysis. The constructor parameters exercise exact DI binding: IGadgetStore matches two
// distinct AddScoped registrations (never one selected) and IClock matches one AddSingleton.
[ApiController]
[Route("api/[controller]")]
public sealed class GadgetsController : ControllerBase
{
    private readonly IGadgetStore _store;
    private readonly IClock _clock;

    public GadgetsController(IGadgetStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<Gadget> GetById(Guid id)
    {
        return Ok(new Gadget { Id = id, Label = "gadget" });
    }
}
