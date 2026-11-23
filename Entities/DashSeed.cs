using System.Collections;
using Celeste.Mod.Entities;
using ChroniaHelper;
using ChroniaHelper.Cores;
using ChroniaHelper.Utils;

namespace ChroniaHelper.Entities;

/// <summary>
/// 冲刺籽：行为沿用原版草莓籽，但不会与草莓合并，也没有拾取保护和停留计时。
/// 拾取后跟随玩家，站在移动实体上会跟着走；只在玩家站上安全地面时掉落。
///
/// 自带一笔冲刺额度：玩家没有冲刺却仍按下冲刺键时，由最先捡起的籽替玩家起冲，
/// 每冲一次扣一点，额度归零这颗籽掉落、由下一颗接上。
///
/// 面板属性：seedDashes 为每颗籽的额度，petals 为花瓣数量（重生点的花瓣环与持有队列 HUD 都显示它）。
/// </summary>
[Tracked]
[CustomEntity("ChroniaHelper/DashSeed")]
[ExA.Credits("Inspiration and code provided by RogerNF")]
public class DashSeed : BaseEntity
{
    private Sprite sprite;
    private Follower follower;
    private Wiggler wiggler;
    private SineWave sine;
    private Shaker shaker;
    private VertexLight light;
    private Tween lightTween;
    private ParticleType burstParticle;
    private Platform attached;

    private Player player;
    private Vector2 start;

    // 重生点世界坐标；贴在移动平台上时会跟着平台走
    private Vector2 Origin => start + (attached?.Position ?? Vector2.Zero);

    // 上一帧玩家是否在水里，用于排除「从水里起跳」
    private bool wasInWater;

    // 面板指定的花瓣数量（0 = 不显示花瓣环与 HUD 数字）
    private int seedPetals;

    // 重生点处的花瓣环，显示花瓣数量
    private PetalRing petalRing;

    // 面板指定的每颗籽的额度 / 这颗籽剩余的额度（耗尽即掉落）
    private int seedDashes = 1;
    private int remainingDashes;

    // 掉落后回到原位前的固定延迟
    private float returnTime;

    private int initDepth;
    private int depthOnTouch = -1000000;

    // 音效变体：生成时随机取 0~3，和花瓣数量无关
    private int sfxVariant;

    private string pulseSfx, touchSfx, poofSfx, appearSfx;
    private string particlePath;
    private CColor spriteColor, particleColor1, particleColor2;

    private float bloomAlpha;
    private float bloomRadius;

    // 留在原位的虚线位置指示
    private OriginMarker marker;
    private string outlineTexture;
    private int outlineDepth;

