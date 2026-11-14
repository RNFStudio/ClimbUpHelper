using System.Collections;
using Celeste.Mod.Entities;
using ChroniaHelper;
using ChroniaHelper.Cores;
using ChroniaHelper.Utils;

namespace ChroniaHelper.Entities;

/// <summary>
/// 冲刺籽：沿用原版草莓籽的行为，但不会与草莓合并。
///
/// 拾取后跟随玩家，站在移动实体上会跟着一起移动；与原版草莓一致，籽只在玩家站在
/// 安全地面时才会掉落（墙面、天花板等其它表面不会），没有拾取保护或停留计时。
/// 籽自带一笔冲刺额度：玩家自己已经没有冲刺、却仍在按冲刺键时，由最先捡起的那颗籽
/// 替玩家发起一次冲刺，每发起一次扣一点额度，额度归零这颗籽掉落，由下一颗籽接上。
///
/// 面板填写 seedDashes 可调整每颗籽的额度；面板填写 index 指定编号（0-9）。
/// 编号画在籽的重生点上方、不跟随籽移动，场景里的编号与持有队列 HUD 都用这个值。
/// </summary>
[Tracked]
[CustomEntity("ChroniaHelper/DashSeed")]
[ExA.Credits("Inspiration and code provided by RogerNF")]
public class DashSeed : BaseEntity
{
    /// <summary>编号贴图相对籽中心的纵向偏移（16x16 的帧正好摆在籽的正上方）。</summary>
    private const float IndexOffsetY = -12f;

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

    /// <summary>上一帧玩家是否在水里，用于排除「从水里起跳」的向上速度判定。</summary>
    private bool wasInWater;

    /// <summary>面板指定的编号（0-9），用于相位错开、音效 count 参数与头顶编号。</summary>
    private int seedIndex;

    /// <summary>编号数字贴图：numbers.png 的 10 帧，按 1-9-0 的顺序排列。</summary>
    private MTexture[] indexDigits;

    /// <summary>钉在重生点上方的编号，不随籽移动。</summary>
    private SeedIndexLabel label;

    /// <summary>面板指定的每颗籽的冲刺额度。</summary>
    private int seedDashes = 1;

    /// <summary>这颗籽剩余的冲刺额度，耗尽即掉落。</summary>
    private int remainingDashes;

    /// <summary>掉落后回到原位前的固定延迟。</summary>
    private float returnTime;

    private int initDepth;
    private int depthOnTouch = -1000000;

    private string pulseSfx, touchSfx, poofSfx, appearSfx;
    private string particlePath;
    private CColor spriteColor, particleColor1, particleColor2;

    /// <summary>房间里有多颗籽时才显示头顶编号。</summary>
    private bool multipleSeeds;

    /// <summary>留在原位的虚线位置指示。</summary>
    private OriginMarker marker;
    private string outlineTexture;
    private int outlineDepth;

