using System.Text.Json;
using Pitako.Modules.AcaiBuilder.Application.Abstractions;
using Pitako.Modules.AcaiBuilder.Domain;
using Pitako.Modules.Catalog.Application.Abstractions;
using Pitako.Modules.Coupons.Application.Abstractions;
using Pitako.Modules.Coupons.Domain;
using Pitako.Modules.Orders.Application.Abstractions;
using Pitako.Modules.Orders.Domain;
using Pitako.Modules.Weighing.Application.Abstractions;
using Pitako.MultiTenancy;
using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.Time;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Orders.Application.Services;

/// <summary>
/// Caso de uso de Carrinho de Compras com validação e recálculo integral no backend
/// (catálogo, açaí personalizado, balança e cupons), garantindo isolamento por item (Seções 17 e 18).
/// </summary>
public sealed class ShoppingCartService
{
    private readonly IShoppingCartRepository _cartRepo;
    private readonly ICatalogItemPricingService _catalogPricing;
    private readonly IAcaiAssemblyPricingService _acaiPricing;
    private readonly IWeighingOrderAttachmentService _weighingService;
    private readonly ICouponValidationService _couponService;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    public ShoppingCartService(
        IShoppingCartRepository cartRepo,
        ICatalogItemPricingService catalogPricing,
        IAcaiAssemblyPricingService acaiPricing,
        IWeighingOrderAttachmentService weighingService,
        ICouponValidationService couponService,
        ITenantContext tenantContext,
        IClock clock)
    {
        _cartRepo = cartRepo;
        _catalogPricing = catalogPricing;
        _acaiPricing = acaiPricing;
        _weighingService = weighingService;
        _couponService = couponService;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public async Task<Result<ShoppingCart>> GetOrCreateCartAsync(
        string cartSessionKey,
        string? customerIdentifier = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.EnsureResolved(_tenantContext);
        if (string.IsNullOrWhiteSpace(cartSessionKey))
            return Result.Failure<ShoppingCart>(Error.Validation("Cart.SessionRequired", "Sessão do carrinho é obrigatória."));

        var existing = await _cartRepo.FindBySessionKeyAsync(cartSessionKey.Trim(), cancellationToken);
        if (existing is not null)
        {
            TenantGuard.EnsureOwnership(_tenantContext, existing);
            if (!string.IsNullOrWhiteSpace(customerIdentifier))
            {
                existing.SetCustomerIdentifier(customerIdentifier);
                await _cartRepo.SaveChangesAsync(cancellationToken);
            }
            return Result.Success(existing);
        }

        var created = ShoppingCart.Create(tenantId, cartSessionKey, _clock.UtcNow.AddDays(7), customerIdentifier);
        if (created.IsFailure)
            return created;

        await _cartRepo.AddAsync(created.Value, cancellationToken);
        await _cartRepo.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<Result<ShoppingCart>> AddCatalogProductAsync(
        string cartSessionKey,
        Guid productId,
        Guid? variationId = null,
        IReadOnlyList<CatalogModifierSelection>? modifiers = null,
        int quantity = 1,
        string? customerNotes = null,
        bool forceSeparateLine = false,
        CancellationToken cancellationToken = default)
    {
        var cartRes = await GetOrCreateCartAsync(cartSessionKey, null, cancellationToken);
        if (cartRes.IsFailure)
            return cartRes;

        var quoteRes = await _catalogPricing.QuoteProductItemAsync(productId, variationId, modifiers, cancellationToken);
        if (quoteRes.IsFailure)
            return Result.Failure<ShoppingCart>(quoteRes.Error);

        var q = quoteRes.Value;
        var addRes = cartRes.Value.AddItem(
            CartItemKind.CatalogProduct,
            q.ProductId,
            q.CategoryId,
            q.VariationId,
            null,
            null,
            q.ProductName,
            q.CustomizationSummaryText,
            q.CanonicalSelectionsJson,
            q.UnitPrice,
            quantity,
            q.HasNonCombinablePromotion,
            customerNotes,
            _clock.UtcNow,
            forceSeparateLine);

        if (addRes.IsFailure)
            return Result.Failure<ShoppingCart>(addRes.Error);

        await ReevaluateAppliedCouponIfAnyAsync(cartRes.Value, cancellationToken);
        await _cartRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cartRes.Value);
    }

    public async Task<Result<ShoppingCart>> AddCustomAcaiCupAsync(
        string cartSessionKey,
        AcaiCupBuildRequest buildRequest,
        bool forceSeparateLine = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buildRequest);

        var cartRes = await GetOrCreateCartAsync(cartSessionKey, null, cancellationToken);
        if (cartRes.IsFailure)
            return cartRes;

        var builtRes = await _acaiPricing.BuildAndPriceCupAsync(buildRequest, cancellationToken);
        if (builtRes.IsFailure)
            return Result.Failure<ShoppingCart>(builtRes.Error);

        var b = builtRes.Value;
        var summaryParts = new List<string>();
        if (b.BaseFlavors.Count > 0)
            summaryParts.Add($"Base: {string.Join(", ", b.BaseFlavors.Select(f => f.Name))}");
        if (b.FreeToppings.Count > 0)
            summaryParts.Add($"Grátis: {string.Join(", ", b.FreeToppings.Select(f => $"{f.Quantity}x {f.Name}"))}");
        if (b.PaidAddons.Count > 0)
            summaryParts.Add($"Adicionais: {string.Join(", ", b.PaidAddons.Select(p => $"{p.Quantity}x {p.Name}"))}");

        var payloadJson = JsonSerializer.Serialize(new
        {
            cupSizeId = b.CupSizeId,
            flavors = b.BaseFlavors.OrderBy(f => f.FlavorId).Select(f => new { f.FlavorId, f.Name, extra = f.ExtraPrice.Amount }),
            freeToppings = b.FreeToppings.OrderBy(t => t.ToppingId).Select(t => new { t.ToppingId, t.Name, t.Quantity }),
            paidAddons = b.PaidAddons.OrderBy(a => a.AddonId).Select(a => new { a.AddonId, a.Name, a.Quantity, unit = a.UnitPrice.Amount })
        });

        var addRes = cartRes.Value.AddItem(
            CartItemKind.CustomAcaiCup,
            null,
            null,
            null,
            b.CupSizeId,
            null,
            $"Açaí {b.CupSizeName}",
            summaryParts.Count == 0 ? "Açaí puro" : string.Join(" | ", summaryParts),
            payloadJson,
            b.UnitFinalPrice,
            b.CupQuantity,
            false,
            b.CustomerNotes,
            _clock.UtcNow,
            forceSeparateLine);

        if (addRes.IsFailure)
            return Result.Failure<ShoppingCart>(addRes.Error);

        await ReevaluateAppliedCouponIfAnyAsync(cartRes.Value, cancellationToken);
        await _cartRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cartRes.Value);
    }

    public async Task<Result<ShoppingCart>> AddWeightedRecordAsync(
        string cartSessionKey,
        Guid weighingRecordId,
        Guid? categoryId = null,
        string? customerNotes = null,
        CancellationToken cancellationToken = default)
    {
        var cartRes = await GetOrCreateCartAsync(cartSessionKey, null, cancellationToken);
        if (cartRes.IsFailure)
            return cartRes;

        var recRes = await _weighingService.GetValidatedRecordAsync(weighingRecordId, cancellationToken);
        if (recRes.IsFailure)
            return Result.Failure<ShoppingCart>(recRes.Error);

        var r = recRes.Value;
        var summary = $"Líquido: {r.NetWeightGrams:N0} g (Tara: {r.TareWeightGrams:N0} g) @ {r.PricePerKilogram}/kg";
        var payloadJson = JsonSerializer.Serialize(new
        {
            weighingRecordId = r.Id,
            grossGrams = r.GrossWeightGrams,
            tareGrams = r.TareWeightGrams,
            netGrams = r.NetWeightGrams,
            pricePerKg = r.PricePerKilogram.Amount
        });

        var addRes = cartRes.Value.AddItem(
            CartItemKind.WeightedProduct,
            r.ProductId,
            categoryId,
            null,
            null,
            r.Id,
            r.ProductNameSnapshot,
            summary,
            payloadJson,
            r.TotalPrice,
            1,
            false,
            customerNotes,
            _clock.UtcNow,
            forceSeparateLine: true);

        if (addRes.IsFailure)
            return Result.Failure<ShoppingCart>(addRes.Error);

        await ReevaluateAppliedCouponIfAnyAsync(cartRes.Value, cancellationToken);
        await _cartRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cartRes.Value);
    }

    public async Task<Result<ShoppingCart>> SetDeliveryFeeAsync(
        string cartSessionKey,
        decimal deliveryFeeAmount,
        CancellationToken cancellationToken = default)
    {
        var cartRes = await GetOrCreateCartAsync(cartSessionKey, null, cancellationToken);
        if (cartRes.IsFailure)
            return cartRes;

        var moneyRes = Money.Create(deliveryFeeAmount);
        if (moneyRes.IsFailure)
            return Result.Failure<ShoppingCart>(moneyRes.Error);

        cartRes.Value.SetDeliveryFee(moneyRes.Value);
        await ReevaluateAppliedCouponIfAnyAsync(cartRes.Value, cancellationToken);
        await _cartRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cartRes.Value);
    }

    public async Task<Result<ShoppingCart>> ApplyCouponAsync(
        string cartSessionKey,
        string couponCode,
        string? customerIdentifier = null,
        CancellationToken cancellationToken = default)
    {
        var cartRes = await GetOrCreateCartAsync(cartSessionKey, customerIdentifier, cancellationToken);
        if (cartRes.IsFailure)
            return cartRes;

        var cart = cartRes.Value;
        var basketLines = cart.Items
            .Select(i => new CouponBasketLine(i.ProductId, i.CategoryId, i.LineSubtotal, i.HasNonCombinablePromotion))
            .ToList();

        var evalRes = await _couponService.EvaluateCouponAsync(
            couponCode,
            cart.CustomerIdentifier,
            basketLines,
            cart.DeliveryFee,
            cancellationToken);

        if (evalRes.IsFailure)
            return Result.Failure<ShoppingCart>(evalRes.Error);

        cart.ApplyCalculatedDiscounts(evalRes.Value.CouponCode, evalRes.Value.ItemsDiscount, evalRes.Value.DeliveryDiscount);
        await _cartRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cart);
    }

    public async Task<Result<ShoppingCart>> UpdateItemQuantityAsync(
        string cartSessionKey,
        Guid cartItemId,
        int newQuantity,
        CancellationToken cancellationToken = default)
    {
        var cartRes = await GetOrCreateCartAsync(cartSessionKey, null, cancellationToken);
        if (cartRes.IsFailure)
            return cartRes;

        var res = cartRes.Value.UpdateItemQuantity(cartItemId, newQuantity);
        if (res.IsFailure)
            return Result.Failure<ShoppingCart>(res.Error);

        await ReevaluateAppliedCouponIfAnyAsync(cartRes.Value, cancellationToken);
        await _cartRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cartRes.Value);
    }

    public async Task<Result<ShoppingCart>> RemoveCouponAsync(string cartSessionKey, CancellationToken cancellationToken = default)
    {
        var cartRes = await GetOrCreateCartAsync(cartSessionKey, null, cancellationToken);
        if (cartRes.IsFailure)
            return cartRes;

        cartRes.Value.ClearCoupon();
        await _cartRepo.SaveChangesAsync(cancellationToken);
        return Result.Success(cartRes.Value);
    }

    private async Task ReevaluateAppliedCouponIfAnyAsync(ShoppingCart cart, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cart.AppliedCouponCode))
            return;

        if (cart.Items.Count == 0)
        {
            cart.ClearCoupon();
            return;
        }

        var basketLines = cart.Items
            .Select(i => new CouponBasketLine(i.ProductId, i.CategoryId, i.LineSubtotal, i.HasNonCombinablePromotion))
            .ToList();

        var evalRes = await _couponService.EvaluateCouponAsync(
            cart.AppliedCouponCode,
            cart.CustomerIdentifier,
            basketLines,
            cart.DeliveryFee,
            cancellationToken);

        if (evalRes.IsSuccess)
            cart.ApplyCalculatedDiscounts(evalRes.Value.CouponCode, evalRes.Value.ItemsDiscount, evalRes.Value.DeliveryDiscount);
        else
            cart.ClearCoupon();
    }
}
