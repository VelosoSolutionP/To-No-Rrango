using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ToNoRango.Domain.Delivery;
using ToNoRango.Domain.Finance;
using ToNoRango.Domain.Identity;
using ToNoRango.Domain.Inventory;
using ToNoRango.Domain.Media;
using ToNoRango.Domain.Messaging;
using ToNoRango.Domain.Parties;
using ToNoRango.Domain.Payments;
using ToNoRango.Domain.Zest;

namespace ToNoRango.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "identity");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.Name).HasMaxLength(120).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(200).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(300).IsRequired();
        builder.Property(user => user.Role).HasConversion<int>();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.TenantId);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", "payments");
        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.Id).ValueGeneratedNever();
        builder.Property(payment => payment.Provider).HasMaxLength(40).IsRequired();
        builder.Property(payment => payment.ProviderPaymentId).HasMaxLength(60).IsRequired();
        builder.Property(payment => payment.Channel).HasConversion<int>();
        builder.Property(payment => payment.Status).HasConversion<int>();
        builder.Property(payment => payment.StatusDetail).HasMaxLength(120);
        builder.Property(payment => payment.PixQrCode).HasColumnType("text");
        builder.Property(payment => payment.PixQrCodeBase64).HasColumnType("text");
        builder.HasIndex(payment => new { payment.EstablishmentId, payment.ProviderPaymentId }).IsUnique();
        builder.HasIndex(payment => payment.OrderId);
        builder.HasIndex(payment => payment.TenantId);
    }
}

public sealed class PaymentSettingsConfiguration : IEntityTypeConfiguration<PaymentSettings>
{
    public void Configure(EntityTypeBuilder<PaymentSettings> builder)
    {
        builder.ToTable("payment_settings", "payments");
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.EncryptedAccessToken).HasMaxLength(1000);
        builder.Property(settings => settings.EncryptedWebhookSecret).HasMaxLength(1000);
        builder.Property(settings => settings.PublicKey).HasMaxLength(200);
        builder.HasIndex(settings => settings.EstablishmentId).IsUnique();
        builder.HasIndex(settings => settings.TenantId);
    }
}

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers", "parties");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();
        builder.Property(customer => customer.Name).HasMaxLength(120).IsRequired();
        builder.Property(customer => customer.Phone).HasMaxLength(30).IsRequired();
        builder.Property(customer => customer.PhoneKey).HasMaxLength(20).IsRequired();
        builder.Property(customer => customer.Document).HasMaxLength(14);
        builder.Property(customer => customer.Email).HasMaxLength(200);
        builder.Property(customer => customer.Notes).HasMaxLength(500);
        builder.HasIndex(customer => new { customer.EstablishmentId, customer.PhoneKey }).IsUnique();
        builder.HasIndex(customer => customer.TenantId);
    }
}

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers", "parties");
        builder.HasKey(supplier => supplier.Id);
        builder.Property(supplier => supplier.Id).ValueGeneratedNever();
        builder.Property(supplier => supplier.Name).HasMaxLength(160).IsRequired();
        builder.Property(supplier => supplier.TradeName).HasMaxLength(160);
        builder.Property(supplier => supplier.Document).HasMaxLength(14);
        builder.Property(supplier => supplier.Phone).HasMaxLength(30);
        builder.Property(supplier => supplier.Email).HasMaxLength(200);
        builder.Property(supplier => supplier.Address).HasMaxLength(300);
        builder.HasIndex(supplier => new { supplier.EstablishmentId, supplier.Document });
        builder.HasIndex(supplier => supplier.TenantId);
    }
}

public sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items", "inventory");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Name).HasMaxLength(120).IsRequired();
        builder.Property(item => item.Unit).HasMaxLength(10).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Ignore(item => item.IsLow);
        builder.HasIndex(item => item.EstablishmentId);
        builder.HasIndex(item => item.TenantId);
    }
}

public sealed class RecipeLineConfiguration : IEntityTypeConfiguration<RecipeLine>
{
    public void Configure(EntityTypeBuilder<RecipeLine> builder)
    {
        builder.ToTable("recipe_lines", "inventory");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.HasIndex(line => new { line.MenuItemId, line.StockItemId }).IsUnique();
        builder.HasIndex(line => line.TenantId);
    }
}

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", "inventory");
        builder.HasKey(movement => movement.Id);
        builder.Property(movement => movement.Id).ValueGeneratedNever();
        builder.Property(movement => movement.Type).HasConversion<int>();
        builder.Property(movement => movement.Notes).HasMaxLength(300);
        builder.Property(movement => movement.CreatedBy).HasMaxLength(200);
        builder.HasIndex(movement => new { movement.EstablishmentId, movement.CreatedAt });
        builder.HasIndex(movement => movement.OrderId);
        builder.HasIndex(movement => movement.TenantId);
    }
}

