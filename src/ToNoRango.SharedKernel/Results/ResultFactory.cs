using System.Collections.Concurrent;
using System.Reflection;

namespace ToNoRango.SharedKernel.Results;

/// <summary>
/// Builds a failed Result or Result&lt;T&gt; when only the closed response type is known, which is
/// the situation every pipeline behaviour is in: it has to short circuit a handler it never saw.
/// The per-type reflection is resolved once and cached, so the hot path is a dictionary lookup.
/// </summary>
public static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, Func<IReadOnlyList<Error>, object>> Builders = new();

    public static TResponse Failure<TResponse>(IReadOnlyList<Error> errors)
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(errors);
        }

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = typeof(TResponse).GetGenericArguments()[0];

            var method = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(Result.Failure)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters() is [{ ParameterType: var parameter }]
                    && parameter == typeof(IReadOnlyList<Error>))
                .MakeGenericMethod(valueType);

            var value = Activator.CreateInstance(valueType);
            return (TResponse)method.Invoke(null, [value, false, errors[0], errors])!;
        }

        throw new InvalidOperationException(
            $"{typeof(TResponse).Name} is not Result nor Result<T>. Every handler must return a Result.");
    }

    public static TResponse Failure<TResponse>(Error error) => Failure<TResponse>([error]);

    /// <summary>
    /// The T of a <c>Result&lt;T&gt;</c>, or null when the response is a plain <c>Result</c>.
    /// Used by the caching behaviour, which must store the value rather than the wrapper: a
    /// Result has no parameterless constructor and cannot be deserialised back from JSON.
    /// </summary>
    public static Type? ValueTypeOf(Type responseType)
        => responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>)
            ? responseType.GetGenericArguments()[0]
            : null;
}
