using System.Collections;
using System.Data;
using Celeste.Mod.Entities;
using ChroniaHelper;
using ChroniaHelper.Cores;
using ChroniaHelper.Utils;

namespace ChroniaHelper.Entities;

/// <summary>
/// 冲刺籽（DashSeed）。
///
/// 沿用原版草莓籽（StrawberrySeed）的完整行为，但不与草莓绑定：
///   玩家碰到即拾取并跟随玩家（seed_touch / 脉冲等原版表现）
///   站在会移动的实体上时会跟着一起移动
///   没有「集齐所有籽拼成草莓」这一流程，任何时候都不会被合并、也不会被永久收集
///
/// 冲刺规则：
///   每颗籽自带一笔冲刺额度（面板 seedDashes，默认 1）
///   玩家自身冲刺为 0、却仍在按冲刺键时，由「最先捡起」的那颗籽替玩家发起冲刺
///   每发起一次扣掉自己 1 点额度；额度归零这颗籽即飞回原位，由下一颗籽接上
///
/// 掉落规则与原版一致：
///   拾取后 0.25 秒内不会掉落
///   之后落地停留 0.15 秒（或离地向上移动）即掉落
///   落地期间离地则重新计时、取消本次掉落
///   掉落后播放 poof 效果并飞回初始位置重新出现
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

    private int seedIndex;

    private float canLoseTimer;
    private float loseTimer;
    private bool losing;

    private float returnTimeMin, returnTimeMax;

    /// <summary>被消耗时补予玩家的冲刺数（后续冲刺逻辑使用）。</summary>
    private int seedDashes = 1;

    /// <summary>这颗籽剩余的冲刺额度，耗尽即被消耗。</summary>
    private int remainingDashes;

    private int depthOnTouch = -1000000;

    private int initDepth;

    public DashSeed(EntityData data, Vector2 offset)
        : base(data, offset)
    {
        start = Position;

        Depth = initDepth = data.Int("depth", -100);
        seedDashes = data.Int("seedDashes", 1);
        remainingDashes = seedDashes;
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
    private string pulseSfx, touchSfx, poofSfx, appearSfx;
    private CColor spriteColor, particleColor1, particleColor2;
    private string particlePath;

    public override void Added(Scene scene)
    {
        base.Added(scene);

        // 草莓籽follower序号设置
        seedIndex = scene.Tracker.CountEntities<DashSeed>() - 1;
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
    }

    public override void Update()
    {
        base.Update();

        // 拾取后先等保护时间过去，之后才允许因落地掉落
        if (canLoseTimer > 0f)
        {
            canLoseTimer -= Engine.DeltaTime;
        }
        else if (follower.HasLeader && player.LoseShards)
        {
            losing = true;
        }

        // 落地掉落：连续在地累计到 LoseGraceTime，或离地向上移动时掉落
        if (losing)
        {
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

        // 冲刺：玩家自己没有冲刺、却仍在尝试冲刺时，由这颗籽替玩家发起一次
        if (follower.HasLeader && IsOldestHeld())
        {
            TrySeedDash();
        }

        sprite.Position = new Vector2(sine.Value * 2f, sine.ValueOverTwo) + shaker.Value;
    }

    /// <summary>玩家没有冲刺却仍在按冲刺键时，消耗自己一点额度替玩家发起冲刺。</summary>
    private void TrySeedDash()
    {
        if (remainingDashes <= 0 || player.Dashes > 0 || !CanSeedDash())
        {
            return;
        }

        remainingDashes--;

        // 补一格冲刺给引擎扣，再走原版冲刺流程（冲刺方向由引擎按当前瞄准决定）
        player.Dashes = 1;
        player.StartDash();
        player.StateMachine.State = 2;

        if (remainingDashes <= 0)
        {
            player.Leader.LoseFollower(follower);
        }
    }

    /// <summary>该籽是否可以替玩家发起冲刺（对齐原版允许冲刺的时机）。</summary>
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

    /// <summary>
    /// 掉落后飞回初始位置复原
    /// </summary>
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
}
