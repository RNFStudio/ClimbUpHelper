using Microsoft.Xna.Framework.Graphics;

namespace ChroniaHelper.Entities;

/// <summary>
/// 持有队列显示：屏幕中上方一排软边平行四边形格子，按玩家身上跟随的冲刺籽顺序显示花瓣数量。
/// 最先捡起的那颗在第一格；籽被消耗或掉落时，对应格子收起。
///
/// 现在是 DashSeed 的内置功能，也是整个 Level 全局唯一的一份：
/// 房间里有籽时由第一颗籽创建，之后切房间、死亡重载都不会再创建或卸载。
/// 样式固定，不读取实体属性。
/// </summary>
[Tracked]
public sealed class DashSeedInventoryHUD : Entity
{
    /// <summary>HUD 画布宽度。</summary>
    private const float ScreenWidth = 1920f;

    /// <summary>单个格子的宽度（完全展开时）。</summary>
    private const float SlotWidth = 96f;

    /// <summary>格子高度。</summary>
    private const float SlotHeight = 36f;

    /// <summary>格子间距。</summary>
    private const float Spacing = 8f;

    /// <summary>斜边宽度。</summary>
    private const float Slant = 10f;

    /// <summary>左右两侧各占宽度 25% 的 alpha 渐变区。</summary>
    private const float EdgeGradient = 0.25f;

    /// <summary>数字描边宽度。</summary>
    private const float Stroke = 2f;

    /// <summary>展开状态下格子的不透明度。</summary>
    private const float SlotAlpha = 0.8f;

    /// <summary>整排格子的纵向位置。</summary>
    private const float OffsetY = 48f;

    /// <summary>编号文字的缩放（比格子更大，属于样式的一部分）。</summary>
    private const float TextScale = 3.25f;

    /// <summary>单个格子展开或收起所需的秒数。</summary>
    private const float AnimationDuration = 0.5f;

    /// <summary>烘焙好的软边平行四边形贴图，只生成一次。</summary>
    private static Texture2D slotTexture;

    /// <summary>数字字体（Renogare 192 图集），只解析一次。</summary>
    private static PixelFontSize digitFont;

    private sealed class Slot
    {
        public DashSeed Seed;
        public float Progress;
        public float From;
        public float To = 1f;
        public float Elapsed;
        public bool Closing;
    }

    private readonly List<Slot> slots = new();

    private readonly List<DashSeed> activeSeeds = new();

    private readonly List<DashSeed> queue = new();

    private readonly List<float> slotWidths = new();

    public DashSeedInventoryHUD()
    {
        // HUD 画在 1920x1080 画布上；Global 让它在切房间、死亡重载时都不被卸载
        Tag = Tags.HUD | Tags.Global;
        Depth = -100;
        Visible = true;
    }

    private static Texture2D SlotTexture
    {
        get
        {
            if (slotTexture == null || slotTexture.IsDisposed)
                slotTexture = BuildSlotTexture();

            return slotTexture;
        }
    }

    /// <summary>场景里是否已经有队列 HUD。</summary>
    public static bool ExistsIn(Scene scene)
    {
        foreach (Entity entity in scene.Entities)
        {
            if (entity is DashSeedInventoryHUD)
                return true;
        }

        return false;
    }

    public override void Update()
    {
        base.Update();
        SyncSlots();
        AnimateSlots();
    }

