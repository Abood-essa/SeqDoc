var group = WebApplication.CreateBuilder(args).Build().MapGroup("/api");
var reassignedGroup = WebApplication.CreateBuilder(args).Build().MapGroup("/initial");
reassignedGroup = WebApplication.CreateBuilder(args).Build().MapGroup("/replacement");

group.MapGet("/items", GetItems);
group.MapPost("/items", PostItems);
group.MapPut("/items/{id}", PutItem);
group.MapDelete("/items/{id}", DeleteItem);
group.MapPost("/anonymous", () => Results.Ok());
group.MapPost("/local", LocalHandler);
group.MapPost("/telecom", async (SmsRequest request, CancellationToken cancellationToken) =>
{
    var roll = Random.Shared.Next(1, 101);
    if (roll <= 30)
    {
        return Results.Problem("service unavailable", statusCode: 500);
    }

    if (roll is > 30 and <= 50)
    {
        await Task.Delay(11000, cancellationToken);
        return Results.Ok(new { request.PhoneNumber, Delayed = true });
    }

    return Results.Ok((object)new { request.PhoneNumber, Delayed = false });
});
group.MapPost("/binding/{id}", (int id, CancellationToken cancellationToken, CustomBinder custom) => Results.Ok());

var delegateHandler = GetItems;
group.MapGet("/delegate", delegateHandler);
group.MapGet("/same", GetItems);
group.MapGet("/same", GetSame);
reassignedGroup.MapGet("/reassigned", GetItems);
group.MapGet($"/{DateTime.UtcNow.Ticks}", GetItems);

// A different extension with the same method spelling must not become an endpoint.
FakeExtensions.MapPost(group, "/lookalike", GetItems);
FakeExtensions.MapPost(group, "/lookalike-lambda", () => Results.Ok());
group.MapPost("/service-like", (ServiceLike service) => Results.Ok());
group.MapPost("/dynamic-problem", () =>
{
    var status = DateTime.UtcNow.Second;
    return Results.Problem("unstable", statusCode: status);
});
group.MapPost("/nonterminating-pattern", (int x) =>
{
    if (IsAllowed(x))
    {
        Console.WriteLine(x);
    }

    if (x is > 10 and <= 20)
    {
        return Results.Ok();
    }

    return Results.Ok();
});
group.MapPost("/unsupported-then-supported", (int x) =>
{
    if (IsAllowed(x))
    {
        Console.WriteLine(x);
    }

    if (x <= 5)
    {
        return Results.Ok();
    }

    return Results.Ok();
});

static IResult GetItems() => Results.Ok();
static IResult PostItems() => Results.Ok();
static IResult PutItem(int id) => Results.Ok(id);
static IResult DeleteItem(int id) => Results.Ok(id);
static IResult GetSame() => Results.Ok();
static IResult LocalHandler() => Results.Ok();
static bool IsAllowed(int value) => value > 0;

record SmsRequest(string PhoneNumber, string Message);

sealed class CustomBinder
{
    public static ValueTask<CustomBinder?> BindAsync(HttpContext context, System.Reflection.ParameterInfo parameter)
        => ValueTask.FromResult<CustomBinder?>(new CustomBinder());

    public static bool TryParse(string? value, out CustomBinder? result)
    {
        result = new CustomBinder();
        return true;
    }
}

sealed class ServiceLike
{
    public string Name { get; init; } = "service";
}

static class FakeExtensions
{
    public static void MapPost(object builder, string pattern, Delegate handler) { }
}