    public DashSeed(EntityData data, Vector2 offset)
        : base(data, offset)
    {
        start = Position;

        Depth = initDepth = data.Int("depth", -100);
        seedPetals = Math.Max(0, data.Int("petals", 0));
        sfxVariant = Calc.Random.Next(4);
        seedDashes = data.Int("seedDashes", 1);
        remainingDashes = seedDashes;
        outlineTexture = data.Attr("outlineTexture", "ClimbUpHelper/DashSeed/seed_outline");
        outlineDepth = data.Int("outlineDepth", 0);
        Collider = new Hitbox(12f, 12f, -6f, -6f); // TODO: 后续可开放为面板属性

        Add(follower = new Follower(OnGainLeader, OnLoseLeader));
        follower.FollowDelay = 0.2f;

        // 切房间时籽不保留，让 HUD 播放格子收起的动画
        follower.PersistentFollow = false;

        Add(new StaticMover
        {
            SolidChecker = solid => solid.CollideCheck(this),
            OnAttach = platform =>
            {
                Depth = initDepth;
                Collider = new Hitbox(24f, 24f, -12f, -12f);
                attached = platform;
                start = Position - platform.Position;
            }
        });

        Add(new PlayerCollider(OnPlayer));

        // 保留开放属性但暂时不开放
        float wiggleDuration = data.Float("wiggleDuration", 0.5f).ClampMin(Engine.DeltaTime / 2f);
        float wiggleFrequency = data.Float("wiggleFrequency", 4f);

        Add(wiggler = Wiggler.Create(wiggleDuration, wiggleFrequency, v =>
        {
            if (sprite != null)
                sprite.Scale = Vector2.One * (1f + 0.2f * v);
        }));

        // 保留开放属性但暂时不开放
        float waveFrequency = data.Float("waveFrequency", 0.5f);
        float waveOffset = data.Float("waveOffset", 0f);
        Add(sine = new SineWave(waveFrequency, waveOffset).Randomize());
        Add(shaker = new Shaker(false));

        bloomAlpha = data.Float("bloomAlpha", 1f);
        bloomRadius = data.Float("bloomRadius", 12f);
        Add(new BloomPoint(bloomAlpha, bloomRadius));

        CColor lightColor = data.PackColor("lightColor", CColor.White);
        int startFade = data.Int("lightStartFade", 16);
        int endFade = data.Int("lightEndFade", 24);
        Add(light = new VertexLight(lightColor.color, lightColor.alpha, startFade, endFade));
        Add(lightTween = light.CreatePulseTween());

        sprite = GFX.SpriteBank.Create(data.Attr("sprite", "ghostberrySeed"));
        sprite.Position = new Vector2(sine.Value * 2f, sine.ValueOverTwo);
        spriteColor = data.PackColor("spriteColor", new CColor(Color.White, 0.8f));
        sprite.Color = spriteColor.Parsed();
        Add(sprite);

        pulseSfx = data.Attr("pulseSfx", "event:/game/general/seed_pulse");
        touchSfx = data.Attr("touchSfx", "event:/game/general/seed_touch");
        poofSfx = data.Attr("poofSfx", "event:/game/general/seed_poof");
        appearSfx = data.Attr("appearSfx", "event:/game/general/seed_reappear");

        particlePath = data.Attr("particleSprite", "particles/shatter");
        particleColor1 = data.PackColor("particleColor1", CColor.White);
        particleColor2 = data.PackColor("particleColor2", new CColor(Color.Gray));

        returnTime = data.Float("returnTime", 0.3f).ClampMin(0f);
    }

    /// <summary>面板填写的花瓣数量，也是持有队列 HUD 显示的数字。</summary>
    public int Petals => seedPetals;

    public override void Added(Scene scene)
    {
        base.Added(scene);

        marker = new OriginMarker(GFX.Game[outlineTexture], start, outlineDepth);
        scene.Add(marker);

        // 重生点处用花瓣环显示花瓣数量；透明度、bloom 跟籽本体一致
        // 运行时构造的环 trigger_range 固定为 0，不会变成触发式绽放
        petalRing = new PetalRing(Origin, seedPetals, initDepth, spriteColor.alpha, bloomAlpha, bloomRadius);
        scene.Add(petalRing);

        // 房间里有籽就有 HUD：由本房间的第一颗籽创建；HUD 是 Global，切房间、死亡重载都不会被卸载
        if (IsFirstSeedInRoom(scene) && !DashSeedInventoryHUD.ExistsIn(scene))
            scene.Add(new DashSeedInventoryHUD());
    }

    /// <summary>本籽是否是当前房间里第一颗被加入场景的籽。</summary>
    private bool IsFirstSeedInRoom(Scene scene)
    {
        if (scene is not Level level)
            return true;

        foreach (Entity entity in scene.Entities)
        {
            if (entity is DashSeed other && other != this && level.IsInBounds(other.Origin))
                return false;
        }

        return true;
    }

