using Microsoft.EntityFrameworkCore;
using BehaviorDocumentation.GetMeaning.Data;
using BehaviorDocumentation.GetMeaning.Models;

namespace BehaviorDocumentation.GetMeaning.Services;

public interface IGadgetService
{
    Task<GadgetResult<Gadget>> GetByIdAsync(int id);

    Task<GadgetResult<Gadget>> FindByTokenAsync(Guid token);

    Task<GadgetResult<Gadget>> FindFirstAsync(int id);

    Task<GadgetResult<Gadget>> FindByLabelAsync();

    Task<GadgetResult<Gadget>> FindLookalikeAsync(int id);
}

/// <summary>
/// Admitted GetMeaning service. GetByIdAsync carries the exact supported EF slice (AsNoTracking,
/// ordered Include navigation anchors, SingleOrDefaultAsync with an equality predicate) and the
/// generic success/data versus failure/status result flow. The remaining methods exercise unsupported
/// terminals, non-equality predicates, and lookalike helpers that must fail closed.
/// </summary>
public sealed class GadgetService(GadgetDbContext context) : IGadgetService
{
    public async Task<GadgetResult<Gadget>> GetByIdAsync(int id)
    {
        var gadget = await context.Gadgets
            .AsNoTracking()
            .Include(item => item.Parts)
            .Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == id);

        if (gadget is null)
        {
            return GadgetResult<Gadget>.NotFound("Gadget was not found");
        }

        return GadgetResult<Gadget>.Success(gadget);
    }

    public async Task<GadgetResult<Gadget>> FindFirstAsync(int id)
    {
        var gadget = await context.Gadgets
            .Where(item => item.Id == id)
            .FirstOrDefaultAsync();

        return gadget is null
            ? GadgetResult<Gadget>.NotFound("Gadget was not found")
            : GadgetResult<Gadget>.Success(gadget);
    }

    public async Task<GadgetResult<Gadget>> FindByTokenAsync(Guid token)
    {
        // The Guid comparison has a user-defined operator, so the accepted primitive-comparison
        // vocabulary does not project a linked comparison fact; the EF query still exists.
        var gadget = await context.Gadgets
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Token == token);

        if (gadget is null)
        {
            return GadgetResult<Gadget>.NotFound("Gadget was not found");
        }

        return GadgetResult<Gadget>.Success(gadget);
    }

    public async Task<GadgetResult<Gadget>> FindByLabelAsync()
    {
        var gadget = await context.Gadgets
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Label != null);

        return gadget is null
            ? GadgetResult<Gadget>.NotFound("Gadget was not found")
            : GadgetResult<Gadget>.Success(gadget);
    }

    public async Task<GadgetResult<Gadget>> FindLookalikeAsync(int id)
    {
        var gadget = await context.Gadgets.LookalikeSingleOrDefaultAsync(item => item.Id == id);

        return gadget is null
            ? GadgetResult<Gadget>.NotFound("Gadget was not found")
            : GadgetResult<Gadget>.Success(gadget);
    }
}
