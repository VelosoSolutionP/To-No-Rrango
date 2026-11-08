using ToNoRango.SharedKernel.Results;

namespace ToNoRango.SharedKernel.Messaging;

/// <summary>Marker for anything the dispatcher can route. TResponse is always a Result.</summary>
public interface IRequest<TResponse>;

/// <summary>Changes state. Never cached, always audited.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>Changes state and returns a value, typically the identifier of what was created.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

/// <summary>
/// A command whose failure is itself state worth keeping. A failed sign in must still record the
/// attempt and advance the lockout counter, and a replayed refresh token must still revoke the
/// family; rolling those back with the rest of a failed command would disable the very controls
/// they exist for. The transaction commits for these even when the result is a failure.
/// </summary>
public interface IPersistsFailureState;

/// <summary>Reads state. Free of side effects, therefore safe to cache and to retry.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross cutting step wrapped around a handler: validation, logging, caching, transactions.
/// Behaviours run in registration order, outermost first.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>Marks a query whose result can be served from the distributed cache.</summary>
public interface ICacheableQuery
{
    string CacheKey { get; }

    TimeSpan? Expiration { get; }

    /// <summary>Tags let a write invalidate every query that touched the same data.</summary>
    IReadOnlyCollection<string> Tags => [];
}

public interface IDispatcher
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
