using System.Collections;
using Celeste.Mod.Entities;
using ChroniaHelper;
using ChroniaHelper.Cores;
using ChroniaHelper.Utils;

namespace ChroniaHelper.Entities;

/// <summary>
/// 冲刺籽：沿用原版草莓籽的行为，但不会与草莓合并。
///
/// 拾取后跟随玩家，站在移动实体上会跟着一起移动，落地按原版时序掉落并飞回原位。
/// 籽自带一笔冲刺额度：玩家自己已经没有冲刺、却仍在按冲刺键时，由最先捡起的那颗籽
/// 替玩家发起一次冲刺，每发起一次扣一点额度，额度归零这颗籽掉落，由下一颗籽接上。
///
/// 面板填写 seedDashes 可调整每颗籽的额度；序号按房间内的摆放顺序从 0 开始自动生成，
/// 头顶编号与持有队列 HUD 都用这个序号。
/// </summary>
[Tracked]
[CustomEntity("ChroniaHelper/DashSeed")]
[ExA.Credits("Inspiration and code provided by RogerNF")]
public class DashSeed : BaseEntity
{
    /// <summary>拾取后不会掉落的时间。</summary>
    private const float LoseDelay = 0.25f;

    /// <summary>落地后掉落所需的停留时间。</summary>
    private const float LoseGraceTime = 0.15f;

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

    /// <summary>房间内的序号，用于相位错开与音效 count 参数。</summary>
    private int seedIndex;

    /// <summary>面板指定的每颗籽的冲刺额度。</summary>
    private int seedDashes = 1;

    /// <summary>这颗籽剩余的冲刺额度，耗尽即掉落。</summary>
    private int remainingDashes;

    private float canLoseTimer;
    private float loseTimer;
    private bool losing;

    private float returnTimeMin, returnTimeMax;

    private int initDepth;
    private int depthOnTouch = -1000000;

    private string pulseSfx, touchSfx, poofSfx, appearSfx;
    private string particlePath;
    private CColor spriteColor, particleColor1, particleColor2;

    /// <summary>房间内有多颗籽时才显示头顶编号。</summary>
    private bool multipleSeeds;

    /// <summary>留在原位的虚线位置指示。</summary>
    private OriginMarker marker;
    private string outlineTexture;
    private int outlineDepth;

    /// <summary>头顶编号标签（HUD 层）。</summary>
    private IndexLabel label;

    /// <summary>房间内的持有队列 HUD，用来读取头顶编号的显隐设置。</summary>
    private DashSeedInventoryHUD hud;

    /// <summary>是否已经确认过房间里的持有队列 HUD。</summary>
    private bool hudEnsured;

    public DashSeed(EntityData data, Vector2 offset)
        : base(data, offset)
    {
        start = Position;

        Depth = initDepth = data.Int("depth", -100);
        seedDashes = data.Int("seedDashes", 1);
        remainingDashes = seedDashes;
        outlineTexture = data.Attr("outlineTexture", "ClimbUpHelper/DashSeed/seed_outline");
        outlineDepth = data.Int("outlineDepth", 0);
        Collider = new Hitbox(12f, 12f, -6f, -6f); // wip: can customize in the future

        Add(follower = new Follower(OnGainLeader, OnLoseLeader));
        follower.FollowDelay = 0.2f;
        follower.PersistentFollow = data.Bool("persistent", false);

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

        returnTimeMax = data.Float("returnTimeMax", 0.4f);
        returnTimeMin = data.Float("returnTimeMin", 0.2f);
    }

    /// <summary>该籽的序号：按房间内的摆放顺序从 0 开始，供头顶编号与持有队列 HUD 使用。</summary>
    public int Index => seedIndex;

    public override void Added(Scene scene)
    {
        base.Added(scene);

        seedIndex = scene.Tracker.CountEntities<DashSeed>() - 1;

        marker = new OriginMarker(GFX.Game[outlineTexture], start, outlineDepth);
        scene.Add(marker);

        // 编号画在 HUD 画布上，避免在 Gameplay 画布上被放大发糊
        label = new IndexLabel(Index);
        scene.Add(label);
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

        multipleSeeds = scene.Tracker.CountEntities<DashSeed>() > 1;
    }

    public override void Update()
    {
        base.Update();

        // 房间里没有手动放置的持有队列 HUD 时补一个默认的
        if (!hudEnsured)
        {
            hudEnsured = true;
            DashSeedInventoryHUD.EnsureIn(Scene);
        }

        UpdateLosing();
        UpdateDash();

        // 籽被带走后，原位留下一圈位置指示
        if (marker != null)
        {
            marker.Position = start + (attached?.Position ?? Vector2.Zero);
            marker.Visible = follower.HasLeader || !Visible;
        }

        if (hud == null)
            hud = Scene?.Tracker?.GetEntity<DashSeedInventoryHUD>();

        // 头顶编号：世界坐标换算成 HUD 画布坐标，显隐由持有队列 HUD 的设置决定
        if (label != null)
        {
            label.Visible = Visible && multipleSeeds && (hud?.ShowSeedIndex ?? true);

            if (label.Visible)
            {
                label.ScreenPosition = level.WorldToScreen(Position + new Vector2(0f, -4f));
            }
        }

        sprite.Position = new Vector2(sine.Value * 2f, sine.ValueOverTwo) + shaker.Value;
    }

    private void UpdateLosing()
    {
        // 拾取后先等保护时间过去，之后才允许因落地掉落
        if (canLoseTimer > 0f)
        {
            canLoseTimer -= Engine.DeltaTime;
        }
        else if (follower.HasLeader && player.LoseShards)
        {
            losing = true;
        }

        // 连续在地累计到 LoseGraceTime，或离地向上移动时掉落
        if (!losing)
            return;

        if (loseTimer <= 0f || player.Speed.Y < 0f)
        {
            player.Leader.LoseFollower(follower);
            losing = false;
        }
        else if (player.LoseShards)
        {
            loseTimer -= Engine.DeltaTime;
        }
        else
        {
            loseTimer = LoseGraceTime;
            losing = false;
        }
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
        // 只在原版会判定 CanDash 的状态里接管，冲刺中 / 飞行器 / 梦冲等状态不介入
        int state = player.StateMachine.State;

        if (state != 0 && state != 1 && state != 3 && state != 5 && state != 6 && state != 7 && state != 19)
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
        canLoseTimer = LoseDelay;
        loseTimer = LoseGraceTime;
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

        yield return RandomUtils.RandomFloat(returnTimeMin, returnTimeMax, seedIndex).ClampMin(0);

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

    /// <summary>跑在 HUD 画布上的编号标签，位置由所在籽的世界坐标换算而来。</summary>
    private sealed class IndexLabel : Entity
    {
        /// <summary>数字在世界里的大致高度，换算到 HUD 画布时乘 6。</summary>
        private const float WorldHeight = 10f;

        private readonly string text;

        public Vector2 ScreenPosition;

        public IndexLabel(int index)
        {
            text = index.ToString();
            Tag = Tags.HUD;
            Depth = -200;
            Visible = false;
        }

        public override void Render()
        {
            base.Render();

            Vector2 size = ActiveFont.Measure(text);
            float scale = size.Y > 0f ? WorldHeight * 6f / size.Y : 1f;

            ActiveFont.DrawOutline(text, ScreenPosition, new Vector2(0.5f, 1f), Vector2.One * scale, Color.White, 2f, Color.Black);
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
