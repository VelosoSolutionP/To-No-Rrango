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

public sealed class ZestConnectionConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Zest.ZestConnection>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Zest.ZestConnection> builder)
    {
        builder.ToTable("connections", "zest");
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.Property(connection => connection.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(connection => connection.KeyHint).HasMaxLength(12).IsRequired();
        builder.HasIndex(connection => connection.EstablishmentId).IsUnique();
        builder.HasIndex(connection => connection.TenantId);
    }
}

public sealed class ReceivedNoteConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.ReceivedNote>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.ReceivedNote> builder)
    {
        builder.ToTable("received_notes", "fiscal");
        builder.HasKey(note => note.Id);
        builder.Property(note => note.Id).ValueGeneratedNever();
        builder.Property(note => note.AccessKey).HasMaxLength(44).IsRequired();
        builder.Property(note => note.IssuerDocument).HasMaxLength(14);
        builder.Property(note => note.IssuerName).HasMaxLength(120);
        builder.Property(note => note.Status).HasConversion<int>();
        builder.Property(note => note.Xml).HasColumnType("text");
        builder.Property(note => note.LastEvent).HasMaxLength(120);
        builder.Property<uint>("xmin").IsRowVersion();
        builder.HasIndex(note => new { note.EstablishmentId, note.AccessKey }).IsUnique();
        builder.HasIndex(note => note.TenantId);
    }
}

public sealed class FiscalAuditEntryConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.FiscalAuditEntry>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.FiscalAuditEntry> builder)
    {
        builder.ToTable("audit_entries", "fiscal");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Actor).HasMaxLength(160).IsRequired();
        builder.Property(entry => entry.Category).HasMaxLength(40).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.Target).HasMaxLength(120);
        builder.Property(entry => entry.Period).HasMaxLength(7);
        builder.Property(entry => entry.Details).HasMaxLength(1000);
        builder.HasIndex(entry => new { entry.EstablishmentId, entry.At });
        builder.HasIndex(entry => new { entry.EstablishmentId, entry.Category, entry.Period });
        builder.HasIndex(entry => entry.TenantId);
    }
}

public sealed class FiscalApiKeyConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.FiscalApiKey>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.FiscalApiKey> builder)
    {
        builder.ToTable("api_keys", "fiscal");
        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();
        builder.Property(key => key.Name).HasMaxLength(60).IsRequired();
        builder.Property(key => key.Prefix).HasMaxLength(16).IsRequired();
        builder.Property(key => key.Hash).HasMaxLength(64).IsRequired();
        builder.Property(key => key.CreatedBy).HasMaxLength(160).IsRequired();
        builder.Ignore(key => key.IsActive);
        builder.HasIndex(key => key.Hash).IsUnique();
        builder.HasIndex(key => key.EstablishmentId);
        builder.HasIndex(key => key.TenantId);
    }
}

public sealed class FiscalWebhookConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.FiscalWebhook>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.FiscalWebhook> builder)
    {
        builder.ToTable("webhooks", "fiscal");
        builder.HasKey(webhook => webhook.Id);
        builder.Property(webhook => webhook.Id).ValueGeneratedNever();
        builder.Property(webhook => webhook.Url).HasMaxLength(500).IsRequired();
        builder.Property(webhook => webhook.EncryptedSecret).HasMaxLength(500).IsRequired();
        builder.Property(webhook => webhook.Subscribed).HasMaxLength(300).IsRequired();
        builder.Ignore(webhook => webhook.SubscribedEvents);
        builder.HasIndex(webhook => new { webhook.EstablishmentId, webhook.IsActive });
        builder.HasIndex(webhook => webhook.TenantId);
    }
}

public sealed class FiscalWebhookDeliveryConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.FiscalWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.FiscalWebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries", "fiscal");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();
        builder.Property(delivery => delivery.EventKey).HasMaxLength(120).IsRequired();
        builder.Property(delivery => delivery.Event).HasMaxLength(40).IsRequired();
        builder.Property(delivery => delivery.Payload).HasColumnType("text").IsRequired();
        builder.Property(delivery => delivery.LastError).HasMaxLength(500);
        // The same event never goes twice to the same webhook, even if two scans overlap.
        builder.HasIndex(delivery => new { delivery.WebhookId, delivery.EventKey }).IsUnique();
        builder.HasIndex(delivery => new { delivery.Status, delivery.NextAttemptAt });
        builder.HasIndex(delivery => new { delivery.EstablishmentId, delivery.CreatedAt });
        builder.HasIndex(delivery => delivery.TenantId);
    }
}

