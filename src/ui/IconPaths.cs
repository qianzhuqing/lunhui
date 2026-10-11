using Godot;

namespace Lunhui.Ui;

/// <summary>
/// 图标路径的**唯一出处**（C# 版，对位原 `src/ui/icon_paths.gd`；设计 15 §六「命名规范」）：
///
/// | 类别 | 路径 | 例 |
/// |---|---|---|
/// | 武学图标 | `assets/icons/skill/&lt;skill_id&gt;.png` | `sk_xuanwei_01.png` |
/// | 装备图标 | `assets/icons/equip/&lt;equip_id&gt;.png` | `eq_sword_01.png` |
/// | 物品图标 | `assets/icons/item/&lt;item_id&gt;.png` | `item_iron.png` |
/// | 增益图标 | `assets/icons/buff/&lt;buff_id&gt;.png` | `buff_guard.png` |
/// | 角色／敌人立绘 | `assets/sprites/&lt;faction&gt;/&lt;id&gt;.png` | `sprites/bandit/en_bd_thug.png` |
///
/// 为什么单开一份：这几条路径以前散在各处（大地图标记在旧 `overworld_controller.ICON_DIR`、
/// 精英标识在 `roaming_enemy.ELITE_MARKER_DIR`），再加「武学／道具」两处就是**四个副本**——
/// 换目录时漏一处就是「有些图忽然不显示了」这种没人会红的毛病。
///
/// **没图是常态**（美术约 160 个图标只交付了一小部分）：所以这里提供
/// `load_icon()` / `make_icon()`——**有图才摆，没图返回 null**，调用方不要自己 `load()`
/// （加载失败会刷一条引擎错误，而"这张图还没出"不是错误）。
/// </summary>
public partial class IconPaths : RefCounted
{
    public const string ROOT = "res://assets/icons/";
    public const string SKILL_DIR = ROOT + "skill/";
    public const string EQUIP_DIR = ROOT + "equip/";
    public const string ITEM_DIR = ROOT + "item/";
    public const string BUFF_DIR = ROOT + "buff/";
    /// <summary>
    /// 异常状态图标（15 §4.3 的「异常状态 4」）。**目录约定先由开发侧定在 `icons/status/`**：
    /// `status_effect.icon` 的值就是文件名（如 `status_poison.png`），空则退回行 id——
    /// 与 `item_base.icon`／`buff_def.icon` 同一套优先级。这条路径待设计侧在交接单里确认（`待策划确认.md` Q80）。
    /// </summary>
    public const string STATUS_DIR = ROOT + "status/";
    /// <summary>
    /// 七维图标（15 §六 命名表，2026-10-04 设计拍）：**`attribute_def` 没有 `icon` 列**，
    /// 一律按行 id 走（`icons/attr/&lt;attr_id&gt;.png`）；派生数值那批同理（`icons/stat/&lt;stat_id&gt;.png`）。
    /// </summary>
    public const string ATTR_DIR = ROOT + "attr/";
    public const string STAT_DIR = ROOT + "stat/";
    /// <summary>角色与敌人的**剪影／立绘**不在 `icons/` 下，而在 `assets/sprites/&lt;faction&gt;/`（15 §六）。</summary>
    public const string SPRITES_DIR = "res://assets/sprites/";

    /// <summary>图标显示尺寸：15 §六 定的是原生 32×32（与地编的贴图同规格）。</summary>
    public const int SIZE = 32;

    public static string skill(string skill_id) => _under(SKILL_DIR, skill_id);

    public static string equip(string equip_id) => _under(EQUIP_DIR, equip_id);

    public static string item(string item_id) => _under(ITEM_DIR, item_id);

    public static string buff(string buff_id) => _under(BUFF_DIR, buff_id);

    public static string status(string status_id) => _under(STATUS_DIR, status_id);

    public static string attr(string attr_id) => _under(ATTR_DIR, attr_id);

    public static string stat(string stat_id) => _under(STAT_DIR, stat_id);

    /// <summary>
    /// 角色／敌人的剪影：`assets/sprites/&lt;faction&gt;/&lt;enemy_id&gt;.png`（15 §六）。
    /// `faction` 就是 `enemy_base.faction`（bandit／neutral／beast／rogue／hidden／training）。
    /// </summary>
    public static string actor(string faction, string actor_id)
    {
        string who = actor_id.StripEdges();
        string side = faction.StripEdges();
        if (string.IsNullOrEmpty(who) || string.IsNullOrEmpty(side))
        {
            return "";
        }

        return SPRITES_DIR + side + "/" + who + ".png";
    }

    /// <summary>有这张图就返回它，没有就返回 null（**不报错**——还没出图不是数据错）。</summary>
    public static Texture2D load_icon(string path)
    {
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path))
        {
            return null;
        }

        return ResourceLoader.Load(path) as Texture2D;
    }

    /// <summary>
    /// 按路径摆一个图标：没图返回 null，调用方据此决定「这一行要不要留位置」。
    ///
    /// **只有一个两参签名**：C# 的默认参数不进 Godot 的方法签名（坑 87），
    /// 而**重载也不行**——GDScript 解析脚本类的静态调用时，一个名字出现两次就报
    /// `Static function "make_icon()" not found in base "CSharpScript"`（坑 92）。
    /// `node_name` 想省就用 `"Icon"`，但必须显式写出来。
    /// </summary>
    public static TextureRect make_icon(string path, string node_name)
    {
        Texture2D texture = load_icon(path);
        if (texture == null)
        {
            return null;
        }

        var rect = new TextureRect
        {
            Name = node_name,
            Texture = texture,
            CustomMinimumSize = new Vector2(SIZE, SIZE),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        return rect;
    }

    private static string _under(string dir, string id)
    {
        string trimmed = id.StripEdges();
        return string.IsNullOrEmpty(trimmed) ? "" : dir + trimmed + ".png";
    }
}
