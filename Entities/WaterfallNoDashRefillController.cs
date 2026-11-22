using Celeste.Mod.Entities;

namespace ChroniaHelper.Entities;

/// <summary>
/// 在 ChroniaHelper Waterfall 内部时，玩家落地/游泳不会回复冲刺。
/// 实现方式是在 <see cref="Player.Update"/> 期间临时把 <see cref="PlayerInventory.NoRefills"/> 设为 true，
/// 让原版的回复冲刺逻辑被跳过，更新结束后再恢复原值。
/// </summary>
[CustomEntity("ChroniaHelper/WaterfallNoDashRefillController")]
public sealed class WaterfallNoDashRefillController : Entity
{
    private readonly List<Rectangle> volumes = new();
    private readonly List<Rectangle> scratch = new();
    private readonly int inset;

    public WaterfallNoDashRefillController(EntityData data, Vector2 offset)
        : base(data.Position + offset)
    {
        Visible = false;

        // 和 no-grab controller 一样，左右各收缩 inset 像素，避免擦边就判定为在瀑布里
        inset = Math.Max(0, data.Int("inset", 0));

        // 在 Player（Depth 0）之前更新，保证本帧的判定体积是最新的
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
            if (entity is WaterfallNoDashRefillController controller
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
            if (entity is not Waterfall waterfall || !waterfall.Active)
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
