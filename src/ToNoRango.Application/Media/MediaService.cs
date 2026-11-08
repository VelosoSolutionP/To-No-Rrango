using ToNoRango.Application.Abstractions;
using ToNoRango.Application.Common;
using ToNoRango.Domain.Media;
using ToNoRango.SharedKernel.Abstractions;
using ToNoRango.SharedKernel.Results;

namespace ToNoRango.Application.Media;

public sealed record MediaUploadDto(Guid Id);

public sealed class MediaService(
    IRepository<MediaFile> files,
    IEstablishmentRepository establishments,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Identifies the image from its first bytes; the name and type sent by the client are not trusted.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[..8].SequenceEqual<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    public async Task<Result<MediaUploadDto>> UploadAsync(Guid establishmentId, byte[] data, CancellationToken cancellationToken)
    {
        var establishment = await establishments.GetByIdAsync(establishmentId, cancellationToken);

        if (establishment is null)
        {
            return Result<MediaUploadDto>.Failure(CommonErrors.EstablishmentNotFound);
        }

        if (data.Length == 0 || data.Length > MaxBytes)
        {
            return Result<MediaUploadDto>.Failure(AppErrors.Validation("A foto precisa ter até 5 MB."));
        }

        var contentType = DetectContentType(data);

        if (contentType is null)
        {
            return Result<MediaUploadDto>.Failure(AppErrors.Validation("Envie uma foto em JPG, PNG ou WebP."));
        }

        var file = new MediaFile(establishment.TenantId, establishment.Id, contentType, data, clock.UtcNow);
        files.Add(file);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new MediaUploadDto(file.Id);
    }

    public Task<MediaFile?> GetAsync(Guid id, CancellationToken cancellationToken) => files.FindAsync(id, cancellationToken);
}
