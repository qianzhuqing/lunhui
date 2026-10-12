using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 战斗形象的**程序占位图**（0 张新图）。
///
/// 为什么要有这一份：美术的动作表（`docs/design/22_战斗表现.md` §2.1–2.2 的 `_anim.png`）
/// 还没交，而「整屏战场」这一版的核心就是**人站在场上、信息挂人身上**——没有形象，
/// 版面就退化成「一排血条浮在空处」，既看不出纵深，也没法对着示意图量位置。
/// 这里按 22 §1.3 的三层纵深，用代码画 32×32 的像素剪影顶上：**我方背视（越肩）、
/// 敌方正视**，两边都用**亮描边**分阵营（我方纸白／敌方冷灰，15 §4.4）——
/// 暗底上墨色描边等于没描边。
///
/// **只画站位与辨识，不画动作**：姿势、帧序全在美术的动作表里。等 `<id>_anim.png`／
/// `<id>.png` 一到位，`BattleScreen` 那边就自动改贴图（有图才摆），这份自然退休。
///
/// 像素格与配色照 `docs/dev/交接/小策划.md` 追加 19 里那份可编辑示意源，
/// 只把「3D 立体」那一档丢掉（22 §1.3 已定不引 3D）。
/// </summary>
public static class BattleAvatarArt
{
    private const int CELL = 32;

    /// <summary>
    /// 我方的辨识色（15 §4.4「背影只靠剪影与配色分人，不靠脸」）。
    /// 按 actor_id 定序取，同一个人每次都一样——不随帧变化。
    /// </summary>
    private static readonly Color[] ACCENTS =
    {
        new("#4E6E86"), new("#8C3B2A"), new("#6B4A7A"), new("#B08A2E"),
        new("#3E6E5A"), new("#7A5B2E"), new("#5A4A7A"), new("#2E6E6E"),
    };

    /// <summary>我方剪影：背视、越肩（22 §1.3「9 帧全按背面画」）。</summary>
    public static Texture2D ally_figure(string actor_id, string weapon_type, bool hat)
        => _figure(actor_id, true, weapon_type, hat);

    /// <summary>敌方剪影：正视、原地驻守（22 §1.3「敌方正视」）。</summary>
    public static Texture2D foe_figure(string actor_id, string weapon_type, bool hat)
        => _figure(actor_id, false, weapon_type, hat);

