namespace ToNoRango.Domain.Orders;

public enum FulfillmentType
{
    Pickup = 1,
    Delivery = 2,
    DineIn = 3
}

public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Preparing = 3,
    Ready = 4,
    OutForDelivery = 5,
    Delivered = 6,
    Cancelled = 7
}

public enum PaymentMethod
{
    Cash = 1,
    Pix = 2,
    CardOnDelivery = 3,
    Online = 4
}

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Refunded = 3
}

public enum OrderChannel
{
    Web = 1,
    WhatsApp = 2
}