    public override void Awake(Scene scene)
    {
        base.Awake(scene);
        level = SceneAs<Level>();

        float phase = 1f - (float)seedPetals / (scene.Tracker.CountEntities<DashSeed>() + 1f);
        phase = Calc.Clamp(0.25f + phase * 0.75f, 0f, 1f);
        sprite.PlayOffset("idle", phase);

        // 播到第 19 帧时闪光、发粒子和音效
        sprite.OnFrameChange = _ =>
        {
            if (Visible && sprite.CurrentAnimationID == "idle" && sprite.CurrentAnimationFrame == 19)
            {
                Audio.Play(pulseSfx, Position, "count", (float)sfxVariant);
                lightTween.Start();
                level.Displacement.AddBurst(Position, 0.6f, 8f, 20f, 0.2f);
            }
        };

        burstParticle = CreateBurstParticle(particlePath, particleColor1.Parsed(), particleColor2.Parsed());
    }

    public override void Update()
    {
        base.Update();

        UpdateLosing();
        UpdateDash();

        // 籽被带走后，原位留下一圈位置指示
        if (marker != null)
        {
            marker.Position = Origin;
            marker.Visible = follower.HasLeader || !Visible;
        }

        // 花瓣环钉在重生点：位置跟重生点走，不跟籽走
        if (petalRing != null)
            petalRing.Position = Origin;

        sprite.Position = new Vector2(sine.Value * 2f, sine.ValueOverTwo) + shaker.Value;
    }

    private void UpdateLosing()
    {
        if (!follower.HasLeader || player == null)
            return;

        bool inWater = player.CollideCheck<Water>();

        // 与原版草莓的收集条件一致：OnSafeGround（安全地面）+ OnGround（脚下真的有 Solid / JumpThru）。
        // 水虽然也是 safe ground，但不算地面，所以游泳、水中冲刺这些帧 OnGround 为 false，籽不会误掉。
        bool onSafeGround = player.OnSafeGround && player.OnGround();

        // 原版草莓籽在玩家「接触过安全地面后起跳」的向上速度时也会掉。
        // OnSafeGround 是 Player.Update 开头的快照，起跳那一帧它仍然是 true，正好能抓到这一帧；
        // OnSafeGround 为 false 时（非 safe ground）这条不生效；水里用当前/上一帧的水体碰撞排除，
        // 避免游泳上浮或从水里起跳时误掉。
        bool jumpedFromSafeGround = player.Speed.Y < 0f
            && player.OnSafeGround
            && !inWater
            && !wasInWater;

        if (onSafeGround || jumpedFromSafeGround)
            player.Leader.LoseFollower(follower);

        wasInWater = inWater;
    }

    private void UpdateDash()
    {
        if (follower.HasLeader && IsOldestHeld())
        {
            TrySeedDash();
        }
    }

    /// <summary>玩家没有冲刺却仍在按冲刺键时，消耗自己一点额度替玩家发起冲刺。</summary>
    private void TrySeedDash()
    {
        if (remainingDashes <= 0 || player.Dashes > 0 || !CanSeedDash())
        {
            return;
        }

        remainingDashes--;

        // 补一格冲刺给引擎扣，再走原版冲刺流程（方向由引擎按当前瞄准决定）
        player.Dashes = 1;
        player.StartDash();
        player.StateMachine.State = 2;

        if (remainingDashes <= 0)
        {
            player.Leader.LoseFollower(follower);
        }
    }

    /// <summary>是否到了可以替玩家发起冲刺的时机。</summary>
    private bool CanSeedDash()
    {
        bool inWater = player.CollideCheck<Water>();

        // 只接管理论上能起冲的状态（Normal / Climb / Swim / 红冲 / 受击 / 发射 / 星飞）；
        // 冲刺中、梦冲、飞行器、过场不接管，避免在这些状态里硬插一次冲刺
        int state = player.StateMachine.State;

        if (state is not (Player.StNormal
            or Player.StClimb
            or Player.StSwim
            or Player.StRedDash
            or Player.StHitSquash
            or Player.StLaunch
            or Player.StStarFly))
        {
            return false;
        }

        // 水里如果玩家自己还有冲刺（Dashes > 0），优先用水中的冲刺，籽不介入；
        // 但 Dashes == 0 时（例如序章 inventory）玩家无法冲刺，允许消耗籽发起冲刺。
        if (inWater && player.Dashes > 0)
        {
            return false;
        }

        return (Input.CrouchDashPressed || Input.DashPressed)
            && player.dashCooldownTimer <= 0f
            && !player.StateMachine.Locked
            && (TalkComponent.PlayerOver == null || !Input.Talk.Pressed);
    }

