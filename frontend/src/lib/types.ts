export type FulfillmentType = "Pickup" | "Delivery" | "DineIn";

export type PaymentMethod = "Cash" | "Pix" | "CardOnDelivery" | "Online";

export type PaymentStatus = "Pending" | "Paid" | "Refunded";

export type OrderStatus =
  | "Pending"
  | "Confirmed"
  | "Preparing"
  | "Ready"
  | "OutForDelivery"
  | "Delivered"
  | "Cancelled";

export interface Establishment {
  id: string;
  tenantId: string;
  name: string;
  slug: string;
  description: string | null;
  logoUrl: string | null;
  coverUrl: string | null;
  phone: string | null;
  whatsApp: string | null;
  addressLine: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  timeZone: string;
  isActive: boolean;
  isPickupEnabled: boolean;
  isDeliveryEnabled: boolean;
  isDineInEnabled: boolean;
  deliveryFeeInCents: number;
  minimumOrderInCents: number;
  primaryColor: string;
  secondaryColor: string;
  openingHours?: string | null;
  acceptsOrdersWhenClosed?: boolean;
  isOpenNow?: boolean;
  nextOpeningAt?: string | null;
  hoursText?: string | null;
}

export type PrepStation = "Kitchen" | "Bar";

export type ItemPrepStatus = "Received" | "Queued" | "Preparing" | "Finishing" | "Ready" | "Delivered";

export interface MenuComboPart {
  menuItemId: string;
  name: string;
  quantity: number;
  priceInCents: number;
}

export interface MenuOption {
  id: string;
  name: string;
  priceInCents: number;
  isAvailable: boolean;
  sortOrder: number;
}

export interface MenuOptionGroup {
  id: string;
  name: string;
  minSelections: number;
  maxSelections: number;
  isRequired: boolean;
  sortOrder: number;
  options: MenuOption[];
}

export interface MenuItem {
  id: string;
  categoryId: string;
  name: string;
  description: string | null;
  imageUrl: string | null;
  priceInCents: number;
  isActive: boolean;
  isAvailable: boolean;
  sortOrder: number;
  optionGroups: MenuOptionGroup[];
  comboParts?: MenuComboPart[] | null;
  comboSavingsInCents?: number | null;
  compareAtPriceInCents?: number | null;
  sku?: string | null;
  barcode?: string | null;
  unit?: string;
}

export interface MenuCategory {
  id: string;
  name: string;
  description: string | null;
  sortOrder: number;
  isActive: boolean;
  items: MenuItem[];
  station?: PrepStation;
}

export interface PublicMenu {
  establishment: Establishment;
  categories: MenuCategory[];
}

export interface DeliveryAddress {
  street: string;
  number: string;
  complement: string | null;
  district: string;
  city: string;
  state: string;
  postalCode: string;
  reference?: string | null;
}

export interface CreateOrderItem {
  menuItemId: string;
  quantity: number;
  notes: string | null;
  optionIds: string[];
}

export interface CreateOrderRequest {
  customerName: string;
  customerPhone: string;
  fulfillmentType: FulfillmentType;
  paymentMethod: PaymentMethod;
  notes: string | null;
  idempotencyKey: string | null;
  deliveryAddress: DeliveryAddress | null;
  items: CreateOrderItem[];
  tableLabel?: string;
}

export interface OrderItemOption {
  groupName: string;
  name: string;
  priceInCents: number;
}

export interface OrderItem {
  id: string;
  menuItemId: string;
  name: string;
  unitPriceInCents: number;
  quantity: number;
  notes: string | null;
  totalInCents: number;
  options: OrderItemOption[];
  station?: PrepStation;
  prepStatus?: ItemPrepStatus;
  prepUpdatedAt?: string | null;
}

export interface OrderStatusHistory {
  fromStatus: OrderStatus | null;
  toStatus: OrderStatus;
  changedAt: string;
  changedBy: string | null;
  reason: string | null;
}

export interface OrderTracking {
  code: string;
  fulfillmentType: FulfillmentType;
  status: OrderStatus;
  paymentMethod: PaymentMethod;
  paymentStatus: PaymentStatus;
  channel?: "Web" | "WhatsApp" | "Pdv" | "Waiter" | "Table";
  scheduledFor?: string | null;
  discountInCents?: number;
  tableLabel?: string | null;
  courierName?: string | null;
  customerName: string;
  customerPhoneMasked: string;
  subtotalInCents: number;
  deliveryFeeInCents: number;
  totalInCents: number;
  createdAt: string;
  confirmedAt: string | null;
  readyAt: string | null;
  deliveredAt: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  items: OrderItem[];
  statusHistory: OrderStatusHistory[];
}

