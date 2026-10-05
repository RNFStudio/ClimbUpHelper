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
/// 一个"无效果的草莓籽"，统一使用 ghost（蓝色残影）外观：
///   玩家碰到即可拾取，拾取后跟随玩家（seed_touch / 脉冲等原版表现）
///   站在会移动的实体上时会跟着一起移动
///   永远不会拼合成草莓，也没有任何收集 / 计数 / 保存效果
///
/// 冲刺机制：在"背包本身不给冲刺"的关卡里（例如 Prologue inventory，MaxDashes == 0），
/// 玩家每持有一颗籽就有一颗冲刺；冲刺会消耗"最先捡起"的那颗籽。
///
/// 严格落地回收：只要玩家脚一沾地，持有的籽立刻飞回原位，没有任何容错窗口
/// （原版的"落地 0.15 秒后才回收"和"捡起后 0.25 秒内不会掉"都已删除）。
/// 因此籽只能滞空持有 —— 落地就等于清空冲刺。
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

    private int seedIndex;

    private float returnTimeMin, returnTimeMax;

    /// <summary>被消耗时补予玩家的冲刺数。</summary>
    private int seedDashes = 1;

    private int depthOnTouch = -1000000;

    private int initDepth;

    public DashSeed(EntityData data, Vector2 offset)
        : base(data, offset)
    {
        start = Position;

        Depth = initDepth = data.Int("depth", -100);
        seedDashes = data.Int("seedDashes", 1);
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

        CColor lightColor = data.GetChroniaColor("lightColor", CColor.White);
        int startFade = data.Int("lightStartFade", 16);
        int endFade = data.Int("lightEndFade", 24);
        Add(light = new VertexLight(lightColor.color, lightColor.alpha, startFade, endFade));
        Add(lightTween = light.CreatePulseTween());

        sprite = GFX.SpriteBank.Create(data.Attr("sprite", "ghostberrySeed"));
        sprite.Position = new Vector2(sine.Value * 2f, sine.ValueOverTwo);
        spriteColor = data.GetChroniaColor("spriteColor", new CColor(Color.White, 0.8f));
        sprite.Color = spriteColor.Parsed();
        Add(sprite);

        pulseSfx = data.Attr("pulseSfx", "event:/game/general/seed_pulse");
        touchSfx = data.Attr("touchSfx", "event:/game/general/seed_touch");
        poofSfx = data.Attr("poofSfx", "event:/game/general/seed_poof");
        appearSfx = data.Attr("appearSfx", "event:/game/general/seed_reappear");

        particlePath = data.Attr("particleSprite", "particles/shatter");
        particleColor1 = data.GetChroniaColor("particleColor1", CColor.White);
        particleColor2 = data.GetChroniaColor("particleColor2", new CColor(Color.Gray));

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

        // 只有「最先捡起」的那颗籽负责冲刺检测与回收，其余籽跳过以免重复触发
        if (follower.HasLeader && IsOldestHeld())
        {
            // 上一颗籽的份额耗尽后，由当前这颗顶替成为新的待消耗籽
            if (Md.Session.DashSeedDashes <= 0)
            {
                Md.Session.DashSeedDashes = seedDashes;
                player.Dashes += seedDashes;
            }

            // 玩家刚冲刺过：份额减一，减到 0 时这颗籽被消耗
            bool condition1 = Md.Session.DashSeedLastDashes > player.Dashes && player.Dashes == 0;
            bool condition2 = player.CollideCheck<Water>() && (player.dashAttackTimer > 0 || player.StateMachine.State == 2);
            if (condition1 || condition2)
            {
                Md.Session.DashSeedDashes--;

                // 手动代替冲刺减1
                if (condition2)
                {
                    player.Dashes--;
                }

                if (Md.Session.DashSeedDashes <= 0)
                {
                    Md.Session.DashSeedDashes = 0;
                    player.Leader.LoseFollower(follower);
                    Md.Session.DashSeedLastDashes = player.Dashes;
                    return;
                }
            }

            // 严格落地回收：脚一沾地，籽的份额作废、籽立刻飞回原位
            if (player.LoseShards)
            {
                player.Dashes = Math.Max(0, player.Dashes - Md.Session.DashSeedDashes);
                Md.Session.DashSeedDashes = 0;
                player.Leader.LoseFollower(follower);
                Md.Session.DashSeedLastDashes = player.Dashes;
                return;
            }

            Md.Session.DashSeedLastDashes = player.Dashes;
        }

        sprite.Position = new Vector2(sine.Value * 2f, sine.ValueOverTwo) + shaker.Value;
    }

    /// <summary>该籽是否是玩家当前"最先捡起"的那一颗。</summary>
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

        // 本实体不检查是否集齐
    }

    private void OnGainLeader()
    {
        wiggler.Start();
    }

    private void OnLoseLeader()
    {
        Add(new Coroutine(ReturnRoutine()));
    }

    /// <summary>
    /// 丢失 / 被冲刺消耗后飞回初始位置复原（沿用原版时间）
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
