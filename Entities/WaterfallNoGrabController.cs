using Celeste.Mod.Entities;
using System.Reflection;

namespace ChroniaHelper.Entities;

/// <summary>
/// While the player is inside any <see cref="Waterfall"/> in this room, they cannot grab or climb:
/// a <see cref="ClimbBlocker"/> stops climbing from being started, and the ClimbUpdate hook plus a
/// per-frame check kick the player back to normal if they are already climbing.
/// </summary>
[CustomEntity("ChroniaHelper/WaterfallNoGrabController")]
public sealed class WaterfallNoGrabController : Entity
{
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
        inset = Math.Max(0, data.Int("inset", 2));

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

        // 强制松开：ClimbBlocker 只能阻止「开始抓」，已经抓着的玩家会被 ClimbUpdate 一直保持。
        // 这里在 Player 之前（Depth 1 > 0）直接检查并切回 StNormal，覆盖被移动实体带进瀑布的情况。
        ForceReleaseGrabInside();
    }

    private void ForceReleaseGrabInside()
    {
        if (Collider == null)
            return;

        Player player = Scene?.Tracker?.GetEntity<Player>();

        if (player == null || player.Dead || player.StateMachine.State != Player.StClimb)
            return;

        if (!player.CollideCheck(this))
            return;

        if (liftBoostProperty?.GetValue(player) is Vector2 liftBoost)
            player.Speed += liftBoost;

        // 切回普通状态会走 ClimbEnd，抓取立即松开；这一帧的 ClimbBlocker 会阻止重新抓回去
        player.StateMachine.State = Player.StNormal;
    }

    private void UpdateColliderIfChanged()
    {
        scratch.Clear();

        foreach (Entity entity in Scene.Entities)
        {
            if (entity is not Waterfall waterfall || !waterfall.Active)
                continue;

            // The waterfall occupies [X, X + Width) x [Y, Y + Height); shrink it horizontally on
            // both sides for a bit of tolerance at the edges.
            float judgedWidth = waterfall.Width - inset * 2f;
            float judgedHeight = waterfall.RenderedHeight;

            if (judgedWidth <= 0f || judgedHeight <= 0f)
                continue;

            scratch.Add(new Rectangle(
                (int)MathF.Floor(waterfall.X + inset),
                (int)MathF.Floor(waterfall.Y),
                (int)MathF.Ceiling(judgedWidth),
                (int)MathF.Ceiling(judgedHeight)));
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
}
