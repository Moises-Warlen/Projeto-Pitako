export interface StoreOperatingHoursEntry {
  dayOfWeek: number;
  isOpen: boolean;
  openTime: string;
  closeTime: string;
}

export interface StoreOperatingStatus {
  isOpenNow: boolean;
  isTemporarilyPaused: boolean;
  pauseReason?: string | null;
  statusLabel: string;
  nextChangeText?: string | null;
  schedule: StoreOperatingHoursEntry[];
}

export interface StoreBannerSnapshot {
  title: string;
  subtitle?: string | null;
  imageUrl?: string | null;
  targetUrl?: string | null;
  displayOrder: number;
}

export interface MenuCategory {
  id: string;
  name: string;
  description?: string | null;
  imageUrl?: string | null;
  displayOrder: number;
  segmentTemplateCode?: string | null;
  ruleNotes?: string | null;
  templateKind?: number;
  templateKindName?: string;
  availabilityScheduleJson?: string | null;
  isScheduledAvailable?: boolean;
  status: string;
}

export interface MenuProductVariation {
  id: string;
  variationName: string;
  description?: string | null;
  price: number;
  measureAmount?: number | null;
  measureUnit?: string | null;
  maxFlavors: number;
  sku?: string | null;
  displayOrder: number;
  isActive?: boolean;
  trackInventory?: boolean;
  stockQuantity?: number;
  isInStock?: boolean;
}

export interface MenuVariationRule {
  id: string;
  variationId?: string | null;
  modifierGroupId: string;
  minSelections: number;
  maxSelections: number;
  freeAllowance: number;
  isRequired: boolean;
  isHidden: boolean;
}

export interface MenuProductImage {
  id: string;
  imageUrl: string;
  displayOrder: number;
  isPrimary: boolean;
}

export interface MenuProduct {
  id: string;
  categoryId: string;
  name: string;
  description?: string | null;
  sku?: string | null;
  price: number;
  promotionalPrice?: number | null;
  effectivePrice: number;
  discountAmount: number;
  discountPercent?: number;
  appliedPromotionName?: string | null;
  saleMode: string;
  pricingStrategy: string;
  pricingStrategyCode: number;
  isFeatured: boolean;
  isNew: boolean;
  isRecommended: boolean;
  allowsNotes: boolean;
  preparationMinutes?: number | null;
  trackInventory?: boolean;
  stockQuantity?: number;
  autoPauseOnZeroStock?: boolean;
  isInStock?: boolean;
  isScheduledAvailable?: boolean;
  servesPeopleCount?: number | null;
  dietaryTags?: string[];
  tags: string[];
  customTemplateKey?: string | null;
  displayOrder: number;
  salesCount: number;
  primaryImageUrl?: string | null;
  images: MenuProductImage[];
  variations: MenuProductVariation[];
  modifierGroupIds: string[];
  variationRules: MenuVariationRule[];
}

export interface MenuModifierOption {
  id: string;
  name: string;
  description?: string | null;
  additionalPrice: number;
  maxQuantity: number;
  displayOrder: number;
  isAvailable: boolean;
  replacesLabel?: string | null;
  imageUrl?: string | null;
  sku?: string | null;
  incompatibleOptionIds: string[];
}

export interface MenuModifierGroup {
  id: string;
  name: string;
  groupType: string;
  selectionKind: number;
  selectionKindName: string;
  minSelections: number;
  maxSelections: number;
  freeAllowance: number;
  isRequired: boolean;
  countingMode: number;
  categoryTag?: string | null;
  displayOrder: number;
  options: MenuModifierOption[];
}

export interface MenuCombo {
  id: string;
  name: string;
  description?: string | null;
  comboPrice: number;
  originalPrice: number;
  primaryImageUrl?: string | null;
  isFeatured: boolean;
  displayOrder: number;
  items: Array<{
    productId: string;
    productName: string;
    quantity: number;
    variationId?: string | null;
    notes?: string | null;
  }>;
}

