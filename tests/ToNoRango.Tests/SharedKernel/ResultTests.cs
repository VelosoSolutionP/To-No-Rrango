using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Tests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccessAndHasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Failure_WithError_IsFailureAndKeepsError()
    {
        var error = Error.Validation("contract.expired", "Contrato expirado em 01/11/2026.");
        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Failure_WithErrors_KeepsFirstError()
    {
        var errors = new[]
        {
            Error.Validation("field.required", "Campo obrigatório."),
            Error.Conflict("state.invalid", "Estado inválido.")
        };

        var result = Result.Failure(errors);

        Assert.True(result.IsFailure);
        Assert.Equal(errors[0], result.Error);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void ResultOfT_Success_HoldsTheValue()
    {
        var orderId = Guid.NewGuid();
        var result = Result<Guid>.Success(orderId);

        Assert.True(result.IsSuccess);
        Assert.Equal(orderId, result.Value);
    }

    [Fact]
    public void ResultOfT_Failure_DoesNotExposeValue()
    {
        Result<Order> result = Result<Order>.Failure(Error.NotFound("order.not.found", "Pedido não encontrado."));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }
}

public sealed record Order(Guid Id);
