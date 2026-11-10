using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;

namespace MuzyWeaponsPhysics;

[MinimumApiVersion(80)]
public sealed class MuzyWeaponsPhysicsPlugin : BasePlugin, IPluginConfig<MuzyWeaponsPhysicsConfig>
{
    private const int PruneIntervalTicks = 64;
    private const float DefaultEyeHeight = 64.0f;

    private static readonly HashSet<string> ExcludedWeaponNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "weapon_c4",
        "weapon_knife",
        "weapon_knife_t",
        "weapon_bayonet",
        "weapon_fists",
        "weapon_flashbang",
        "weapon_smokegrenade",
        "weapon_hegrenade",
        "weapon_molotov",
        "weapon_incgrenade",
        "weapon_decoy",
        "weapon_tagrenade",
        "weapon_healthshot",
        "weapon_breachcharge",
        "weapon_bumpmine",
        "weapon_shield",
        "weapon_tablet"
    };

    private readonly HashSet<uint> _trackedWeaponIndices = [];
    private readonly Dictionary<uint, DateTime> _weaponImpulseCooldownsUtc = [];

    private bool _entitySystemReady;
    private int _nextPruneTick;

    private enum ImpactKind
    {
        None,
        Bullet,
        Knife
    }

    public override string ModuleName => "zMuzy WeaponPhysics";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Made by zMuzy (with AI assistance)";
    public override string ModuleDescription => "Makes loose weapons react much more strongly to bullets and knife hits.";

    public MuzyWeaponsPhysicsConfig Config { get; set; } = new();

    public void OnConfigParsed(MuzyWeaponsPhysicsConfig config)
    {
        Config = config;

        Config.BulletImpactRadius = Math.Clamp(Config.BulletImpactRadius, 8.0f, 256.0f);
        Config.BulletBoostSpeed = Math.Clamp(Config.BulletBoostSpeed, 50.0f, 4000.0f);
        Config.KnifeReach = Math.Clamp(Config.KnifeReach, 16.0f, 256.0f);
        Config.KnifeLateralTolerance = Math.Clamp(Config.KnifeLateralTolerance, 8.0f, 160.0f);
        Config.KnifeBoostSpeed = Math.Clamp(Config.KnifeBoostSpeed, 50.0f, 4000.0f);
        Config.VerticalBoost = Math.Clamp(Config.VerticalBoost, 0.0f, 1000.0f);
        Config.MaxWeaponSpeed = Math.Clamp(Config.MaxWeaponSpeed, 100.0f, 5000.0f);
        Config.ImpulseCooldownSeconds = Math.Clamp(Config.ImpulseCooldownSeconds, 0.0f, 1.0f);
    }

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        RegisterListener<Listeners.OnEntityDeleted>(OnEntityDeleted);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamagePre);
        RegisterListener<Listeners.OnTick>(OnTick);

        RegisterEventHandler<EventBulletImpact>(OnBulletImpact);
        RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
    }

    public override void Unload(bool hotReload)
    {
        _trackedWeaponIndices.Clear();
        _weaponImpulseCooldownsUtc.Clear();
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        TryEnsureEntitySystemReady();
        PruneTrackedWeapons();
        return HookResult.Continue;
    }

    private void OnMapStart(string mapName)
    {
        _entitySystemReady = false;
        TryEnsureEntitySystemReady();
    }

    private void OnEntitySpawned(CEntityInstance entity)
    {
        TryTrackWeapon(entity);
    }

    private void OnEntityDeleted(CEntityInstance entity)
    {
        _trackedWeaponIndices.Remove(entity.Index);
        _weaponImpulseCooldownsUtc.Remove(entity.Index);
    }

    private void OnTick()
    {
        if (!TryEnsureEntitySystemReady())
            return;

        if (Server.TickCount < _nextPruneTick)
            return;

        _nextPruneTick = Server.TickCount + PruneIntervalTicks;
        PruneTrackedWeapons();
    }

    private HookResult OnEntityTakeDamagePre(CBaseEntity entity, CTakeDamageInfo info)
    {
        if (!Config.Enabled || !_entitySystemReady)
            return HookResult.Continue;

        if (!TryGetLooseWeapon(entity.Index, out var weapon))
            return HookResult.Continue;

        var kind = ClassifyDamage(info);
        if (kind == ImpactKind.None)
            return HookResult.Continue;

        if (!TryResolveDamageDirection(info, weapon!, out var direction))
            return HookResult.Continue;

        var strength = kind == ImpactKind.Knife ? Config.KnifeBoostSpeed : Config.BulletBoostSpeed;
        ApplyImpulse(weapon!, direction, strength);
        return HookResult.Continue;
    }

    private HookResult OnBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        if (!Config.Enabled || !Config.EnableBulletBoost || !_entitySystemReady)
            return HookResult.Continue;

        var attacker = @event.Userid;
        if (!IsValidHuman(attacker))
            return HookResult.Continue;

        var impactPoint = new Vector(@event.X, @event.Y, @event.Z);
        if (!TryFindClosestLooseWeapon(impactPoint, Config.BulletImpactRadius, out var weapon))
            return HookResult.Continue;

        var direction = GetPushDirectionFromPoint(attacker!, weapon!, impactPoint);
        ApplyImpulse(weapon!, direction, Config.BulletBoostSpeed);
        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        if (!Config.Enabled || !Config.EnableKnifeBoost || !_entitySystemReady)
            return HookResult.Continue;

        if (!IsKnifeWeaponName(@event.Weapon))
            return HookResult.Continue;

        if (!IsValidHuman(@event.Userid))
            return HookResult.Continue;

        if (!TryFindKnifeTarget(@event.Userid!, out var weapon, out var direction))
            return HookResult.Continue;

        ApplyImpulse(weapon!, direction, Config.KnifeBoostSpeed);
        return HookResult.Continue;
    }

    private void RescanWeapons()
    {
        _trackedWeaponIndices.Clear();
        foreach (var entity in Utilities.GetAllEntities())
            TryTrackWeapon(entity);
    }

    private bool TryEnsureEntitySystemReady()
    {
        if (_entitySystemReady)
            return true;

        try
        {
            RescanWeapons();
            _entitySystemReady = true;
            return true;
        }
        catch (NativeException)
        {
            return false;
        }
    }

    private void PruneTrackedWeapons()
    {
        var staleIndices = new List<uint>();
        foreach (var index in _trackedWeaponIndices)
        {
            var entity = Utilities.GetEntityFromIndex<CEntityInstance>((int)index);
            if (entity == null || !entity.IsValid || !IsSupportedWeaponDesignerName(entity.DesignerName))
                staleIndices.Add(index);
        }

        for (var i = 0; i < staleIndices.Count; i++)
        {
            _trackedWeaponIndices.Remove(staleIndices[i]);
            _weaponImpulseCooldownsUtc.Remove(staleIndices[i]);
        }
    }

    private void TryTrackWeapon(CEntityInstance? entity)
    {
        if (entity == null || !entity.IsValid)
            return;

        if (!IsSupportedWeaponDesignerName(entity.DesignerName))
            return;

        _trackedWeaponIndices.Add(entity.Index);
    }

    private bool TryGetLooseWeapon(uint entityIndex, out CBasePlayerWeapon? weapon)
    {
        weapon = Utilities.GetEntityFromIndex<CBasePlayerWeapon>((int)entityIndex);
        if (weapon == null || !weapon.IsValid)
            return false;

        if (!IsLooseWeapon(weapon))
            return false;

        return true;
    }

    private bool TryFindClosestLooseWeapon(Vector origin, float radius, out CBasePlayerWeapon? weapon)
    {
        weapon = null;
        var radiusSquared = radius * radius;
        var bestDistanceSquared = radiusSquared;

        foreach (var index in _trackedWeaponIndices)
        {
            if (!TryGetLooseWeapon(index, out var candidate))
                continue;

            var candidateOrigin = candidate!.AbsOrigin;
            if (candidateOrigin == null)
                continue;

            var delta = candidateOrigin - origin;
            var distanceSquared = delta.LengthSqr();
            if (distanceSquared > bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            weapon = candidate;
        }

        return weapon != null;
    }

    private bool TryFindKnifeTarget(CCSPlayerController attacker, out CBasePlayerWeapon? weapon, out Vector direction)
    {
        weapon = null;
        direction = Vector.Zero;

        var pawn = attacker.PlayerPawn.Value;
        var pawnOrigin = pawn?.AbsOrigin;
        var eyeAngles = pawn?.EyeAngles;
        if (pawn == null || !pawn.IsValid || pawnOrigin == null || eyeAngles == null)
            return false;

        var eyeOrigin = pawnOrigin + new Vector(0.0f, 0.0f, DefaultEyeHeight);
        var forward = GetForwardVector(eyeAngles);
        if (forward.LengthSqr() <= 0.0001f)
            return false;

        var bestForwardDistance = float.MaxValue;
        var bestLateralDistanceSquared = float.MaxValue;
        var lateralLimitSquared = Config.KnifeLateralTolerance * Config.KnifeLateralTolerance;

        foreach (var index in _trackedWeaponIndices)
        {
            if (!TryGetLooseWeapon(index, out var candidate))
                continue;

            var candidateOrigin = candidate!.AbsOrigin;
            if (candidateOrigin == null)
                continue;

            var toWeapon = candidateOrigin - eyeOrigin;
            var forwardDistance = Dot(toWeapon, forward);
            if (forwardDistance <= 0.0f || forwardDistance > Config.KnifeReach)
                continue;

            var lateralDistanceSquared = MathF.Max(0.0f, toWeapon.LengthSqr() - (forwardDistance * forwardDistance));
            if (lateralDistanceSquared > lateralLimitSquared)
                continue;

            if (lateralDistanceSquared > bestLateralDistanceSquared)
                continue;

            if (Math.Abs(lateralDistanceSquared - bestLateralDistanceSquared) < 0.001f && forwardDistance >= bestForwardDistance)
                continue;

            bestLateralDistanceSquared = lateralDistanceSquared;
            bestForwardDistance = forwardDistance;
            weapon = candidate;
        }

        if (weapon?.AbsOrigin == null)
            return false;

        direction = NormalizeOrZero(weapon.AbsOrigin - eyeOrigin);
        return direction.LengthSqr() > 0.0001f;
    }

    private ImpactKind ClassifyDamage(CTakeDamageInfo info)
    {
        var damageType = info.BitsDamageType;
        var attackerWeaponName = TryGetAttackerWeaponName(info);
        var inflictorName = info.Inflictor.Get()?.DesignerName;

        if (Config.EnableKnifeBoost &&
            (IsKnifeWeaponName(attackerWeaponName) ||
             IsKnifeWeaponName(inflictorName) ||
             (damageType & DamageTypes_t.DMG_SLASH) != 0 ||
             (damageType & DamageTypes_t.DMG_CLUB) != 0))
        {
            return ImpactKind.Knife;
        }

        if (Config.EnableBulletBoost &&
            (IsGunWeaponName(attackerWeaponName) ||
             IsGunWeaponName(inflictorName) ||
             (damageType & DamageTypes_t.DMG_BULLET) != 0 ||
             (damageType & DamageTypes_t.DMG_BUCKSHOT) != 0))
        {
            return ImpactKind.Bullet;
        }

        return ImpactKind.None;
    }

    private bool TryResolveDamageDirection(CTakeDamageInfo info, CBasePlayerWeapon weapon, out Vector direction)
    {
        direction = NormalizeOrZero(info.DamageForce);
        if (direction.LengthSqr() > 0.0001f)
            return true;

        direction = NormalizeOrZero(info.DamageDirection);
        if (direction.LengthSqr() > 0.0001f)
            return true;

        var attackerOrigin = TryGetAttackerOrigin(info);
        if (attackerOrigin == null || weapon.AbsOrigin == null)
            return false;

        direction = NormalizeOrZero(weapon.AbsOrigin - attackerOrigin);
        return direction.LengthSqr() > 0.0001f;
    }

    private void ApplyImpulse(CBasePlayerWeapon weapon, Vector direction, float speed)
    {
        if (!TryBeginImpulseCooldown(weapon.Index))
            return;

        if (weapon.AbsOrigin == null)
            return;

        direction = NormalizeOrZero(direction);
        if (direction.LengthSqr() <= 0.0001f)
            return;

        var currentVelocity = CopyVector(weapon.AbsVelocity);
        var boostedVelocity = currentVelocity + (direction * speed);
        boostedVelocity.Z = MathF.Max(boostedVelocity.Z, Config.VerticalBoost);
        boostedVelocity = ClampVectorLength(boostedVelocity, Config.MaxWeaponSpeed);

        weapon.Teleport(velocity: boostedVelocity);
    }

    private bool TryBeginImpulseCooldown(uint entityIndex)
    {
        var now = DateTime.UtcNow;
        if (_weaponImpulseCooldownsUtc.TryGetValue(entityIndex, out var cooldownUntilUtc) && cooldownUntilUtc > now)
            return false;

        _weaponImpulseCooldownsUtc[entityIndex] = now.AddSeconds(Config.ImpulseCooldownSeconds);
        return true;
    }

    private static Vector GetPushDirectionFromPoint(CCSPlayerController attacker, CBasePlayerWeapon weapon, Vector impactPoint)
    {
        var fromImpact = weapon.AbsOrigin == null
            ? Vector.Zero
            : NormalizeOrZero(weapon.AbsOrigin - impactPoint);

        if (fromImpact.LengthSqr() > 0.0001f)
            return fromImpact;

        var attackerPawnOrigin = attacker.PlayerPawn.Value?.AbsOrigin;
        if (attackerPawnOrigin == null || weapon.AbsOrigin == null)
            return GetForwardVector(attacker.PlayerPawn.Value?.EyeAngles);

        return NormalizeOrZero(weapon.AbsOrigin - attackerPawnOrigin);
    }

    private static Vector? TryGetAttackerOrigin(CTakeDamageInfo info)
    {
        var attackerEntity = info.Attacker.Get();
        if (attackerEntity is null || !attackerEntity.IsValid)
            return null;

        return attackerEntity.AbsOrigin;
    }

    private static string? TryGetAttackerWeaponName(CTakeDamageInfo info)
    {
        var attackerEntity = info.Attacker.Get();
        switch (attackerEntity)
        {
            case CCSPlayerController controller when controller.IsValid:
                return controller.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value?.DesignerName;

            case CCSPlayerPawn pawn when pawn.IsValid:
                return pawn.WeaponServices?.ActiveWeapon.Value?.DesignerName;
        }

        return info.Inflictor.Get()?.DesignerName;
    }

    private static bool IsValidHuman(CCSPlayerController? player)
        => player != null && player.IsValid && !player.IsBot && player.PawnIsAlive;

    private static bool IsLooseWeapon(CBasePlayerWeapon weapon)
    {
        if (!weapon.IsValid || !IsSupportedWeaponDesignerName(weapon.DesignerName))
            return false;

        return weapon.OwnerEntity.Value == null;
    }

    private static bool IsSupportedWeaponDesignerName(string? designerName)
    {
        if (string.IsNullOrWhiteSpace(designerName))
            return false;

        if (!designerName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
            return false;

        return !ExcludedWeaponNames.Contains(designerName);
    }

    private static bool IsKnifeWeaponName(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName))
            return false;

        return weaponName.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
               weaponName.Contains("bayonet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGunWeaponName(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName))
            return false;

        if (!weaponName.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
            return false;

        return !IsKnifeWeaponName(weaponName) && !ExcludedWeaponNames.Contains(weaponName);
    }

    private static Vector CopyVector(Vector source)
        => new(source.X, source.Y, source.Z);

    private static Vector NormalizeOrZero(Vector? source)
    {
        if (source == null)
            return Vector.Zero;

        var copy = CopyVector(source);
        var length = copy.Length();
        if (length <= 0.0001f)
            return Vector.Zero;

        return copy / length;
    }

    private static Vector ClampVectorLength(Vector vector, float maxLength)
    {
        var length = vector.Length();
        if (length <= maxLength || length <= 0.0001f)
            return vector;

        return vector * (maxLength / length);
    }

    private static float Dot(Vector left, Vector right)
        => left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static Vector GetForwardVector(QAngle? angles)
    {
        if (angles == null)
            return Vector.Zero;

        var pitch = MathF.PI / 180.0f * angles.X;
        var yaw = MathF.PI / 180.0f * angles.Y;
        var cosPitch = MathF.Cos(pitch);

        return NormalizeOrZero(new Vector(
            cosPitch * MathF.Cos(yaw),
            cosPitch * MathF.Sin(yaw),
            -MathF.Sin(pitch)));
    }
}
