namespace ToNoRango.SharedKernel.Abstractions;

/// <summary>A concurrent write or a unique constraint rejected the change; the caller may retry.</summary>
public sealed class PersistenceConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