    /// <summary>该籽是否是玩家当前「最先捡起」的那一颗。</summary>
    private bool IsOldestHeld()
    {
        if (player?.Leader?.Followers == null)
            return false;

        foreach (Follower other in player.Leader.Followers)
        {
            if (other.Entity is DashSeed)
                return other == follower;
        }

        return false;
    }

    private void OnPlayer(Player player)
    {
        Audio.Play(touchSfx, Position, "count", (float)sfxVariant);
        this.player = player;
        player.Leader.GainFollower(follower);
        Collidable = false;
        Depth = depthOnTouch;

        // 被拾取时花瓣环缩小、变淡
        petalRing?.PlayPickupAnimation();
    }

    private void OnGainLeader()
    {
        wiggler.Start();
        remainingDashes = seedDashes;
    }

    private void OnLoseLeader()
    {
        Add(new Coroutine(ReturnRoutine()));
    }

    /// <summary>掉落后飞回初始位置复原。</summary>
    private IEnumerator ReturnRoutine()
    {
        Audio.Play(poofSfx, Position);
        Collidable = false;
        sprite.Scale = Vector2.One * 2f;
        yield return 0.05f;
        Input.Rumble(RumbleStrength.Medium, RumbleLength.Medium);

        for (int i = 0; i < 6; i++)
        {
            float angle = Calc.Random.NextFloat(MathHelper.TwoPi);
            level.ParticlesFG.Emit(burstParticle, 1, Position + Calc.AngleToVector(angle, 4f), Vector2.Zero, angle);
        }

        Visible = false;
        Depth = initDepth;

        yield return returnTime;

        Audio.Play(appearSfx, Position, "count", (float)sfxVariant);

        Position = start;
        if (attached != null)
            Position += attached.Position;

        shaker.ShakeFor(0.4f, false);
        sprite.Scale = Vector2.One;
        Visible = true;
        Collidable = true;
        petalRing?.ResetPickupAnimation();
        level.Displacement.AddBurst(Position, 0.2f, 8f, 28f, 0.2f);
    }

    private ParticleType CreateBurstParticle(string path, Color color1, Color color2) => new ParticleType
    {
        Source = GFX.Game[path],
        Color = color1,
        Color2 = color2,
        ColorMode = ParticleType.ColorModes.Fade,
        LifeMin = 0.3f,
        LifeMax = 0.4f,
        Size = 0.8f,
        SizeRange = 0.3f,
        ScaleOut = true,
        Direction = 0f,
        DirectionRange = 0f,
        SpeedMin = 100f,
        SpeedMax = 140f,
        SpeedMultiplier = 1E-05f,
        RotationMode = ParticleType.RotationModes.SameAsDirection
    };

    /// <summary>留在原位的虚线位置指示，籽被带走时显示。</summary>
    private sealed class OriginMarker : Entity
    {
        private readonly Image image;

        private float timer;

        public OriginMarker(MTexture texture, Vector2 position, int depth)
            : base(position)
        {
            Depth = depth;
            Visible = false;
            Collidable = false;

            Add(image = new Image(texture));
            image.CenterOrigin();
        }

        public override void Update()
        {
            base.Update();

            timer += Engine.DeltaTime;
            image.Color = Color.White * (0.55f + 0.2f * (float)Math.Sin(timer * 3f));
        }
    }
}