    public DashSeed(EntityData data, Vector2 offset)
        : base(data, offset)
    {
        start = Position;

        Depth = initDepth = data.Int("depth", -100);
        seedIndex = Math.Clamp(data.Int("index", 0), 0, 9);
        seedDashes = data.Int("seedDashes", 1);
        remainingDashes = seedDashes;
        outlineTexture = data.Attr("outlineTexture", "ClimbUpHelper/DashSeed/seed_outline");
        outlineDepth = data.Int("outlineDepth", 0);
        Collider = new Hitbox(12f, 12f, -6f, -6f); // wip: can customize in the future

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

        float bloomAlpha = data.Float("bloomAlpha", 1f);
        float bloomRadius = data.Float("bloomRadius", 12f);
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

    /// <summary>
    /// 该籽的编号：面板填写 index（0-9）的值，也是持有队列 HUD 显示的编号。
    /// </summary>
    public int Index => seedIndex;

    /// <summary>房间里是否有多颗籽；只有多颗时才出现在持有队列 HUD 里。</summary>
    public bool MultipleSeedsInRoom => multipleSeeds;

    /// <summary>籽的重生点世界坐标；贴在移动平台上时会跟着平台走。</summary>
    private Vector2 Origin => start + (attached?.Position ?? Vector2.Zero);

    public override void Added(Scene scene)
    {
        base.Added(scene);

        marker = new OriginMarker(GFX.Game[outlineTexture], start, outlineDepth);
        scene.Add(marker);

        indexDigits = LoadIndexDigits();

        if (indexDigits != null)
        {
            label = new SeedIndexLabel(indexDigits[FrameFor(seedIndex)], Origin + new Vector2(0f, IndexOffsetY), initDepth);
            scene.Add(label);
        }

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

        float phase = 1f - (float)seedIndex / (scene.Tracker.CountEntities<DashSeed>() + 1f);
        phase = 0.25f + phase * 0.75f;
        sprite.PlayOffset("idle", phase);

        // 播到第 19 帧时闪光、发粒子和音效
        sprite.OnFrameChange = _ =>
        {
            if (Visible && sprite.CurrentAnimationID == "idle" && sprite.CurrentAnimationFrame == 19)
            {
                Audio.Play(pulseSfx, Position, "count", (float)seedIndex);
                lightTween.Start();
                level.Displacement.AddBurst(Position, 0.6f, 8f, 20f, 0.2f);
            }
        };

        burstParticle = CreateBurstParticle(particlePath, particleColor1.Parsed(), particleColor2.Parsed());

        // 只数当前房间里的籽。切房间时 Level 会先加载新房间、过场结束才卸载旧房间，
        // 这时 Tracker 里还留着上一房间的籽；直接数会把单籽房间误判成多籽。
        multipleSeeds = CountSeedsInRoom(scene) > 1;
    }

    /// <summary>数当前房间里的籽：只统计重生点落在本房间范围内的那些。</summary>
    private int CountSeedsInRoom(Scene scene)
    {
        int count = 0;

        foreach (Entity entity in scene.Tracker.GetEntities<DashSeed>())
        {
            if (entity is DashSeed seed && level.IsInBounds(seed.Origin))
                count++;
        }

        return count;
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

        // 编号钉在重生点上方：位置跟重生点走，不跟籽走；深度固定为籽的初始深度
        if (label != null)
        {
            label.Position = Origin + new Vector2(0f, IndexOffsetY);
            label.SetShown(multipleSeeds);
        }

        sprite.Position = new Vector2(sine.Value * 2f, sine.ValueOverTwo) + shaker.Value;
    }

    /// <summary>numbers.png 的顺序是 1-9-0：数字 1 在第 0 帧，数字 0 在第 9 帧。</summary>
    private static int FrameFor(int index)
    {
        int digit = ((index % 10) + 10) % 10;
        return digit == 0 ? 9 : digit - 1;
    }

    /// <summary>把 numbers.png 切成 10 个数字帧；贴图缺失时返回 null，编号不显示。</summary>
    private static MTexture[] LoadIndexDigits()
    {
        const string path = "ClimbUpHelper/DashSeed/numbers";

        if (!GFX.Game.Has(path))
            return null;

        MTexture sheet = GFX.Game[path];
        int frameWidth = sheet.Width / 10;

        MTexture[] digits = new MTexture[10];

        for (int i = 0; i < digits.Length; i++)
        {
            digits[i] = sheet.GetSubtexture(i * frameWidth, 0, frameWidth, sheet.Height);
        }

        return digits;
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
        // 只接管理论上能起冲的状态（Normal / Climb / 红冲 / 受击 / 发射 / 星飞）。
        // 冲刺中、梦冲、飞行器、过场等状态不接管：否则会在一个本来不允许冲刺的状态里
        // 硬插一次冲刺（例如冲刺中途再甩一次、打断梦冲）。
        int state = player.StateMachine.State;

        if (state is not (Player.StNormal
            or Player.StClimb
            or Player.StRedDash
            or Player.StHitSquash
            or Player.StLaunch
            or Player.StStarFly))
        {
            return false;
        }

        // 水里一律用玩家自己的冲刺（水中冲刺不消耗冲刺、会持续回冲），籽不介入；
        // 只看状态不够——水面 / 浅水时状态可能还是 StNormal。
        if (player.CollideCheck<Water>())
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
        Audio.Play(touchSfx, Position, "count", (float)seedIndex);
        this.player = player;
        player.Leader.GainFollower(follower);
        Collidable = false;
        Depth = depthOnTouch;
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

        Audio.Play(appearSfx, Position, "count", (float)seedIndex);

        Position = start;
        if (attached != null)
            Position += attached.Position;

        shaker.ShakeFor(0.4f, false);
        sprite.Scale = Vector2.One;
        Visible = true;
        Collidable = true;
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

    /// <summary>画在重生点上方的编号贴图，不随籽移动，籽消失飞回期间也保持显示。</summary>
    private sealed class SeedIndexLabel : Entity
    {
        /// <summary>上下浮动的幅度（世界像素）。</summary>
        private const float BobAmplitude = 2.0f;

        /// <summary>上下浮动的角速度（弧度/秒）。</summary>
        private const float BobSpeed = 2.0f;

        /// <summary>淡入所需的时间。</summary>
        private const float FadeInTime = 0.4f;

        private readonly MTexture texture;

        private float timer;

        /// <summary>当前不透明度，0~1。</summary>
        private float alpha;

        /// <summary>目标状态，由所在籽每帧设置。</summary>
        private bool shown;

        public SeedIndexLabel(MTexture texture, Vector2 position, int depth)
            : base(position)
        {
            this.texture = texture;
            Depth = depth;
            Visible = false;
            Collidable = false;

            // 用位置生成相位，避免所有籽的编号同步上下浮动
            timer = (position.X + position.Y) * 0.05f;
        }

        /// <summary>是否应该显示；从不可见变为可见时会淡入。</summary>
        public void SetShown(bool value) => shown = value;

        public override void Update()
        {
            base.Update();
            timer += Engine.DeltaTime;

            // 切房间的过场结束时新的籽已经加进来了；等过场结束再开始淡入，
            // 否则淡入会被转场画面盖住，看起来还是直接出现。
            if (Scene is Level level && level.Transitioning)
                return;

            alpha = Calc.Approach(alpha, shown ? 1f : 0f, Engine.DeltaTime / FadeInTime);
            Visible = alpha > 0f;
        }

        public override void Render()
        {
            base.Render();

            if (alpha <= 0f)
                return;

            float bob = MathF.Sin(timer * BobSpeed) * BobAmplitude;
            texture.DrawCentered(Position + new Vector2(0f, bob), Color.White * alpha);
        }
    }

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
