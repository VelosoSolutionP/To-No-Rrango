using ToNoRango.SharedKernel.Primitives;

namespace ToNoRango.Domain.Contracts;

/// <summary>
/// Situação contratual do cliente no Portal Veloso Solution. O To-No-Rango apenas consome; a
/// fonte oficial da verdade é o Portal, que publica essa leitura.
/// </summary>
public enum ContractStatus
{
    Vigente = 1,
    Expirado = 2
}
