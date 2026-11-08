using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ToNoRango.SharedKernel.Messaging;

/// <summary>
/// Resolves the handler for a request and wraps it in the registered pipeline behaviours.
/// Written in house rather than taken from a library because the whole contract is the four
/// interfaces above, and MediatR moved to a paid licence from v13 onwards.
/// </summary>
public sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, InvocationPlan> Plans = new();

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = Plans.GetOrAdd(request.GetType(), static requestType => InvocationPlan.Build(requestType, typeof(TResponse)));

        return plan.Invoke<TResponse>(provider, request, cancellationToken);
    }

    private sealed record InvocationPlan(Type HandlerType, Type BehaviourType, MethodInfo HandleMethod, MethodInfo BehaviourMethod)
    {
        public static InvocationPlan Build(Type requestType, Type responseType)
        {
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
            var behaviourType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, responseType);

            var handleMethod = handlerType.GetMethod(nameof(IRequestHandler<IRequest<object>, object>.HandleAsync))
                ?? throw new InvalidOperationException($"HandleAsync not found on {handlerType}.");

            var behaviourMethod = behaviourType.GetMethod(nameof(IPipelineBehavior<IRequest<object>, object>.HandleAsync))
                ?? throw new InvalidOperationException($"HandleAsync not found on {behaviourType}.");

            return new InvocationPlan(handlerType, behaviourType, handleMethod, behaviourMethod);
        }

        public Task<TResponse> Invoke<TResponse>(IServiceProvider provider, object request, CancellationToken cancellationToken)
        {
            var handler = provider.GetService(HandlerType)
                ?? throw new InvalidOperationException(
                    $"No handler registered for {request.GetType().Name}. Expected an implementation of {HandlerType.Name}.");

            RequestHandlerDelegate<TResponse> pipeline = () =>
                (Task<TResponse>)HandleMethod.Invoke(handler, [request, cancellationToken])!;

            var behaviours = ((IEnumerable<object>)provider.GetServices(BehaviourType)).Reverse();

            foreach (var behaviour in behaviours)
            {
                var next = pipeline;
                pipeline = () => (Task<TResponse>)BehaviourMethod.Invoke(behaviour, [request, next, cancellationToken])!;
            }

            return pipeline();
        }
    }
}
