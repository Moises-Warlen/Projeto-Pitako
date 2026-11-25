using Pitako.SharedKernel.Results;

namespace Pitako.Modules.DeliveryLogistics.Domain;

public sealed record GeoCoordinate(double Latitude, double Longitude)
{
    public static Result<GeoCoordinate> Create(double latitude, double longitude)
    {
        if (latitude is < -90d or > 90d || double.IsNaN(latitude))
            return Result.Failure<GeoCoordinate>(Error.Validation("Geo.InvalidLatitude", "Latitude deve estar entre -90 e 90."));

        if (longitude is < -180d or > 180d || double.IsNaN(longitude))
            return Result.Failure<GeoCoordinate>(Error.Validation("Geo.InvalidLongitude", "Longitude deve estar entre -180 e 180."));

        return Result.Success(new GeoCoordinate(latitude, longitude));
    }
}

/// <summary>
/// Cálculo geodésico de distância via fórmula de Haversine em quilômetros (Seção 19).
/// </summary>
public static class GeoDistanceCalculator
{
    private const double EarthRadiusKm = 6371.0d;

    public static decimal CalculateKilometers(GeoCoordinate origin, GeoCoordinate destination)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);

        var dLat = ToRadians(destination.Latitude - origin.Latitude);
        var dLon = ToRadians(destination.Longitude - origin.Longitude);

        var lat1 = ToRadians(origin.Latitude);
        var lat2 = ToRadians(destination.Latitude);

        var a = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);

        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        var km = (decimal)(EarthRadiusKm * c);
        return decimal.Round(km, 2, MidpointRounding.ToEven);
    }

    private static double ToRadians(double degrees) => degrees * (Math.PI / 180d);
}
