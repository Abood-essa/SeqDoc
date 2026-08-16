using Microsoft.AspNetCore.Mvc;
using AdvancedAnalysis.ConditionalDependencyInjection.Services;
using System.Threading.Tasks;

namespace AdvancedAnalysis.ConditionalDependencyInjection.Controllers;

/// <summary>
/// accepted contract guard fixture controller. The action calls the exact <see cref="IStorageService"/> interface
/// whose compiler call resolution includes BOTH implementations registered behind the
/// <c>Storage:UseMemoryStorage</c> toggle, so the Scenario Graph may suppress SC001 only when the
/// exact same-condition alternative group accounts for the complete binding set.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class StorageController : ControllerBase
{
    private readonly IStorageService _storage;

    public StorageController(IStorageService storage)
    {
        _storage = storage;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<string>> GetItem(int id)
    {
        return Ok(await _storage.GetItemAsync(id));
    }
}
