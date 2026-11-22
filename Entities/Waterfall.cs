using Celeste.Mod.Entities;
using ChroniaHelper.Utils;

namespace ChroniaHelper.Entities;

/// <summary>
/// 简易瀑布：可自由缩放，填充和 2px 描边各自用颜色（颜色自带 alpha，不再额外给 alpha 参数）。
/// 底部接到水面时按水面波动衔接并产生 ripple，同时通过 DisplacementRenderHook 像原版瀑布一样扭曲后面的景物。
/// 不渲染 detailed lines。
/// </summary>
[CustomEntity("ChroniaHelper/Waterfall")]
public sealed class Waterfall : Entity
{
    /// <summary>产生 ripple 的间隔（秒）。</summary>
    private const float RippleInterval = 0.3f;

    /// <summary>每个 ripple 大约覆盖的宽度，用来决定一条瀑布上放几个 ripple。</summary>
    private const float RippleSpacing = 40f;

    /// <summary>底边与水面允许的衔接误差（像素）。</summary>
    private const float ConnectTolerance = 8f;

    /// <summary>左右描边的宽度（像素），与原版瀑布一致。</summary>
    private const float OutlineWidth = 2f;

    /// <summary>瀑布区域写进 displacement 缓冲的颜色，与原版瀑布一致。</summary>
    private static readonly Color DisplacementColor = new Color(0.5f, 0.5f, 0.8f, 1f);

    private readonly float width;
    private readonly float height;
    private readonly float rippleStrength;
    private readonly Color fillColor;
    private readonly Color outlineColor;

    private Water water;

    /// <summary>实际绘制高度：接水时会被吸附到水面，其余情况等于面板高度。</summary>
    private float renderHeight;

    public Waterfall(EntityData data, Vector2 offset)
        : base(data.Position + offset)
    {
        width = data.Width;
        height = data.Height;
        rippleStrength = Math.Max(0f, data.Float("ripple_strength", 0.75f));
        fillColor = data.PackColor("fill_color", new CColor(Color.LightSkyBlue, 0.3f)).Parsed();
        outlineColor = data.PackColor("outline_color", new CColor(Color.LightSkyBlue, 0.8f)).Parsed();
        Depth = data.Int("depth", -49900);
        Tag = Tags.TransitionUpdate;

        Add(new DisplacementRenderHook(RenderDisplacement));
    }

    /// <summary>瀑布宽度（世界像素）。</summary>
    public new float Width => width;

    /// <summary>瀑布高度（世界像素），由面板直接指定。</summary>
    public new float Height => height;

    /// <summary>实际绘制高度：接水时会吸附到水面。</summary>
    public float RenderedHeight => renderHeight;

    public override void Awake(Scene scene)
    {
        base.Awake(scene);

        water = FindBottomWater(scene);
        renderHeight = height;

        if (water == null)
            return;

        // 只把「底边刚好接在水面」视为衔接；差得太多（比如整条瀑布埋在水里）就不算
        float toSurface = water.Y - Y;

        if (Math.Abs(toSurface - height) > ConnectTolerance)
        {
            water = null;
            return;
        }

        renderHeight = Math.Max(toSurface, 0f);
    }

    public override void Update()
    {
        base.Update();

        if (water == null || !water.Active || water.TopSurface == null)
            return;

        if (!Scene.OnInterval(RippleInterval))
            return;

        int count = Math.Max(1, (int)MathF.Ceiling(width / RippleSpacing));
        float step = width / count;

        for (int i = 0; i < count; i++)
        {
            water.TopSurface.DoRipple(new Vector2(X + step * (i + 0.5f), water.Y), rippleStrength);
        }
    }

    public override void Render()
    {
        base.Render();

        if (water?.TopSurface is Water.Surface surface)
            DrawConnected(surface);
        else
            DrawStraight();
    }

    /// <summary>
    /// 像原版瀑布一样把瀑布区域写进 displacement 缓冲；接水时和填充一样按 1px 列跟随水面，
    /// 否则最后一小段（水面波动处）会没有扭曲。
    /// </summary>
    public void RenderDisplacement()
    {
        if (water?.TopSurface is not Water.Surface surface)
        {
            Draw.Rect(X, Y, width, renderHeight, DisplacementColor);
            return;
        }

        float full = renderHeight + surface.Position.Y - water.Y;
        int columns = Math.Max(1, (int)MathF.Ceiling(width));

        for (int i = 0; i < columns; i++)
        {
            float x = X + i;
            float columnHeight = full - MathF.Round(surface.GetSurfaceHeight(new Vector2(x, water.Y)));

            if (columnHeight > 0f)
                Draw.Rect(x, Y, 1f, columnHeight, DisplacementColor);
        }
    }

    private void DrawStraight()
    {
        float innerWidth = width - 2f;

        if (innerWidth > 0f)
            Draw.Rect(X + 1f, Y, innerWidth, renderHeight, fillColor);

        Draw.Rect(X - 1f, Y, OutlineWidth, renderHeight, outlineColor);
        Draw.Rect(X + width - 1f, Y, OutlineWidth, renderHeight, outlineColor);
    }

    private void DrawConnected(Water.Surface surface)
    {
        // 与原版 WaterFall 相同的接水画法：按 1px 列跟随水面高度
        float full = renderHeight + surface.Position.Y - water.Y;

        int innerColumns = Math.Max(0, (int)MathF.Ceiling(width - 2f));

        for (int i = 0; i < innerColumns; i++)
        {
            float x = X + 1f + i;
            float columnHeight = full - MathF.Round(surface.GetSurfaceHeight(new Vector2(x, water.Y)));

            if (columnHeight > 0f)
                Draw.Rect(x, Y, 1f, columnHeight, fillColor);
        }

        float leftHeight = full - MathF.Round(surface.GetSurfaceHeight(new Vector2(X, water.Y)));

        if (leftHeight > 0f)
            Draw.Rect(X - 1f, Y, OutlineWidth, leftHeight, outlineColor);

        float rightHeight = full - MathF.Round(surface.GetSurfaceHeight(new Vector2(X + width, water.Y)));

        if (rightHeight > 0f)
            Draw.Rect(X + width - 1f, Y, OutlineWidth, rightHeight, outlineColor);
    }

    /// <summary>
    /// 找出底边接触到的水：用一个跨在底边上的 16px 高区域判断，
    /// 底边贴住水面、或者稍微埋进/悬在水面上方一点，都能命中。
    /// </summary>
    private Water FindBottomWater(Scene scene)
    {
        Rectangle bottom = new Rectangle(
            (int)MathF.Floor(X),
            (int)MathF.Floor(Y + height) - 8,
            Math.Max(1, (int)MathF.Ceiling(width)),
            16);

        return scene.CollideFirst<Water>(bottom);
    }
}
