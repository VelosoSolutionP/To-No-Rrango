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
    WhatsApp = 2,
    Pdv = 3
}

/// <summary>Where an item is made. Drinks are picked at the bar; food is cooked in the kitchen.</summary>
public enum PrepStation
{
    Kitchen = 1,
    Bar = 2
}

/// <summary>
/// The progress of one item. Kitchen items go through every step. Bar items skip the queue and
/// the finishing step: received, being picked (Preparing), picked (Ready), delivered.
/// </summary>
public enum ItemPrepStatus
{
    Received = 1,
    Queued = 2,
    Preparing = 3,
    Finishing = 4,
    Ready = 5,
    Delivered = 6
}
