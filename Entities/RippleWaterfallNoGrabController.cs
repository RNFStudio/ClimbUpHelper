using Celeste.Mod.Entities;
using MonoMod.Utils;

namespace ChroniaHelper.Entities;

/// <summary>
/// 瀑布内玩家无法开始攀爬，也不能保持攀爬。
/// 判定体积 = 房间里每个 Waterfall 的范围（按 inset 左右收窄）。
/// </summary>
[CustomEntity("ChroniaHelper/RippleWaterfallNoGrabController")]
public sealed class RippleWaterfallNoGrabController : Entity
{
    private readonly List<Rectangle> volumes = new();
    private readonly List<Rectangle> scratch = new();
    private readonly int inset;

    public RippleWaterfallNoGrabController(EntityData data, Vector2 offset)
        : base(data.Position + offset)
    {
        Visible = false;

        // 左右各收缩 inset 像素，避免擦边就被判定为在瀑布里
        inset = Math.Max(0, data.Int("inset", 2));

        // 只阻止「开始攀爬」；本实体的判定体积铺在瀑布上时才生效
        Add(new ClimbBlocker(true));

        // 比 Player（Depth 0）先更新，保证本帧判定体积是最新的
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
            if (DynamicData.For(self).Get("LiftBoost") is Vector2 liftBoost)
                self.Speed += liftBoost;

            return Player.StNormal;
        }

        return orig(self);
    }

    private static bool PlayerInsideWaterfall(Player player)
    {
        // 直接扫房间实体：Tracker 对房间里没出现过的类型会抛异常
        foreach (Entity entity in player.Scene.Entities)
        {
            if (entity is RippleWaterfallNoGrabController controller && player.CollideCheck(controller))
                return true;
        }

        return false;
    }

    public override void Update()
    {
        base.Update();
        UpdateColliderIfChanged();

        // ClimbBlocker 只能阻止「开始抓」，已经抓着的玩家由这里踢回 StNormal，
        // 覆盖被移动实体带进瀑布的情况
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

        if (DynamicData.For(player).Get("LiftBoost") is Vector2 liftBoost)
            player.Speed += liftBoost;

        // 切回普通状态会走 ClimbEnd，抓取立即松开；这一帧的 ClimbBlocker 会阻止重新抓回去
        player.StateMachine.State = Player.StNormal;
    }

    private void UpdateColliderIfChanged()
    {
        scratch.Clear();

        foreach (Entity entity in Scene.Entities)
        {
            if (entity is not RippleWaterfall waterfall || !waterfall.Active)
                continue;

            // 瀑布占据 [X, X + Width) × [Y, Y + 绘制高度)，左右各收缩 inset 像素留出容差
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
