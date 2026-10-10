namespace ProjectD;

/// <summary>One cartridge type (GDD s8): damage, penetration tier 1-5, muzzle velocity in m/s.</summary>
public sealed record AmmoType(string Id, float Damage, int PenetrationTier, float MuzzleVelocity);
