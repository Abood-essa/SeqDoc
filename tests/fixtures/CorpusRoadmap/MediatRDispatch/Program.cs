using MediatR;

namespace CorpusRoadmap.MediatRDispatch;

public sealed record CreateOrderDraftCommand(string CustomerId) : IRequest<CreateOrderDraftResponse>;
public sealed record WrongResponse(string Value);
public sealed record WrongRequest(string Value) : IRequest<WrongResponse>;
public sealed record NoHandlerRequest : IRequest<WrongResponse>;
public sealed record MultipleRequest : IRequest<MultipleResponse>;
public sealed record MultipleResponse(string Value);

public sealed class CreateOrderDraftCommandHandler
    : IRequestHandler<CreateOrderDraftCommand, CreateOrderDraftResponse>
{
    public Task<CreateOrderDraftResponse> Handle(CreateOrderDraftCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new CreateOrderDraftResponse(request.CustomerId));
}

public sealed record CreateOrderDraftResponse(string CustomerId);

// These types deliberately resemble the admitted shape but must not be selected by name.
public interface ILookalikeSender
{
    Task<TResponse> Send<TResponse>(object request, CancellationToken cancellationToken = default);
}

public sealed class LookalikeSender : ILookalikeSender
{
    public Task<TResponse> Send<TResponse>(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

public sealed class WrongResponseHandler : IRequestHandler<WrongRequest, WrongResponse>
{
    public Task<WrongResponse> Handle(WrongRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new WrongResponse(request.Value));
}

public sealed class MultipleRequestHandlerA : IRequestHandler<MultipleRequest, MultipleResponse>
{
    public Task<MultipleResponse> Handle(MultipleRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new MultipleResponse("a"));
}

public sealed class MultipleRequestHandlerB : IRequestHandler<MultipleRequest, MultipleResponse>
{
    public Task<MultipleResponse> Handle(MultipleRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new MultipleResponse("b"));
}

public static class OrdersApi
{
    public static Task<CreateOrderDraftResponse> CreateOrderDraftAsync(
        ISender sender, CreateOrderDraftCommand request)
    {
        return sender.Send(request);
    }

    public static Task<TResponse> WrongGeneric<TResponse>(ISender sender, IRequest<TResponse> request)
        => sender.Send<TResponse>(request);

    public static Task<TResponse> Lookalike<TResponse>(ILookalikeSender sender, object request)
        => sender.Send<TResponse>(request);

    public static Task<WrongResponse> NoHandler(ISender sender, NoHandlerRequest request)
        => sender.Send(request);

    public static Task<MultipleResponse> Multiple(ISender sender, MultipleRequest request)
        => sender.Send(request);
}

public static class FixtureEntryPoint
{
    public static void Main() { }
}