public sealed class AccountantFirmConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.AccountantFirm>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.AccountantFirm> builder)
    {
        builder.ToTable("accountant_firms", "fiscal");
        builder.HasKey(firm => firm.Id);
        builder.Property(firm => firm.Id).ValueGeneratedNever();
        builder.Property(firm => firm.Name).HasMaxLength(120).IsRequired();
        builder.Property(firm => firm.Document).HasMaxLength(14).IsRequired();
        builder.Property(firm => firm.Crc).HasMaxLength(20).IsRequired();
        builder.Property(firm => firm.Email).HasMaxLength(200).IsRequired();
        builder.Property(firm => firm.Phone).HasMaxLength(20);
        builder.HasIndex(firm => firm.Document);
    }
}

public sealed class AccountantUserConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.AccountantUser>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.AccountantUser> builder)
    {
        builder.ToTable("accountant_users", "fiscal");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.Name).HasMaxLength(120).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(200).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(500).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.FirmId);
    }
}

public sealed class AccountantLinkConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.AccountantLink>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.AccountantLink> builder)
    {
        builder.ToTable("accountant_links", "fiscal");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.Property(link => link.EstablishmentName).HasMaxLength(120).IsRequired();
        builder.Property(link => link.InvitedEmail).HasMaxLength(200).IsRequired();
        builder.Property(link => link.CodeHash).HasMaxLength(64);
        builder.Property(link => link.InvitedBy).HasMaxLength(160).IsRequired();
        builder.HasIndex(link => link.CodeHash).IsUnique().HasFilter("\"CodeHash\" IS NOT NULL");
        builder.HasIndex(link => new { link.FirmId, link.Status });
        builder.HasIndex(link => new { link.EstablishmentId, link.Status });
        builder.HasIndex(link => link.TenantId);
    }
}

public sealed class AccountantMessageConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.AccountantMessage>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.AccountantMessage> builder)
    {
        builder.ToTable("accountant_messages", "fiscal");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Author).HasMaxLength(160).IsRequired();
        builder.Property(message => message.Text).HasMaxLength(2000).IsRequired();
        builder.Property(message => message.ResolvedBy).HasMaxLength(160);
        builder.HasIndex(message => new { message.EstablishmentId, message.CreatedAt });
        builder.HasIndex(message => message.LinkId);
        builder.HasIndex(message => message.TenantId);
    }
}

public sealed class ServiceInvoiceConfiguration : IEntityTypeConfiguration<ToNoRango.Domain.Fiscal.ServiceInvoice>
{
    public void Configure(EntityTypeBuilder<ToNoRango.Domain.Fiscal.ServiceInvoice> builder)
    {
        builder.ToTable("service_invoices", "fiscal");
        builder.HasKey(note => note.Id);
        builder.Property(note => note.Id).ValueGeneratedNever();
        builder.Property(note => note.ExternalId).HasMaxLength(40).IsRequired();
        builder.Property(note => note.DpsId).HasMaxLength(45).IsRequired();
        builder.Property(note => note.AccessKey).HasMaxLength(50);
        builder.Property(note => note.Xml).HasColumnType("text");
        builder.Property(note => note.Message).HasMaxLength(2000);
        builder.Property(note => note.TakerDocument).HasMaxLength(14).IsRequired();
        builder.Property(note => note.TakerName).HasMaxLength(150).IsRequired();
        builder.Property(note => note.ServiceCode).HasMaxLength(6).IsRequired();
        builder.Property(note => note.Description).HasMaxLength(2000).IsRequired();
        builder.Property(note => note.IssRate).HasPrecision(5, 2);
        builder.Property(note => note.CancelReason).HasMaxLength(255);
        builder.Property<uint>("xmin").IsRowVersion();
        // Same external id never makes a second note; same DPS number never goes twice.
        builder.HasIndex(note => new { note.EstablishmentId, note.IsSimulation, note.ExternalId }).IsUnique();
        builder.HasIndex(note => new { note.EstablishmentId, note.IsSimulation, note.Environment, note.Series, note.Number }).IsUnique();
        builder.HasIndex(note => new { note.EstablishmentId, note.CreatedAt });
        builder.HasIndex(note => note.TenantId);
    }
}