public sealed class PurchaseEntryConfiguration : IEntityTypeConfiguration<PurchaseEntry>
{
    public void Configure(EntityTypeBuilder<PurchaseEntry> builder)
    {
        builder.ToTable("purchase_entries", "inventory");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.InvoiceNumber).HasMaxLength(30);
        builder.Property(entry => entry.AccessKey).HasMaxLength(44);
        builder.Property(entry => entry.Notes).HasMaxLength(500);
        builder.Property(entry => entry.CreatedBy).HasMaxLength(200);
        builder.HasIndex(entry => new { entry.EstablishmentId, entry.AccessKey }).IsUnique().HasFilter("\"AccessKey\" IS NOT NULL");
        builder.HasIndex(entry => entry.TenantId);
        builder.HasMany(entry => entry.Lines).WithOne().HasForeignKey(line => line.PurchaseEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(entry => entry.Lines).AutoInclude().UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PurchaseEntryLineConfiguration : IEntityTypeConfiguration<PurchaseEntryLine>
{
    public void Configure(EntityTypeBuilder<PurchaseEntryLine> builder)
    {
        builder.ToTable("purchase_entry_lines", "inventory");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.Description).HasMaxLength(200).IsRequired();
        builder.HasIndex(line => line.TenantId);
    }
}

public sealed class FinancialEntryConfiguration : IEntityTypeConfiguration<FinancialEntry>
{
    public void Configure(EntityTypeBuilder<FinancialEntry> builder)
    {
        builder.ToTable("financial_entries", "finance");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Type).HasConversion<int>();
        builder.Property(entry => entry.Status).HasConversion<int>();
        builder.Property(entry => entry.Source).HasConversion<int>();
        builder.Property(entry => entry.Category).HasMaxLength(60).IsRequired();
        builder.Property(entry => entry.Description).HasMaxLength(200).IsRequired();
        builder.HasIndex(entry => new { entry.Source, entry.SourceId }).IsUnique().HasFilter("\"SourceId\" IS NOT NULL");
        builder.HasIndex(entry => new { entry.EstablishmentId, entry.DueOn });
        builder.HasIndex(entry => entry.TenantId);
    }
}

public sealed class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("delivery_zones", "delivery");
        builder.HasKey(zone => zone.Id);
        builder.Property(zone => zone.Id).ValueGeneratedNever();
        builder.Property(zone => zone.District).HasMaxLength(80).IsRequired();
        builder.Property(zone => zone.DistrictKey).HasMaxLength(80).IsRequired();
        builder.HasIndex(zone => new { zone.EstablishmentId, zone.DistrictKey }).IsUnique();
        builder.HasIndex(zone => zone.TenantId);
    }
}

public sealed class WhatsAppSettingsConfiguration : IEntityTypeConfiguration<WhatsAppSettings>
{
    public void Configure(EntityTypeBuilder<WhatsAppSettings> builder)
    {
        builder.ToTable("whatsapp_settings", "messaging");
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.InstanceName).HasMaxLength(80);
        builder.Property(settings => settings.EncryptedApiKey).HasMaxLength(1000);
        builder.Property(settings => settings.EncryptedWebhookToken).HasMaxLength(1000).IsRequired();
        builder.Property(settings => settings.HotelBotId).HasMaxLength(120);
        builder.Property(settings => settings.HotelBotName).HasMaxLength(120);
        builder.Property(settings => settings.HotelBotPrompt).HasColumnType("text");
        builder.Ignore(settings => settings.IsReady);
        builder.HasIndex(settings => settings.EstablishmentId).IsUnique();
        builder.HasIndex(settings => settings.TenantId);
    }
}

public sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> builder)
    {
        builder.ToTable("outbound_messages", "messaging");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Phone).HasMaxLength(30).IsRequired();
        builder.Property(message => message.Text).HasMaxLength(4000).IsRequired();
        builder.Property(message => message.ImageBase64).HasColumnType("text");
        builder.Property(message => message.Status).HasConversion<int>();
        builder.Property(message => message.LastError).HasMaxLength(300);
        builder.HasIndex(message => new { message.Status, message.NextAttemptAt });
        builder.HasIndex(message => message.TenantId);
    }
}

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", "messaging");
        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Id).ValueGeneratedNever();
        builder.Property(conversation => conversation.Phone).HasMaxLength(30).IsRequired();
        builder.Property(conversation => conversation.PhoneKey).HasMaxLength(20).IsRequired();
        builder.Property(conversation => conversation.CustomerName).HasMaxLength(120);
        builder.Property(conversation => conversation.LastMessage).HasMaxLength(200);
        builder.Property(conversation => conversation.SalesState).HasColumnType("text");
        builder.HasIndex(conversation => new { conversation.EstablishmentId, conversation.PhoneKey }).IsUnique();
        builder.HasIndex(conversation => conversation.TenantId);
    }
}

public sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("conversation_messages", "messaging");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Direction).HasConversion<int>();
        builder.Property(message => message.Author).HasConversion<int>();
        builder.Property(message => message.Text).HasMaxLength(4000).IsRequired();
        builder.Property(message => message.ExternalId).HasMaxLength(120);
        builder.HasIndex(message => new { message.ConversationId, message.CreatedAt });
        builder.HasIndex(message => new { message.EstablishmentId, message.ExternalId }).IsUnique().HasFilter("\"ExternalId\" IS NOT NULL");
        builder.HasIndex(message => message.TenantId);
    }
}

public sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("media_files", "media");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).ValueGeneratedNever();
        builder.Property(file => file.ContentType).HasMaxLength(40).IsRequired();
        builder.HasIndex(file => file.TenantId);
    }
}

public sealed class KnowledgeEntryConfiguration : IEntityTypeConfiguration<KnowledgeEntry>
{
    public void Configure(EntityTypeBuilder<KnowledgeEntry> builder)
    {
        builder.ToTable("knowledge_entries", "zest");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Topic).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.Answer).HasMaxLength(1500).IsRequired();
        builder.Property(entry => entry.Keywords).HasMaxLength(300).IsRequired();
        builder.HasIndex(entry => entry.EstablishmentId);
        builder.HasIndex(entry => entry.TenantId);
    }
}