    /// <summary>
    /// 行动顺序条上的头像（28×28）：只有头与肩，四人是发色／衣领辨识，敌人压低眉＋暗色（22 §1.1 第五版）。
    /// </summary>
    public static Texture2D portrait(string actor_id, bool foe)
    {
        const int size = 28;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        Color accent = _accent(actor_id);
        Color skin = foe ? new Color("#C9B79C") : new Color("#E7CDA6");
        Color cloth = foe ? new Color("#39404A") : new Color("#4A4740");
        Color ink = new Color("#241F1A");
        _fill(image, 3, 21, 22, 7, cloth);
        _fill(image, 3, 24, 22, 4, accent);
        _fill(image, 8, 7, 12, 14, skin);
        _fill(image, 7, 3, 14, 6, accent);
        _fill(image, 6, 6, 3, 9, accent);
        _fill(image, 19, 6, 3, 9, accent);
        Color eye = foe ? new Color("#C0584A") : ink;
        _fill(image, 11, 12, 2, 2, eye);
        _fill(image, 16, 12, 2, 2, eye);
        _fill(image, 13, 18, 3, 1, new Color(0.15f, 0.13f, 0.11f, 0.6f));
        if (foe)
        {
            _fill(image, 8, 10, 12, 1, ink);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D _figure(string actor_id, bool back, string weapon_type, bool hat)
    {
        var grid = new byte[CELL, CELL];
        if (back)
        {
            _draw_back(grid, weapon_type, hat, _accent(actor_id));
        }
        else
        {
            _draw_front(grid, weapon_type, hat);
        }

        Color body = back ? new Color("#4A4740") : new Color("#3A3F46");
        Color rim = back ? new Color("#F0E7D2") : new Color("#8E97A3");
        Color accent = _accent(actor_id);
        Color skin = new Color("#E7CDA6");
        Color ink = new Color("#241F1A");
        var image = Image.CreateEmpty(CELL, CELL, false, Image.Format.Rgba8);
        for (int y = 0; y < CELL; y++)
        {
            for (int x = 0; x < CELL; x++)
            {
                byte cell = grid[y, x];
                if (cell == 0)
                {
                    continue;
                }

                Color color = cell switch
                {
                    2 => accent,
                    3 => skin,
                    4 => ink,
                    _ => _is_edge(grid, x, y) ? rim : body,
                };
                image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// 描边判定：四邻有一个空就当作外轮廓——这就是 15 §4.4 要的「亮描边」，
    /// 一整块实心剪影在暗底上是糊的。
    /// </summary>
    private static bool _is_edge(byte[,] grid, int x, int y) =>
        _empty(grid, x - 1, y) || _empty(grid, x + 1, y)
        || _empty(grid, x, y - 1) || _empty(grid, x, y + 1);

    private static bool _empty(byte[,] grid, int x, int y) =>
        x < 0 || x >= CELL || y < 0 || y >= CELL || grid[y, x] == 0;

    /// <summary>正视（敌方）：头在正中、双臂垂下、兵器在右手（画面右侧）。</summary>
    private static void _draw_front(byte[,] grid, string weapon_type, bool hat)
    {
        _rect(grid, 12, 23, 14, 30);
        _rect(grid, 17, 23, 19, 30);
        _rect(grid, 12, 13, 19, 22);
        _rect(grid, 10, 11, 21, 13);
        _rect(grid, 9, 13, 10, 20);
        _rect(grid, 21, 13, 22, 20);
        _rect(grid, 15, 11, 16, 12);
        _rect(grid, 13, 4, 18, 10);
        if (hat)
        {
            _rect(grid, 10, 2, 21, 5);
        }

        switch (weapon_type)
        {
            case "sword":
                _rect(grid, 23, 6, 24, 9);
                _rect(grid, 22, 10, 25, 11);
                _rect(grid, 23, 12, 24, 25);
                break;
            case "blade":
                _rect(grid, 22, 9, 24, 11);
                _rect(grid, 21, 12, 25, 13);
                _rect(grid, 22, 14, 24, 26);
                break;
            case "bow":
                foreach ((int bx, int by) in BOW_FRONT)
                {
                    _rect(grid, bx, by, bx + 1, by + 1);
                }

                for (int y = 9; y <= 20; y++)
                {
                    grid[y, 9] = 1;
                }

                break;
        }
    }

    /// <summary>背视（我方）：肩膀不对称、露出一列侧脸与一只眼，兵器在近侧手。</summary>
    private static void _draw_back(byte[,] grid, string weapon_type, bool hat, Color accent)
    {
        _rect(grid, 14, 24, 16, 31);
        _rect(grid, 19, 23, 21, 30);
        _rect(grid, 13, 13, 19, 23);
        _rect(grid, 12, 13, 13, 15);
        _rect(grid, 18, 12, 21, 14);
        _rect(grid, 20, 13, 21, 20);
        _rect(grid, 15, 11, 18, 12);
        _rect(grid, 15, 4, 19, 10);
        _rect(grid, 14, 6, 14, 10, 3);
        _rect(grid, 14, 7, 14, 7, 4);
        _rect(grid, 15, 2, 20, 4, 2);
        if (hat)
        {
            _rect(grid, 16, 2, 20, 5, 2);
        }

        _rect(grid, 13, 19, 19, 20, 2);
        switch (weapon_type)
        {
            case "sword":
                _rect(grid, 24, 5, 25, 8);
                _rect(grid, 23, 9, 26, 10);
                _rect(grid, 24, 11, 25, 24);
                break;
            case "blade":
                _rect(grid, 23, 8, 25, 10);
                _rect(grid, 22, 11, 26, 12);
                _rect(grid, 23, 13, 25, 25);
                break;
            case "bow":
                foreach ((int bx, int by) in BOW_BACK)
                {
                    _rect(grid, bx, by, bx + 1, by + 1);
                }

                for (int y = 8; y <= 19; y++)
                {
                    grid[y, 11] = 1;
                }

                break;
        }
    }

    private static readonly (int x, int y)[] BOW_FRONT =
    {
        (6, 8), (5, 10), (5, 12), (5, 14), (5, 16), (6, 18), (7, 19), (8, 20),
    };

    private static readonly (int x, int y)[] BOW_BACK =
    {
        (8, 7), (7, 9), (7, 11), (7, 13), (7, 15), (8, 17), (9, 18), (10, 19),
    };

    private static void _rect(byte[,] grid, int x0, int y0, int x1, int y1, byte value = 1)
    {
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (x >= 0 && x < CELL && y >= 0 && y < CELL)
                {
                    grid[y, x] = value;
                }
            }
        }
    }

    private static void _fill(Image image, int x, int y, int width, int height, Color color)
    {
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                int px = x + dx;
                int py = y + dy;
                if (px >= 0 && px < image.GetWidth() && py >= 0 && py < image.GetHeight())
                {
                    image.SetPixel(px, py, color);
                }
            }
        }
    }

    /// <summary>同一个 actor_id 每次都取到同一种辨识色（不随帧、不随顺序变）。</summary>
    private static Color _accent(string actor_id)
    {
        int hash = 0;
        foreach (char letter in actor_id)
        {
            hash = ((hash * 31) + letter) & 0x7FFFFFF;
        }

        return ACCENTS[hash % ACCENTS.Length];
    }
}