export interface MenuPromotion {
  id: string;
  name: string;
  discountType: string;
  discountValue: number;
  startsAtUtc: string;
  endsAtUtc: string;
}

export interface PublicMenuResponse {
  tenantId: string;
  storeTradeName: string;
  segmentCode: string;
  usesPitakoBranding: boolean;
  operatingStatus?: StoreOperatingStatus | null;
  categories: MenuCategory[];
  products: MenuProduct[];
  bestSellerIds: string[];
  modifierGroups: MenuModifierGroup[];
  combos: MenuCombo[];
  promotions: MenuPromotion[];
  delivery?: {
    isDeliveryEnabled: boolean;
    allowPickup: boolean;
    isTemporarilyPaused: boolean;
    pauseReason?: string | null;
    calculationMode: string;
    baseFee: number;
    baseEstimatedMinutes: number;
    minimumOrderAmount: number;
    freeShippingThreshold?: number | null;
    zones: Array<{ id: string; zoneName: string; fee: number; estimatedMinutes: number }>;
  } | null;
  acai: {
    cupSizes: Array<{
      id: string;
      name: string;
      volumeMl: number;
      description?: string | null;
      basePrice: number;
      freeToppingsLimit: number;
      minFreeToppings?: number;
      countingMode?: string;
      allowFreeToppings?: boolean;
      allowPaidAddons?: boolean;
      maxPaidAddonsPerCup?: number | null;
      displayOrder?: number;
    }>;
    addonCategories?: Array<{
      id: string;
      name: string;
      kind: number;
      isFreeByDefault: boolean;
      description?: string | null;
      displayOrder: number;
    }>;
    baseFlavors: Array<{ id: string; name: string; extraPrice: number }>;
    freeToppings: Array<{
      id: string;
      name: string;
      description?: string | null;
      addonCategoryId?: string | null;
      categoryName?: string | null;
      maxPortionsPerCup: number;
      displayOrder?: number;
    }>;
    paidAddons: Array<{
      id: string;
      name: string;
      description?: string | null;
      addonCategoryId?: string | null;
      categoryName?: string | null;
      price: number;
      maxQuantityPerCup: number;
      displayOrder?: number;
    }>;
  };
}

export interface SmartCartItem {
  id: string;
  lineKind: string;
  productId?: string | null;
  categoryId?: string | null;
  variationId?: string | null;
  acaiCupSizeId?: string | null;
  itemTitle: string;
  customizationSummaryText: string;
  selectionsPayloadJson: string;
  unitPrice: number;
  quantity: number;
  lineSubtotal: number;
  customerNotes?: string | null;
}

export interface SmartCartResponse {
  id: string;
  cartSessionKey: string;
  customerIdentifier?: string | null;
  appliedCouponCode?: string | null;
  itemsSubtotal: number;
  itemsDiscount: number;
  deliveryFee: number;
  deliveryDiscount: number;
  totalAmount: number;
  items: SmartCartItem[];
}

export interface OrderTrackingResponse {
  id: string;
  orderNumber: string;
  status: string;
  statusCode: number;
  fulfillmentMode: string;
  customerName: string;
  deliveryAddressSummary?: string | null;
  paymentMethod?: string | null;
  isPaymentConfirmed?: boolean;
  pixBrCode?: string | null;
  appliedCouponCode?: string | null;
  itemsSubtotal: number;
  itemsDiscount: number;
  netDeliveryFee: number;
  totalAmount: number;
  placedAtUtc: string;
  completedAtUtc?: string | null;
  cancelledAtUtc?: string | null;
  cancellationReason?: string | null;
  items: Array<{
    id: string;
    itemTitle: string;
    customizationSummaryText: string;
    quantity: number;
    unitPrice: number;
    lineSubtotal: number;
    customerNotes?: string | null;
  }>;
  statusHistory: Array<{
    fromStatus?: string | null;
    toStatus: string;
    toStatusCode: number;
    reason: string;
    changedAtUtc: string;
  }>;
}
