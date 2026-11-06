using Celeste.Mod.Entities;
using ChroniaHelper.Cores;

namespace ChroniaHelper.Entities;

/// <summary>
/// 持有队列显示：屏幕中上方一排平行四边形格子，按玩家身上跟随的冲刺籽顺序显示编号。
/// 最先捡起的那颗在第一格；籽被消耗或掉落时，对应格子收起。
///
/// 每个房间只需要放一个，没有放置时 DashSeed 会自动补一个默认参数的。
/// </summary>
[Tracked]
[CustomEntity("ChroniaHelper/DashSeedInventoryHUD")]
public sealed class DashSeedInventoryHUD : BaseEntity
{
    /// <summary>HUD 画布宽度。</summary>
    private const float ScreenWidth = 1920f;

    /// <summary>斜边宽度。</summary>
    private const float Slant = 10f;

    /// <summary>数字描边宽度。</summary>
    private const float Stroke = 2f;

    private sealed class Slot
    {
        public int Index;
        public float Progress;
        public float From;
        public float To = 1f;
        public float Elapsed;
        public bool Closing;
    }

    private readonly List<Slot> slots = new();

    private readonly List<int> activeIndices = new();

    private readonly List<int> queue = new();

    private readonly List<float> slotWidths = new();

    private readonly float slotWidth;
    private readonly float slotHeight;
    private readonly float spacing;
    private readonly float animationDuration;
    private readonly float slotAlpha;
    private readonly float offsetY;
    private readonly float textScale;

    private readonly bool showSeedIndex;

    public DashSeedInventoryHUD(EntityData data, Vector2 offset)
        : base(data, offset)
    {
        Tag = Tags.HUD;
        Depth = data.Int("depth", -100);
        Visible = true;

        slotWidth = Math.Max(1f, data.Float("slotWidth", 128f));
        slotHeight = Math.Max(1f, data.Float("slotHeight", 72f));
        spacing = Math.Max(0f, data.Float("spacing", 12f));

        // 单个格子展开或收起所需的秒数
        animationDuration = Math.Max(0.01f, data.Float("animationDuration", 0.5f));
        slotAlpha = Calc.Clamp(data.Float("slotAlpha", 0.5f), 0f, 1f);

        // 不叫 y，y 是地图里的摆位坐标
        offsetY = data.Float("offsetY", 48f);
        textScale = Math.Max(0.1f, data.Float("textScale", 1f));

        // 房间内籽头顶编号的显隐，由这里统一控制
        showSeedIndex = data.Bool("showSeedIndex", true);
    }

    /// <summary>是否在籽头顶显示自动序号，供 DashSeed 读取。</summary>
    public bool ShowSeedIndex => showSeedIndex;

    /// <summary>房间里没有手动放置的队列 HUD 时，补一个默认参数的。</summary>
    public static void EnsureIn(Scene scene)
    {
        if (scene == null)
            return;

        foreach (Entity entity in scene.Entities)
        {
            if (entity is DashSeedInventoryHUD)
                return;
        }

        scene.Add(new DashSeedInventoryHUD(new EntityData(), Vector2.Zero));
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
            float width = slotWidth * slot.Progress;
            slotWidths.Add(width);
            totalWidth += width;
        }

        for (int i = 0; i < slots.Count - 1; i++)
        {
            // 间距按相邻两格中较慢的那个动画，避免格子出入时间距跳变
            totalWidth += spacing * Math.Min(slots[i].Progress, slots[i + 1].Progress);
        }

        float x = (ScreenWidth - totalWidth) / 2f;

        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];
            float progress = slot.Progress;
            float width = slotWidths[i];

            DrawSlot(x, width, progress);

            float textAlpha = Math.Max(2f * progress - 1f, 0f);

            if (textAlpha > 0f)
            {
                DrawIndex(slot.Index, x + width / 2f, offsetY + slotHeight / 2f, textAlpha);
            }

            x += width;

            if (i < slots.Count - 1)
            {
                x += spacing * Math.Min(progress, slots[i + 1].Progress);
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
            if (follower.Entity is DashSeed seed)
                queue.Add(seed.Index);
        }
    }

    private void SyncSlots()
    {
        BuildQueue();

        // 未在收起的格子对应的编号，队尾应当与队列的队首重合
        activeIndices.Clear();

        foreach (Slot slot in slots)
        {
            if (!slot.Closing)
                activeIndices.Add(slot.Index);
        }

        int overlap = 0;

        for (int s = Math.Min(activeIndices.Count, queue.Count); s > 0; s--)
        {
            bool match = true;

            for (int i = 0; i < s; i++)
            {
                if (activeIndices[activeIndices.Count - s + i] != queue[i])
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
        int popCount = activeIndices.Count - overlap;
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
                Index = queue[i],
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

            float t = Calc.Clamp(slot.Elapsed / animationDuration, 0f, 1f);
            slot.Progress = slot.From + (slot.To - slot.From) * Ease.CubeInOut(t);

            if (slot.Closing && t >= 1f)
                slots.RemoveAt(i);
        }
    }

    private void DrawSlot(float x, float width, float progress)
    {
        if (width <= 0f || progress <= 0f)
            return;

        float slant = Math.Min(Slant, width * 0.5f);
        float bodyWidth = width - slant;

        if (bodyWidth <= 0f)
            return;

        Color color = Color.Black * (slotAlpha * progress);
        int rows = Math.Max(1, (int)MathF.Round(slotHeight));
        float rowHeight = slotHeight / rows;

        for (int row = 0; row < rows; row++)
        {
            float offset = rows > 1 ? slant * (1f - (float)row / (rows - 1f)) : 0f;
            Draw.Rect(x + offset, offsetY + row * rowHeight, bodyWidth, rowHeight + 0.5f, color);
        }
    }

    private void DrawIndex(int index, float centerX, float centerY, float alpha)
    {
        string text = index.ToString();
        Vector2 size = ActiveFont.Measure(text);
        float scale = size.Y > 0f ? slotHeight * 0.72f * textScale / size.Y : 1f;

        ActiveFont.DrawOutline(
            text,
            new Vector2(centerX, centerY),
            new Vector2(0.5f, 0.5f),
            Vector2.One * scale,
            Color.White * alpha,
            Stroke,
            Color.Black * alpha);
    }
}
