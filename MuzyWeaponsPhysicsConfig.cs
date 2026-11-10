using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace MuzyWeaponsPhysics;

public sealed class MuzyWeaponsPhysicsConfig : BasePluginConfig
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("EnableBulletBoost")]
    public bool EnableBulletBoost { get; set; } = true;

    [JsonPropertyName("EnableKnifeBoost")]
    public bool EnableKnifeBoost { get; set; } = true;

    [JsonPropertyName("BulletImpactRadius")]
    public float BulletImpactRadius { get; set; } = 52.0f;

    [JsonPropertyName("BulletBoostSpeed")]
    public float BulletBoostSpeed { get; set; } = 1150.0f;

    [JsonPropertyName("KnifeReach")]
    public float KnifeReach { get; set; } = 125.0f;

    [JsonPropertyName("KnifeLateralTolerance")]
    public float KnifeLateralTolerance { get; set; } = 48.0f;

    [JsonPropertyName("KnifeBoostSpeed")]
    public float KnifeBoostSpeed { get; set; } = 1450.0f;

    [JsonPropertyName("VerticalBoost")]
    public float VerticalBoost { get; set; } = 65.0f;

    [JsonPropertyName("MaxWeaponSpeed")]
    public float MaxWeaponSpeed { get; set; } = 1700.0f;

    [JsonPropertyName("ImpulseCooldownSeconds")]
    public float ImpulseCooldownSeconds { get; set; } = 0.05f;
}