export interface AdminOrder extends OrderTracking {
  id: string;
  establishmentId: string;
  customerPhone: string;
  notes: string | null;
  deliveryAddress: DeliveryAddress | null;
  updatedAt: string;
  discountReason?: string | null;
  deliveryReference?: string | null;
  waiterName?: string | null;
}

export type UserRole = "Owner" | "Manager" | "Attendant" | "Kitchen";

export interface AdminSession {
  token: string;
  expiresAt: string;
  tenantId: string;
  userName: string;
  role: UserRole;
  modules: string[];
  establishmentId?: string;
}

export interface CheckoutOptions {
  pixOnline: boolean;
  cardOnline: boolean;
  publicKey: string | null;
  hasDeliveryZones: boolean;
  checkoutLink?: boolean;
}

export type ChargeStatus = "Pending" | "Approved" | "Rejected" | "Cancelled" | "Refunded";

export interface OrderPayment {
  channel: "Pix" | "Card" | "Checkout";
  status: ChargeStatus;
  statusDetail: string | null;
  amountInCents: number;
  pixQrCode: string | null;
  pixQrCodeBase64: string | null;
  expiresAt: string | null;
}

export interface CepData {
  postalCode: string;
  street: string | null;
  district: string | null;
  city: string;
  state: string;
}

export interface CreateEstablishmentPayload {
  tenantId: string;
  name: string;
  slug: string;
  description: string | null;
  phone: string | null;
  whatsApp: string | null;
  addressLine: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  logoUrl: string | null;
  coverUrl: string | null;
  isPickupEnabled: boolean;
  isDeliveryEnabled: boolean;
  isDineInEnabled: boolean;
  deliveryFeeInCents: number;
  minimumOrderInCents: number;
  primaryColor: string;
  secondaryColor: string;
}

export interface UpdateEstablishmentPayload {
  logoUrl: string | null;
  coverUrl: string | null;
  name: string;
  description: string | null;
  phone: string | null;
  whatsApp: string | null;
  addressLine: string | null;
  city: string | null;
  state: string | null;
  postalCode: string | null;
  timeZone: string;
  primaryColor: string;
  secondaryColor: string;
  isActive: boolean;
  isPickupEnabled: boolean;
  isDeliveryEnabled: boolean;
  isDineInEnabled: boolean;
  deliveryFeeInCents: number;
  minimumOrderInCents: number;
  openingHours?: string | null;
  acceptsOrdersWhenClosed?: boolean | null;
}

export interface CategoryPayload {
  name: string;
  description: string | null;
  sortOrder: number;
  station?: PrepStation;
}

export interface MenuOptionPayload {
  name: string;
  priceInCents: number;
  isAvailable: boolean;
  sortOrder: number;
}

export interface MenuOptionGroupPayload {
  name: string;
  minSelections: number;
  maxSelections: number;
  isRequired: boolean;
  sortOrder: number;
  options: MenuOptionPayload[];
}

export interface MenuItemPayload {
  categoryId: string;
  name: string;
  description: string | null;
  imageUrl: string | null;
  priceInCents: number;
  sortOrder: number;
  isAvailable: boolean;
  optionGroups: MenuOptionGroupPayload[];
  comboLines?: Array<{ menuItemId: string; quantity: number }> | null;
  compareAtPriceInCents?: number | null;
}

export interface UpdateOrderStatusPayload {
  status: OrderStatus;
  reason: string | null;
  changedBy: string | null;
}

export interface RatingState {
  canRate: boolean;
  isRated: boolean;
  courierName: string | null;
}

export interface RatePayload {
  serviceScore: number;
  productScore: number;
  overallScore: number;
  storeComment: string | null;
  courierServiceScore: number | null;
  courierPunctualityScore: number | null;
  courierCareScore: number | null;
  courierComment: string | null;
}

export const prepLabels: Record<PrepStation, Partial<Record<ItemPrepStatus, string>>> = {
  Kitchen: {
    Received: "Recebido",
    Queued: "Na fila",
    Preparing: "Em preparação",
    Finishing: "Em finalização",
    Ready: "Pronto",
    Delivered: "Entregue",
  },
  Bar: {
    Received: "Recebido",
    Preparing: "Em separação",
    Ready: "Separado",
    Delivered: "Entregue",
  },
};
