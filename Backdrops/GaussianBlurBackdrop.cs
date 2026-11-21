using Celeste.Mod.Backdrops;
using Microsoft.Xna.Framework.Graphics;

namespace ChroniaHelper.Backdrops;

/// <summary>
/// 通用的高斯模糊 styleground：在这一层渲染的那一刻，把已经画进 Level buffer 的内容做高斯模糊，
/// 再按指定混合模式叠回去。
/// 放在背景层时模糊的是比它更深的背景；放在前景层时模糊的是已经画好的背景 + 游戏画面。
/// </summary>
[CustomBackdrop("ChroniaHelper/GaussianBlur")]
public class GaussianBlurBackdrop : Backdrop
{
    private enum BlendMode
    {
        Additive,
        Alpha,
    }

    private readonly BlendMode mode;
    private readonly float strength;
    private readonly float blur;
    private readonly float alpha;

    /// <summary>纯加法混合：不受贴图 alpha 影响，直接叠上 tint 后的模糊图像。</summary>
    private static readonly BlendState AdditiveBlend = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add,
    };

    public GaussianBlurBackdrop(BinaryPacker.Element data)
    {
        mode = data.Attr("mode", "additive").Equals("alpha", StringComparison.OrdinalIgnoreCase)
            ? BlendMode.Alpha
            : BlendMode.Additive;
        strength = Math.Max(0f, data.AttrFloat("strength", 1f));
        blur = Math.Max(0.1f, data.AttrFloat("blur", 1f));
        alpha = Calc.Clamp(data.AttrFloat("alpha", 1f), 0f, 1f);

        // 这个 styleground 自己不用 SpriteBatch；Render 里会结束/重开 batch 做 render target 操作
        UseSpritebatch = false;
    }

    public override void Render(Scene scene)
    {
        base.Render(scene);

        float amount = strength * alpha;

        if (amount <= 0f || scene is not Level)
            return;

        // 只取当前已经画进 Level buffer 的内容：
        // 背景层 = 比这一层更深的背景；前景层 = 背景 + 游戏画面
        Texture2D blurred = GaussianBlur.Blur(
            GameplayBuffers.Level,
            GameplayBuffers.TempA,
            GameplayBuffers.TempB,
            0f,
            true,
            GaussianBlur.Samples.Nine,
            blur,
            GaussianBlur.Direction.Both,
            1f);

        // 模糊失败时 GaussianBlur 会把原图返回；这时不能把 Level 采样到它自己身上
        if (ReferenceEquals(blurred, GameplayBuffers.Level))
            return;

        Engine.Instance.GraphicsDevice.SetRenderTarget(GameplayBuffers.Level);

        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            mode == BlendMode.Additive ? AdditiveBlend : BlendState.AlphaBlend,
            SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            null,
            Matrix.Identity);

        Draw.SpriteBatch.Draw(blurred, Vector2.Zero, Color.White * amount);
        Draw.SpriteBatch.End();
    }
}
