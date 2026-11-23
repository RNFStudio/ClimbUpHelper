using Celeste.Mod.Entities;

namespace ChroniaHelper.Entities;

/// <summary>
/// 装饰用花瓣环：一圈白色卵形花瓣，贴图方向朝上，可整体旋转并带 bloom。
/// 面板属性：radius / petal_size / petal_count / rotation_speed / bloom_alpha / alpha / depth。
/// PetalScale 可在运行时整体缩放花瓣。
/// </summary>
[Tracked]
[CustomEntity("ChroniaHelper/PetalRing")]
public sealed class PetalRing : Entity
{
    private static MTexture petalTexture;

    // 拾取 / 恢复动画的时长
    private const float AnimationDuration = 0.5f;

    // 拾取 / 恢复时临时增加的转速（度/秒）及其衰减时间
    private const float SpinBoostAmount = 180f;
    private const float SpinBoostDuration = 0.5f;

    private readonly float baseRadius;
    private readonly float baseAlpha;
    private readonly float petalSize;
    private readonly int petalCount;
    private readonly float rotationSpeed;
    private readonly float bloomAlpha;

    private float radius;
    private float alpha;
    private float rotation;

    private bool animating;
    private float animTime;
    private float animDuration;
    private float animRadiusFrom;
    private float animRadiusTo;
    private float animAlphaFrom;
    private float animAlphaTo;

    private float spinBoost;
    private float spinBoostTime;

    public PetalRing(EntityData data, Vector2 offset)
        : base(data.Position + offset)
    {
        baseRadius = Math.Max(0f, data.Float("radius", 8f));
        petalSize = Math.Max(0f, data.Float("petal_size", 6.4f));
        petalCount = Math.Max(0, data.Int("petal_count", 8));
        rotationSpeed = data.Float("rotation_speed", 30f);
        bloomAlpha = Calc.Clamp(data.Float("bloom_alpha", 1f), 0f, 1f);
        alpha = Calc.Clamp(data.Float("alpha", 1f), 0f, 1f);
        baseAlpha = alpha;
        Depth = data.Int("depth", -50000);
        radius = baseRadius;

        Add(new BloomPoint(bloomAlpha, baseRadius + petalSize * 1.5f));
        int litRadius = (int)(baseRadius + petalSize * 1.5f);
        Add(new VertexLight(Color.White, 1f, litRadius, litRadius + 8));

        rotation = InitialRotation(data.Position);
        Collidable = false;
    }

    /// <summary>供 DashSeed 等代码在运行时创建花瓣环；alpha/bloom 通常沿用籽本体的参数。</summary>
    public PetalRing(Vector2 position, int petals, int depth, float alpha, float bloomAlpha, float bloomRadius)
        : base(position)
    {
        baseRadius = 8f;
        petalSize = 0.8f * 8f;
        petalCount = Math.Max(0, petals);
        rotationSpeed = 30f;
        this.bloomAlpha = Calc.Clamp(bloomAlpha, 0f, 1f);
        baseAlpha = Calc.Clamp(alpha, 0f, 1f);
        this.alpha = baseAlpha;
        Depth = depth;
        radius = baseRadius;

        Add(new BloomPoint(this.bloomAlpha, Math.Max(0f, bloomRadius)));
        int litRadius = (int)(baseRadius + petalSize * 1.5f);
        Add(new VertexLight(Color.White, 1f, litRadius, litRadius + 8));

        rotation = InitialRotation(position);
        Collidable = false;
    }

    /// <summary>花瓣大小倍率；外部动画可以每帧设置它。</summary>
    public float PetalScale { get; set; } = 1f;

    /// <summary>拾取时播放：环收缩、透明度不变，并短暂提高转速。</summary>
    public void PlayPickupAnimation()
    {
        AnimateTo(5f, baseAlpha, AnimationDuration, true);
    }

    /// <summary>籽回到原位时恢复初始半径和透明度，同样带短暂的转速提升。</summary>
    public void ResetPickupAnimation()
    {
        AnimateTo(baseRadius, baseAlpha, AnimationDuration, true);
    }

    /// <summary>
    /// 从当前半径/alpha 开始过渡到目标值；动画途中再次触发也不会跳变。
    /// </summary>
    public void AnimateTo(float targetRadius, float targetAlpha, float duration, bool spin = false)
    {
        animRadiusFrom = radius;
        animAlphaFrom = alpha;
        animRadiusTo = targetRadius;
        animAlphaTo = targetAlpha;
        animTime = 0f;
        animDuration = Math.Max(0.01f, duration);
        animating = true;

        if (spin)
        {
            spinBoost = SpinBoostAmount;
            spinBoostTime = 0f;
        }
    }

    public override void Update()
    {
        base.Update();

        if (animating)
        {
            animTime += Engine.DeltaTime;
            float t = Calc.Clamp(animTime / animDuration, 0f, 1f);
            float eased = Ease.SineInOut(t);
            radius = MathHelper.Lerp(animRadiusFrom, animRadiusTo, eased);
            alpha = MathHelper.Lerp(animAlphaFrom, animAlphaTo, eased);

            if (t >= 1f)
                animating = false;
        }

        if (spinBoost > 0f)
        {
            spinBoostTime += Engine.DeltaTime;
            float t = Calc.Clamp(spinBoostTime / SpinBoostDuration, 0f, 1f);
            spinBoost = SpinBoostAmount * (1f - Ease.SineInOut(t));

            if (t >= 1f)
                spinBoost = 0f;
        }

        rotation += MathHelper.ToRadians(rotationSpeed + spinBoost) * Engine.DeltaTime;
    }

    public override void Render()
    {
        base.Render();

        float scale = PetalScale;

        if (scale <= 0f)
            return;

        DrawPetals(scale);
    }

    private void DrawPetals(float scale)
    {
        if (alpha <= 0f)
            return;

        MTexture texture = PetalTexture;

        // 花瓣大小固定为 0.8 × baseRadius：籽的半径变化只影响环，不缩花瓣本身
        float currentPetalSize = petalSize;

        // 长度 0→1.5 倍，宽度 0→1 倍
        float length = currentPetalSize * 1.5f * scale;
        float width = currentPetalSize * scale;
        Vector2 petalScale = new(width / texture.Width, length / texture.Height);

        for (int i = 0; i < petalCount; i++)
        {
            float angle = rotation + MathHelper.TwoPi * i / petalCount;
            Vector2 position = Position + Calc.AngleToVector(angle, radius);

            // 长度以花瓣根部（内侧）为锚点缩放，尖端向外伸展；宽度仍然居中缩放
            texture.Draw(
                position,
                new Vector2(texture.Width / 2f, texture.Height),
                Color.White * alpha,
                petalScale,
                angle + MathHelper.PiOver2);
        }
    }

    private static MTexture PetalTexture =>
        petalTexture ??= GFX.Game["ClimbUpHelper/particles/petal"];

    private static float InitialRotation(Vector2 position)
    {
        // 用坐标生成固定的初始角度：同一位置每次加载都得到同一个角度，不同位置则随机分布
        float value = MathF.Sin(position.X * 12.9898f + position.Y * 78.233f) * 43758.5453f;
        return (value - MathF.Floor(value)) * MathHelper.TwoPi;
    }
}
