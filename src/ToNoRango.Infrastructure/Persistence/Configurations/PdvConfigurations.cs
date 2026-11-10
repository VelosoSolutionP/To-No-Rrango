using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Feedback;
using ToNoRango.Domain.Pdv;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.ToTable("cash_sessions", "pdv");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.Status).HasConversion<int>();
        builder.Property(session => session.OpenedBy).HasMaxLength(120).IsRequired();
        builder.Property(session => session.ClosedBy).HasMaxLength(120);
        builder.Property(session => session.ClosingNotes).HasMaxLength(300);
        builder.Ignore(session => session.ExpectedCash);
        builder.Ignore(session => session.DifferenceInCents);
        builder.HasIndex(session => new { session.EstablishmentId, session.OpenedAt });
        builder.HasIndex(session => session.EstablishmentId).IsUnique().HasFilter("\"Status\" = 1");
        builder.HasIndex(session => session.TenantId);
        builder.HasMany(session => session.Movements).WithOne().HasForeignKey(movement => movement.CashSessionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(session => session.Movements).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("cash_movements", "pdv");
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Id).ValueGeneratedNever();
        builder.Property(movement => movement.Type).HasConversion<int>();
        builder.Property(movement => movement.Method).HasConversion<int>();
        builder.Property(movement => movement.Reason).HasMaxLength(120).IsRequired();
        builder.Property(movement => movement.OperatorName).HasMaxLength(120).IsRequired();
        builder.Ignore(movement => movement.CashEffect);
        builder.HasIndex(movement => movement.OrderId);
        builder.HasIndex(movement => movement.TenantId);
    }
}

public sealed class OrderRatingConfiguration : IEntityTypeConfiguration<OrderRating>
{
    public void Configure(EntityTypeBuilder<OrderRating> builder)
    {
        builder.ToTable("order_ratings", "feedback");
        builder.HasKey(rating => rating.Id);
        builder.Property(rating => rating.Id).ValueGeneratedNever();
        builder.Property(rating => rating.OrderCode).HasMaxLength(20).IsRequired();
        builder.Property(rating => rating.StoreComment).HasMaxLength(500);
        builder.Property(rating => rating.CourierName).HasMaxLength(120);
        builder.Property(rating => rating.CourierComment).HasMaxLength(500);
        builder.Ignore(rating => rating.StoreAverage);
        builder.Ignore(rating => rating.CourierAverage);
        builder.HasIndex(rating => rating.OrderId).IsUnique();
        builder.HasIndex(rating => new { rating.EstablishmentId, rating.CreatedAt });
        builder.HasIndex(rating => rating.CourierId);
        builder.HasIndex(rating => rating.TenantId);
    }
}

public sealed class KnowledgeGapConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Zest.KnowledgeGap>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Zest.KnowledgeGap> builder)
    {
        builder.ToTable("knowledge_gaps", "zest");
        builder.HasKey(gap => gap.Id);
        builder.Property(gap => gap.Id).ValueGeneratedNever();
        builder.Property(gap => gap.Question).HasMaxLength(500).IsRequired();
        builder.Property(gap => gap.QuestionKey).HasMaxLength(200).IsRequired();
        builder.Property(gap => gap.Source).HasMaxLength(30).IsRequired();
        builder.HasIndex(gap => new { gap.EstablishmentId, gap.QuestionKey }).IsUnique();
        builder.HasIndex(gap => gap.TenantId);
    }
}

public sealed class FiscalNumberGapConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.FiscalNumberGap>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.FiscalNumberGap> builder)
    {
        builder.ToTable("number_gaps", "fiscal");
        builder.HasKey(gap => gap.Id);
        builder.Property(gap => gap.Id).ValueGeneratedNever();
        builder.Property(gap => gap.Status).HasConversion<int>();
        builder.Property(gap => gap.Reason).HasMaxLength(255).IsRequired();
        builder.Property(gap => gap.Protocol).HasMaxLength(20);
        builder.Property(gap => gap.Message).HasMaxLength(500);
        builder.Property(gap => gap.Xml).HasColumnType("text");
        builder.HasIndex(gap => new { gap.EstablishmentId, gap.Model, gap.Series });
        builder.HasIndex(gap => gap.TenantId);
    }
}
