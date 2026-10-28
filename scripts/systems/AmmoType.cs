namespace ProjectD;

/// <summary>One cartridge type (GDD s8): damage, penetration tier 1-5, muzzle velocity in m/s.</summary>
/// <param name="Dispersion">Change to the gun's dispersion as a fraction: 0.05 is +5% (a wider group), -0.1 is 10% tighter.</param>
public sealed record AmmoType(string Id, float Damage, int PenetrationTier, float MuzzleVelocity, float Dispersion = 0f);
