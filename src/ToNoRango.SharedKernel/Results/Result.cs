namespace ToNoRango.SharedKernel.Results;

/// <summary>
/// Explicit success or failure. Exceptions stay reserved for genuinely exceptional paths,
/// so expected business outcomes never pay the cost of a stack unwind.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error, IReadOnlyList<Error>? errors = null)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
        Errors = errors ?? (error == Error.None ? Array.Empty<Error>() : new Error[] { error });
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public IReadOnlyList<Error> Errors { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result Failure(IReadOnlyList<Error> errors) => new(false, errors[0], errors);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error, IReadOnlyList<Error>? errors = null)
        : base(isSuccess, error, errors) => _value = value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result.");

    public static Result<TValue> Success(TValue value) => new(value, true, Error.None);

    public new static Result<TValue> Failure(Error error) => new(default, false, error);

    public new static Result<TValue> Failure(IReadOnlyList<Error> errors) => new(default, false, errors[0], errors);

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<Result<TValue>, TOut> onFailure)
        => IsSuccess ? onSuccess(Value) : onFailure(this);
}
