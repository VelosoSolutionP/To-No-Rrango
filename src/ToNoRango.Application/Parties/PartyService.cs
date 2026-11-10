using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Common;
using ToNoRango.Domain.Parties;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Parties;

public sealed record CustomerRequest(string Name, string Phone, string? Document, string? Email, string? Notes);

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string Phone,
    string? Document,
    string? Email,
    string? Notes,
    int OrdersCount,
    long TotalOrderedInCents,
    DateTimeOffset? LastOrderAt,
    bool IsActive = true,
    DateTimeOffset? CreatedAt = null)
{
    public static CustomerDto From(Customer customer) => new(
        customer.Id,
        customer.Name,
        customer.Phone,
        customer.Document,
        customer.Email,
        customer.Notes,
        customer.OrdersCount,
        customer.TotalOrderedInCents,
        customer.LastOrderAt,
        customer.IsActive,
        customer.CreatedAt);
}

public sealed record SupplierRequest(
    string Name,
    string? TradeName,
    string? Document,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive = true);

public sealed record SupplierDto(
    Guid Id,
    string Name,
    string? TradeName,
    string? Document,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive)
{
    public static SupplierDto From(Supplier supplier) => new(
        supplier.Id,
        supplier.Name,
        supplier.TradeName,
        supplier.Document,
        supplier.Phone,
        supplier.Email,
        supplier.Address,
        supplier.IsActive);
}

