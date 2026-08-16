using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreControllers;

// Analogous in framework shape only to the TicketReservation admission: attribute-routed
// [ApiController] actions for POST, GET, DELETE, and PUT with a constrained route parameter, an
// unproven body-like binding, direct outcomes, and an unsupported non-constant StatusCode call.
[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController : ControllerBase
{
    [HttpPost]
    public ActionResult<Order> Create(OrderRequest request)
    {
        var created = new Order { Id = Guid.NewGuid(), Customer = request.Customer };
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<Order> GetById(Guid id)
    {
        return Ok(new Order { Id = id });
    }

    [HttpDelete("{id:guid}/cancel")]
    public ActionResult Cancel(Guid id)
    {
        return Ok(new { id });
    }

    [HttpPut("{id:guid}")]
    public ActionResult Update(Guid id, OrderRequest request)
    {
        return Ok(new Order { Id = id, Customer = request.Customer });
    }

    [HttpDelete("unsupported")]
    public ActionResult UnsupportedStatus()
    {
        var status = DetermineStatus();
        return StatusCode(status);
    }

    private static int DetermineStatus() => 500;
}
