namespace Jentl.Traffic;

/// <summary>
/// A kind of vehicle and how it drives, in metres and seconds: its size, top speed, how hard it speeds up and brakes
/// in comfort, the gap it keeps when stopped, and the time gap it keeps behind the vehicle ahead (the Intelligent
/// Driver Model's parameters).
/// </summary>
public sealed record VehicleKind(string Name, float Length, float Width, float MaxSpeed, float Acceleration, float Braking, float MinGap, float TimeGap)
{
    public static VehicleKind Car { get; } = new("car", 4.5f, 1.8f, 38f, 1.8f, 3f, 2f, 1.3f);

    public static VehicleKind Van { get; } = new("van", 5.6f, 2.0f, 33f, 1.4f, 2.8f, 2f, 1.5f);

    public static VehicleKind Truck { get; } = new("truck", 12f, 2.5f, 25f, 0.8f, 2.2f, 3f, 1.8f);

    public static VehicleKind Bus { get; } = new("bus", 12f, 2.5f, 25f, 1.0f, 2.2f, 3f, 1.6f);

    public static VehicleKind Motorbike { get; } = new("motorbike", 2.2f, 0.8f, 40f, 2.8f, 4f, 1.5f, 1.0f);

    /// <summary>A bicycle, at about 20 km/h: it keeps to cycle paths (see <see cref="Cyclist"/>).</summary>
    public static VehicleKind Bicycle { get; } = new("bicycle", 1.8f, 0.6f, 5.5f, 1.5f, 2.5f, 1.0f, 1.0f) { Cyclist = true };

    /// <summary>Whether it rides on cycle paths rather than the road.</summary>
    public bool Cyclist { get; init; }

    /// <summary>A town's mix: mostly cars, some vans, a few trucks, buses and motorbikes.</summary>
    public static IReadOnlyList<(VehicleKind Kind, float Weight)> TownMix { get; } =
        [(Car, 0.72f), (Van, 0.12f), (Truck, 0.06f), (Bus, 0.04f), (Motorbike, 0.06f)];
}