public sealed class PartyService(
    IRepository<Customer> customers,
    IRepository<Supplier> suppliers,
    IEstablishmentRepository establishments,
    ILookupClient lookups,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly Error LookupUnavailable =
        Error.External("lookup.unavailable", "O serviço de consulta está indisponível. Preencha manualmente.");

    public async Task<Result<IReadOnlyList<CustomerDto>>> ListCustomersAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await customers.ListAsync(
            customer => customer.EstablishmentId == establishmentId,
            customer => customer.Name,
            cancellationToken);

        return list.Select(CustomerDto.From).ToList();
    }

    public Task<Result<CustomerDto>> CreateCustomerAsync(Guid establishmentId, CustomerRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CustomerDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<CustomerDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var customer = new Customer(establishment.TenantId, establishment.Id, request.Name, request.Phone, clock.UtcNow);
            customer.Update(request.Name, request.Document, request.Email, request.Notes, clock.UtcNow);

            if (await customers.AnyAsync(item => item.EstablishmentId == establishment.Id && item.PhoneKey == customer.PhoneKey, cancellationToken))
            {
                return Result<CustomerDto>.Failure(AppErrors.Conflict("customer.duplicate", "Já existe um cliente com este telefone."));
            }

            customers.Add(customer);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CustomerDto.From(customer);
        });

    public Task<Result<CustomerDto>> UpdateCustomerAsync(Guid customerId, CustomerRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CustomerDto>(async () =>
        {
            var customer = await customers.FindAsync(customerId, cancellationToken);

            if (customer is null)
            {
                return Result<CustomerDto>.Failure(AppErrors.NotFound("customer.not_found", "Cliente não encontrado."));
            }

            customer.Update(request.Name, request.Document, request.Email, request.Notes, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CustomerDto.From(customer);
        });

    /// <summary>A customer without orders is removed; one with history is only deactivated, so reports keep their names.</summary>
    public Task<Result<bool>> DeleteCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<bool>(async () =>
        {
            var customer = await customers.FindAsync(customerId, cancellationToken);

            if (customer is null)
            {
                return Result<bool>.Failure(AppErrors.NotFound("customer.not_found", "Cliente não encontrado."));
            }

            if (customer.OrdersCount == 0)
            {
                customers.Remove(customer);
            }
            else
            {
                customer.SetActive(false, clock.UtcNow);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return customer.OrdersCount == 0;
        });

    public Task<Result<CustomerDto>> SetCustomerActiveAsync(Guid customerId, bool isActive, CancellationToken cancellationToken) =>
        Guarded.RunAsync<CustomerDto>(async () =>
        {
            var customer = await customers.FindAsync(customerId, cancellationToken);

            if (customer is null)
            {
                return Result<CustomerDto>.Failure(AppErrors.NotFound("customer.not_found", "Cliente não encontrado."));
            }

            customer.SetActive(isActive, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return CustomerDto.From(customer);
        });

    /// <summary>Suppliers stay in purchase history: deleting deactivates.</summary>
    public Task<Result<SupplierDto>> DeleteSupplierAsync(Guid supplierId, CancellationToken cancellationToken) =>
        Guarded.RunAsync<SupplierDto>(async () =>
        {
            var supplier = await suppliers.FindAsync(supplierId, cancellationToken);

            if (supplier is null)
            {
                return Result<SupplierDto>.Failure(AppErrors.NotFound("supplier.not_found", "Fornecedor não encontrado."));
            }

            supplier.Update(supplier.Name, supplier.TradeName, supplier.Document, supplier.Phone, supplier.Email, supplier.Address, false, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return SupplierDto.From(supplier);
        });

    public async Task<Result<IReadOnlyList<SupplierDto>>> ListSuppliersAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var list = await suppliers.ListAsync(
            supplier => supplier.EstablishmentId == establishmentId,
            supplier => supplier.Name,
            cancellationToken);

        return list.Select(SupplierDto.From).ToList();
    }

    public Task<Result<SupplierDto>> CreateSupplierAsync(Guid establishmentId, SupplierRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<SupplierDto>(async () =>
        {
            var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

            if (establishment is null)
            {
                return Result<SupplierDto>.Failure(CommonErrors.EstablishmentNotFound);
            }

            var supplier = new Supplier(
                establishment.TenantId,
                establishment.Id,
                request.Name,
                request.TradeName,
                request.Document,
                request.Phone,
                request.Email,
                request.Address,
                clock.UtcNow);

            suppliers.Add(supplier);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return SupplierDto.From(supplier);
        });

    public Task<Result<SupplierDto>> UpdateSupplierAsync(Guid supplierId, SupplierRequest request, CancellationToken cancellationToken) =>
        Guarded.RunAsync<SupplierDto>(async () =>
        {
            var supplier = await suppliers.FindAsync(supplierId, cancellationToken);

            if (supplier is null)
            {
                return Result<SupplierDto>.Failure(AppErrors.NotFound("supplier.not_found", "Fornecedor não encontrado."));
            }

            supplier.Update(request.Name, request.TradeName, request.Document, request.Phone, request.Email, request.Address, request.IsActive, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return SupplierDto.From(supplier);
        });

    public async Task<Result<CnpjData>> LookupCnpjAsync(string cnpj, CancellationToken cancellationToken)
    {
        if (!DocumentValidator.IsValidCnpj(cnpj))
        {
            return Result<CnpjData>.Failure(AppErrors.Validation("CNPJ inválido."));
        }

        try
        {
            var data = await lookups.FindCnpjAsync(PhoneNumber.Digits(cnpj), cancellationToken);

            return data is null
                ? Result<CnpjData>.Failure(AppErrors.NotFound("lookup.not_found", "CNPJ não encontrado."))
                : data;
        }
        catch (HttpRequestException)
        {
            return Result<CnpjData>.Failure(LookupUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<CnpjData>.Failure(LookupUnavailable);
        }
    }

    public async Task<Result<CepData>> LookupCepAsync(string cep, CancellationToken cancellationToken)
    {
        var digits = PhoneNumber.Digits(cep);

        if (digits.Length != 8)
        {
            return Result<CepData>.Failure(AppErrors.Validation("CEP inválido."));
        }

        try
        {
            var data = await lookups.FindCepAsync(digits, cancellationToken);

            return data is null
                ? Result<CepData>.Failure(AppErrors.NotFound("lookup.not_found", "CEP não encontrado."))
                : data;
        }
        catch (HttpRequestException)
        {
            return Result<CepData>.Failure(LookupUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<CepData>.Failure(LookupUnavailable);
        }
    }
}
