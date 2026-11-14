using Celeste.Mod.Backdrops;
using Microsoft.Xna.Framework.Graphics;

namespace ChroniaHelper.Backdrops;

/// <summary>
/// 背景 bloom 风格层：在所有背景 styleground 画完之后，把 Level buffer 里的背景模糊后加法叠回去。
/// 因为发生在 gameplay 合成之前，所以只有背景发光，玩家/前景/foreground styleground 都不受影响。
/// </summary>
[CustomBackdrop("ChroniaHelper/Bloom")]
public sealed class BloomBackdrop : Backdrop
{
    private readonly float strength;
    private readonly float blur;

    private static bool logged;

    /// <summary>纯加法混合：不受贴图 alpha 影响，直接叠上 tint 后的模糊背景。</summary>
    private static readonly BlendState AdditiveBlend = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add,
    };

    public BloomBackdrop(BinaryPacker.Element data)
    {
        strength = Math.Max(0f, data.AttrFloat("strength", 0.5f));
        blur = Math.Max(0.1f, data.AttrFloat("blur", 1.5f));

        // 这个 styleground 本身不画东西，只是 bloom 的设置/开关
        UseSpritebatch = false;
    }

    internal static void OnBackdropRender(
        On.Celeste.BackdropRenderer.orig_Render orig,
        BackdropRenderer self,
        Scene scene)
    {
        orig(self, scene);

        // 只处理背景那一层；Foreground 也走同一套 BackdropRenderer
        if (scene is not Level level || self != level.Background)
            return;

        foreach (Backdrop backdrop in self.Backdrops)
        {
            if (backdrop is BloomBackdrop bloom && bloom.Visible)
            {
                // 转场/淡出时 BackdropRenderer 会盖一层 Fade，bloom 也要跟着变暗
                float fade = Calc.Clamp(self.Fade, 0f, 1f);

                if (fade < 1f)
                    bloom.Apply(1f - fade);

                return;
            }
        }
    }

    private void Apply(float fadeMultiplier)
    {
        float amount = strength * FadeAlphaMultiplier * fadeMultiplier;

        if (amount <= 0f)
            return;

        if (!logged)
        {
            logged = true;
            Logger.Info("ClimbUpHelper/Bloom", $"Background bloom applied (strength={strength:0.##}, blur={blur:0.##}).");
        }

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
            AdditiveBlend,
            SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            null,
            Matrix.Identity);

        Draw.SpriteBatch.Draw(blurred, Vector2.Zero, Color.White * amount);
        Draw.SpriteBatch.End();
    }
}
