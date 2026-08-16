using MediatR;

namespace CorpusRoadmap.MediatRDispatch;

public static class Startup
{
    public static void Map(WebApplication app, ISender sender)
        => app.MapPost("/api/orders/draft", (CreateOrderDraftCommand request) =>
            OrdersApi.CreateOrderDraftAsync(sender, request));
}
