using Celeste.Mod.Entities;

namespace ChroniaHelper.Entities;

/// <summary>
/// 瀑布内玩家落地、游泳都不回复冲刺。
/// 判定体积 = 房间里每个 Waterfall 的范围（按 inset 左右收窄）。
/// </summary>
[CustomEntity("ChroniaHelper/RippleWaterfallNoDashRefillController")]
public sealed class RippleWaterfallNoDashRefillController : Entity
{
    private readonly List<Rectangle> volumes = new();
    private readonly List<Rectangle> scratch = new();
    private readonly int inset;

    public RippleWaterfallNoDashRefillController(EntityData data, Vector2 offset)
        : base(data.Position + offset)
    {
        Visible = false;

        // 左右各收缩 inset 像素，避免擦边就被判定为在瀑布里
        inset = Math.Max(0, data.Int("inset", 0));

        // 比 Player（Depth 0）先更新，保证本帧判定体积是最新的
        Depth = 1;
    }

    public override void Update()
    {
        base.Update();
        UpdateColliderIfChanged();
    }

    internal static bool PlayerInsideWaterfall(Player player)
    {
        foreach (Entity entity in player.Scene.Entities)
        {
            if (entity is RippleWaterfallNoDashRefillController controller
                && controller.Collider != null
                && player.CollideCheck(controller))
            {
                return true;
            }
        }

        return false;
    }

    internal static void Player_Update(On.Celeste.Player.orig_Update orig, Player self)
    {
        if (self.Scene is not Level level)
        {
            orig(self);
            return;
        }

        bool block = !self.Dead && PlayerInsideWaterfall(self);
        bool original = level.Session.Inventory.NoRefills;

        if (block)
            level.Session.Inventory.NoRefills = true;

        try
        {
            orig(self);
        }
        finally
        {
            if (block)
                level.Session.Inventory.NoRefills = original;
        }
    }

    private void UpdateColliderIfChanged()
    {
        scratch.Clear();

        foreach (Entity entity in Scene.Entities)
        {
            if (entity is not RippleWaterfall waterfall || !waterfall.Active)
                continue;

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
