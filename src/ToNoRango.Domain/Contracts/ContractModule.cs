namespace ToNoRango.Domain.Contracts;

/// <summary>
/// Module released by a contract. Codes are the official identifiers used by the Portal when it
/// publishes the contracted modules list (cardápio, pedidos, pagamentos, whatsapp, estoque, fiscal,
/// relatórios, ia, ...). A module may be contracted but not yet available (paused by commercial
/// agreement), which keeps the product usable without silently exposing half-configured modules.
/// </summary>
public sealed record ContractModule(string Code, bool IsAvailable = true)
{
    public bool IsAvailable { get; internal set; } = IsAvailable;
}