    public override void Render()
    {
        if (slots.Count == 0)
            return;

        // 每个格子的宽度同时用于居中计算与绘制
        slotWidths.Clear();
        float totalWidth = 0f;

        foreach (Slot slot in slots)
        {
            float width = SlotWidth * slot.Progress;
            slotWidths.Add(width);
            totalWidth += width;
        }

        for (int i = 0; i < slots.Count - 1; i++)
        {
            // 间距按相邻两格中较慢的那个动画，避免格子出入时间距跳变
            totalWidth += Spacing * Math.Min(slots[i].Progress, slots[i + 1].Progress);
        }

        float x = (ScreenWidth - totalWidth) / 2f;

        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            float progress = slot.Progress;
            float width = slotWidths[i];

            DrawSlot(x, width, progress);

            float textAlpha = Math.Max(2f * progress - 1f, 0f);

            if (textAlpha > 0f && slot.Seed != null && slot.Seed.Petals > 0)
            {
                DrawNumber(slot.Seed.Petals, x + width / 2f, OffsetY + SlotHeight / 2f, textAlpha);
            }

            x += width;

            if (i < slots.Count - 1)
            {
                x += Spacing * Math.Min(progress, slots[i + 1].Progress);
            }
        }
    }

    /// <summary>当前持有的籽编号，按跟随顺序（先捡起的在前）。</summary>
    private void BuildQueue()
    {
        queue.Clear();

        Player player = Scene?.Tracker?.GetEntity<Player>();

        if (player?.Leader?.Followers == null)
            return;

        foreach (Follower follower in player.Leader.Followers)
        {
            // 花瓣数量为 0 的籽不进 HUD
            if (follower.Entity is DashSeed seed && seed.Petals > 0)
                queue.Add(seed);
        }
    }

    private void SyncSlots()
    {
        BuildQueue();

        // 未在收起的格子对应的编号，队尾应当与队列的队首重合
        activeSeeds.Clear();

        foreach (Slot slot in slots)
        {
            if (!slot.Closing)
                activeSeeds.Add(slot.Seed);
        }

        int overlap = 0;

        for (int s = Math.Min(activeSeeds.Count, queue.Count); s > 0; s--)
        {
            bool match = true;

            for (int i = 0; i < s; i++)
            {
                if (activeSeeds[activeSeeds.Count - s + i] != queue[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                overlap = s;
                break;
            }
        }

        // 队列变短：前面的格子开始收起
        int popCount = activeSeeds.Count - overlap;
        int activeIndex = 0;

        for (int popped = 0; popped < popCount && activeIndex < slots.Count; activeIndex++)
        {
            Slot slot = slots[activeIndex];

            if (!slot.Closing)
            {
                slot.Closing = true;
                slot.From = slot.Progress;
                slot.To = 0f;
                slot.Elapsed = 0f;
                popped++;
            }
        }

        // 队列变长：后面的格子开始展开
        int pushCount = queue.Count - overlap;

        for (int i = queue.Count - pushCount; i < queue.Count; i++)
        {
            slots.Add(new Slot
            {
                Seed = queue[i],
                Progress = 0f,
                From = 0f,
                To = 1f,
                Elapsed = 0f,
                Closing = false
            });
        }
    }

    private void AnimateSlots()
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            Slot slot = slots[i];
            slot.Elapsed += Engine.DeltaTime;

            float t = Calc.Clamp(slot.Elapsed / AnimationDuration, 0f, 1f);
            slot.Progress = slot.From + (slot.To - slot.From) * Ease.CubeInOut(t);

            if (slot.Closing && t >= 1f)
                slots.RemoveAt(i);
        }
    }

    /// <summary>
    /// 生成 96x36 的软边平行四边形：左边和右边各 10% 宽度从透明线性渐变到不透明，
    /// 4x4 超采样让斜边和渐变都平滑。只在第一次绘制时生成一次。
    /// </summary>
    private static Texture2D BuildSlotTexture()
    {
        const int samples = 4;

        int width = (int)SlotWidth;
        int height = (int)SlotHeight;
        float bodyWidth = width - Slant;
        float gradient = width * EdgeGradient;

        Color[] data = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float alpha = 0f;

                for (int sy = 0; sy < samples; sy++)
                {
                    float py = y + (sy + 0.5f) / samples;
                    float left = Slant * (1f - py / height);
                    float right = left + bodyWidth;

                    for (int sx = 0; sx < samples; sx++)
                    {
                        float px = x + (sx + 0.5f) / samples;

                        if (px < left || px > right)
                            continue;

                        float edge = Math.Min(px - left, right - px);
                        alpha += Calc.Clamp(edge / gradient, 0f, 1f);
                    }
                }

                data[y * width + x] = Color.Black * (alpha / (samples * samples));
            }
        }

        Texture2D texture = new Texture2D(Engine.Graphics.GraphicsDevice, width, height);
        texture.SetData(data);
        return texture;
    }

    private void DrawSlot(float x, float width, float progress)
    {
        if (width <= 0f || progress <= 0f)
            return;

        // 宽度动画直接横向缩放同一张软边贴图，边缘渐变会一起收放
        Draw.SpriteBatch.Draw(
            SlotTexture,
            new Vector2(x, OffsetY),
            null,
            Color.White * (SlotAlpha * progress),
            0f,
            Vector2.Zero,
            new Vector2(width / SlotWidth, 1f),
            SpriteEffects.None,
            0f);
    }

    /// <summary>
    /// 数字用的字体：优先取 Renogare 的 192px 图集，缩小绘制比把 64px 图集放大清晰得多。
    /// 当前语言不是 Renogare 时按需加载它（数字都是 ASCII，不受语言影响）。
    /// </summary>
    private static PixelFontSize DigitFont
    {
        get
        {
            if (digitFont == null)
            {
                PixelFont font = Fonts.Get("Renogare");

                if (font == null)
                {
                    try
                    {
                        font = Fonts.Load("Renogare");
                    }
                    catch
                    {
                        font = null;
                    }
                }

                digitFont = font != null ? font.Get(192f) : ActiveFont.FontSize;
            }

            return digitFont;
        }
    }

    private void DrawNumber(int petals, float centerX, float centerY, float alpha)
    {
        string text = petals.ToString();
        PixelFontSize font = DigitFont;
        Vector2 size = font.Measure(text);
        float scale = size.Y > 0f ? SlotHeight * 0.72f * TextScale / size.Y : 1f;

        font.Draw(
            text,
            new Vector2(centerX, centerY),
            new Vector2(0.5f, 0.5f),
            Vector2.One * scale,
            Color.White * alpha,
            0f,
            Color.Transparent,
            Stroke,
            Color.Black * alpha);
    }
}
