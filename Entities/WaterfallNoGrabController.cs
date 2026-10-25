using Celeste.Mod.Entities;
using MonoMod.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ChroniaHelper.Entities;

/// <summary>
/// While the player is inside any SorbetHelper ResizableWaterfall in this room, they cannot grab or
/// climb, matching the effect of CrystallineHelper's NoGrabTrigger: a <see cref="ClimbBlocker"/>
/// stops climbing from being started, and the ClimbUpdate hook kicks the player back to normal if
/// they are somehow already climbing.
/// </summary>
[CustomEntity("ChroniaHelper/WaterfallNoGrabController")]
public sealed class WaterfallNoGrabController : Entity
{
    private readonly ConditionalWeakTable<Entity, DynamicData> dynamicDataCache = new();
    private readonly List<Rectangle> volumes = new();
    private readonly List<Rectangle> scratch = new();
    private readonly int inset;

    // LiftBoost is private in Celeste.dll; CrystallineHelper reads it the same way.
    private static readonly PropertyInfo liftBoostProperty =
        typeof(Player).GetProperty("LiftBoost", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    public WaterfallNoGrabController(EntityData data, Vector2 offset)
        : base(data.Position + offset)
    {
        Visible = false;

        // shrink the detection area by this many pixels on each side, so barely touching the edge
        // of a waterfall does not count as being inside it
        inset = Math.Max(0, data.Int("inset", 1));

        // Same component CrystallineHelper's NoGrabTrigger adds. It only has an effect because this
        // entity's collider below covers the waterfall volumes.
        Add(new ClimbBlocker(true));

        // entities are updated in descending depth order, so 1 runs before the Player (depth 0),
        // making sure the collider is up to date for this frame's climb checks
        Depth = 1;
    }

    [ChroniaHelper.Cores.ExtendedAttributes.LoadHook]
    public static void Load()
    {
        On.Celeste.Player.ClimbUpdate += Player_ClimbUpdate;
    }
    [ChroniaHelper.Cores.ExtendedAttributes.UnloadHook]
    public static void Unload()
    {
        On.Celeste.Player.ClimbUpdate -= Player_ClimbUpdate;
    }

    private static int Player_ClimbUpdate(On.Celeste.Player.orig_ClimbUpdate orig, Player self)
    {
        if (!self.Dead && PlayerInsideWaterfall(self))
        {
            if (liftBoostProperty?.GetValue(self) is Vector2 liftBoost)
                self.Speed += liftBoost;

            return Player.StNormal;
        }

        return orig(self);
    }

    private static bool PlayerInsideWaterfall(Player player)
    {
        // Scene.Tracker.GetEntities<T>() throws for types that were never added in this room, so
        // just scan the room's entities (there is at most a handful of controllers).
        foreach (Entity entity in player.Scene.Entities)
        {
            if (entity is WaterfallNoGrabController controller && player.CollideCheck(controller))
                return true;
        }

        return false;
    }

    public override void Update()
    {
        base.Update();
        UpdateColliderIfChanged();
    }

    private void UpdateColliderIfChanged()
    {
        scratch.Clear();

        foreach (Entity entity in Scene.Entities)
        {
            if (entity.GetType().ToString() == "Celeste.Mod.SorbetHelper.Entities.ResizableWaterfall" && TryGetVolume(entity, out Rectangle volume))
                scratch.Add(volume);
        }

        if (SameVolumes(scratch, volumes))
            return;

        volumes.Clear();
        volumes.AddRange(scratch);

        if (volumes.Count == 0)
        {
            Collider = null;
            return;
        }

        Collider[] colliders = new Collider[volumes.Count];
        for (int i = 0; i < volumes.Count; i++)
        {
            Rectangle volume = volumes[i];
            colliders[i] = new Hitbox(volume.Width, volume.Height, volume.X - X, volume.Y - Y);
        }

        Collider = new ColliderList(colliders);
    }

    private bool TryGetVolume(Entity waterfall, out Rectangle volume)
    {
        volume = default;

        if (!waterfall.Collidable || !waterfall.Active)
            return false;

        if (!dynamicDataCache.TryGetValue(waterfall, out DynamicData data))
        {
            data = DynamicData.For(waterfall);
            dynamicDataCache.Add(waterfall, data);
        }

        // width comes from EntityData.Width; height is computed in Awake by probing downwards, so
        // the waterfall "droops" below its single placement point and both are needed.
        float width = ReadFloat(data, "width") ?? 0f;
        float height = ReadFloat(data, "height") ?? 0f;

        if (width <= 0f || height <= 0f)
            return false;

        // The waterfall occupies [X, X + width) x [Y, Y + height); shrink it horizontally on both
        // sides for a bit of tolerance at the edges.
        float judgedWidth = width - inset * 2f;
        if (judgedWidth <= 0f)
            return false;

        volume = new Rectangle(
            (int)MathF.Floor(waterfall.X + inset),
            (int)MathF.Floor(waterfall.Y),
            (int)MathF.Ceiling(judgedWidth),
            (int)MathF.Ceiling(height));

        return true;
    }

    private static bool SameVolumes(List<Rectangle> a, List<Rectangle> b)
    {
        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private static float? ReadFloat(DynamicData data, string name)
        => data.Get(name) switch
        {
            float f => f,
            int i => i,
            double d => (float)d,
            _ => null,
        };
}
