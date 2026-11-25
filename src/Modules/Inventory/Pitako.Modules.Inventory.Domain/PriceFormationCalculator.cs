using Pitako.SharedKernel.Results;
using Pitako.SharedKernel.ValueObjects;

namespace Pitako.Modules.Inventory.Domain;

public sealed record PriceFormationInput(
    decimal CostPerPortion,
    decimal TaxPercent,
    decimal ChannelFeePercent,
    decimal OperationalOverheadPercent,
    decimal DesiredNetMarginPercent,
    decimal? ExplicitMarkupMultiplier = null,
    string Currency = Money.DefaultCurrency);

public sealed record PriceFormationResult(
    decimal CostPerPortion,
    Money BreakEvenPrice,
    Money SuggestedSellingPrice,
    decimal EffectiveMarkupMultiplier,
    Money EstimatedNetProfit,
    decimal EstimatedNetMarginPercent);

/// <summary>
/// Motor de formação de preços a partir do custo por porção da ficha técnica,
/// impostos, taxas de canal/marketplace, despesas operacionais e margem desejada (Seções 1 e 15).
/// </summary>
public static class PriceFormationCalculator
{
    public static Result<PriceFormationResult> Calculate(PriceFormationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.CostPerPortion < 0m)
            return Result.Failure<PriceFormationResult>(Error.Validation("Pricing.NegativeCost", "Custo por porção não pode ser negativo."));

        if (input.TaxPercent < 0m || input.ChannelFeePercent < 0m || input.OperationalOverheadPercent < 0m || input.DesiredNetMarginPercent < 0m)
            return Result.Failure<PriceFormationResult>(Error.Validation("Pricing.NegativePercent", "Percentuais não podem ser negativos."));

        var deductionsRatio = (input.TaxPercent + input.ChannelFeePercent + input.OperationalOverheadPercent) / 100m;
        if (deductionsRatio >= 1m)
            return Result.Failure<PriceFormationResult>(Error.Validation("Pricing.InvalidDeductions", "Soma de impostos e taxas não pode atingir ou ultrapassar 100%."));

        var breakEvenAmount = input.CostPerPortion / (1m - deductionsRatio);

        decimal sellingAmount;
        if (input.ExplicitMarkupMultiplier.HasValue)
        {
            if (input.ExplicitMarkupMultiplier.Value <= 0m)
                return Result.Failure<PriceFormationResult>(Error.Validation("Pricing.InvalidMarkup", "Markup multiplicador deve ser positivo."));

            sellingAmount = input.CostPerPortion * input.ExplicitMarkupMultiplier.Value;
        }
        else
        {
            var totalRatio = deductionsRatio + (input.DesiredNetMarginPercent / 100m);
            if (totalRatio >= 1m)
            {
                return Result.Failure<PriceFormationResult>(Error.Validation(
                    "Pricing.ImpossibleMargin",
                    "Soma de impostos, taxas, despesas e margem desejada não pode atingir ou ultrapassar 100% do preço de venda."));
            }

            sellingAmount = input.CostPerPortion / (1m - totalRatio);
        }

        var suggestedPrice = Money.Create(sellingAmount, input.Currency).Value;
        var breakEvenPrice = Money.Create(breakEvenAmount, input.Currency).Value;

        var totalDeductionsOnSale = suggestedPrice.Amount * deductionsRatio;
        var netProfitAmount = suggestedPrice.Amount - input.CostPerPortion - totalDeductionsOnSale;
        var netProfit = Money.Create(netProfitAmount, input.Currency, allowNegative: true).Value;

        var effectiveMarkup = input.CostPerPortion > 0m
            ? decimal.Round(suggestedPrice.Amount / input.CostPerPortion, 4, MidpointRounding.ToEven)
            : 0m;

        var actualMarginPct = suggestedPrice.Amount > 0m
            ? decimal.Round((netProfit.Amount / suggestedPrice.Amount) * 100m, 2, MidpointRounding.ToEven)
            : 0m;

        return Result.Success(new PriceFormationResult(
            input.CostPerPortion,
            breakEvenPrice,
            suggestedPrice,
            effectiveMarkup,
            netProfit,
            actualMarginPct));
    }
}
