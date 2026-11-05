using System.Collections.Generic;
using System.Linq;
using NeonStrikers.Arenas;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a custom arena inside the CustomArena scene (a copy of Arena_01).
///
/// Every element is cloned from the real objects of Arena_01, so colliders, layers, physics materials,
/// particle effects and all serialized gameplay values (PortalTeleporter, GoalZone, LocalSlowZone,
/// GoalSlowMotionZone...) are exactly those of the stock arenas. Only position, rotation and size change:
///  * Wall / Platform: clone of Walls/BottomLeftWall / ArenaPlatforms/BottomPlatform (bar + outline collider),
///    end caps and outline keep their stock world thickness.
///  * Portal: clone of Portals/LeftPortal (trigger + PortalTeleporter 0.25 s cooldown, x1.1 ball / x0.9 player
///    exit speed, min exit speed 5, exit offset 0.5) with an exit point in front of its face; linked portals
///    exit through each other.
///  * Blue / Red goal: clones of Goals/LeftGoal (scoringTeam "Red") / Goals/RightGoal (scoringTeam "Blue"),
///    including their particles; GameManager's goal bursts point to them.
///  * Slow zone: clone of Goals/LeftGoalSlowZone (circle trigger, LocalSlowZone 0.97 / 0.97). Its goal slow
///    motion is driven by <see cref="CustomArenaSlowMotion"/> so time slows exactly like in a stock arena
///    whatever the number of zones (see that class).
///  * Ball / spawns: the scene's own ball, players and spawn points are moved.
///
/// Called by <see cref="GameManager"/> at the very start of its Start, before positions are reset and the
/// CPU (Vs Computer) reads the goals.
/// </summary>
[DisallowMultipleComponent]
public class CustomArenaBuilder : MonoBehaviour
{
    public const string LayoutRootName = "CustomArenaLayout";
    public const string WallTemplatePath = "Walls/BottomLeftWall";
    public const string PlatformTemplatePath = "ArenaPlatforms/BottomPlatform";
    public const string PortalTemplatePath = "Portals/LeftPortal";
    public const string BlueGoalTemplatePath = "Goals/LeftGoal";
    public const string RedGoalTemplatePath = "Goals/RightGoal";
    public const string SlowZoneTemplatePath = "Goals/LeftGoalSlowZone";
    public const string PortalExitName = "Exit";

    /// <summary>Stock layout of Arena_01 that is hidden (the templates stay inside, inactive).</summary>
    public static readonly string[] StockLayoutRoots =
    {
        "Walls", "ArenaPlatforms", "Portals", "Goals", "WhiteGrass", "ArchedLine_0", "ArchedLine_0 (1)"
    };

    static readonly Color[] PortalColors =
    {
        new Color(0.75f, 0.35f, 1f), new Color(1f, 0.6f, 0.1f), new Color(0.3f, 1f, 0.45f),
        new Color(1f, 0.95f, 0.25f), new Color(0.2f, 0.85f, 1f), new Color(1f, 0.4f, 0.75f)
    };

    sealed class BarTemplate
    {
        public Transform Source;
        public float ScaleZ;
        public float OutlineMarginX;
        public float OutlineMarginY;
        public readonly Dictionary<string, float> CapWorldWidths = new Dictionary<string, float>();
        public string OutlinePath;
    }

    public ArenaData Arena { get; private set; }
    public bool Built { get; private set; }
    public Transform LayoutRoot { get; private set; }

    /// <summary>Built GameObject for each arena object id.</summary>
    public readonly Dictionary<int, GameObject> ObjectsById = new Dictionary<int, GameObject>();

    public CustomArenaSlowMotion SlowMotion { get; private set; }

    GameManager manager;
    BarTemplate wallTemplate;
    BarTemplate platformTemplate;
    Transform portalTemplate;
    Transform blueGoalTemplate;
    Transform redGoalTemplate;
    Transform slowZoneTemplate;
    Sprite squareSprite;
    Sprite circleSprite;
    Material spriteMaterial;

    public static bool IsCustomArenaScene(Scene scene)
    {
        return scene.IsValid() && scene.name == CustomArenaFlow.CustomArenaScene;
    }

    /// <summary>
    /// Builds the arena when <paramref name="gameManager"/> lives in the CustomArena scene (no-op elsewhere).
    /// Uses the playtest layout, else the 4th arena of the game flow, else the starter layout.
    /// </summary>
    public static CustomArenaBuilder PrepareScene(GameManager gameManager)
    {
        if (gameManager == null || !IsCustomArenaScene(gameManager.gameObject.scene))
            return null;

        CustomArenaBuilder builder = gameManager.GetComponent<CustomArenaBuilder>();
        if (builder == null)
            builder = gameManager.gameObject.AddComponent<CustomArenaBuilder>();

        if (!builder.Built)
        {
            ArenaData arena = CustomArenaSession.ArenaForCustomScene(ArenaStorage.Default);
            if (arena == null)
            {
                Debug.LogWarning("[CustomArena] No custom arena selected; building the starter layout.");
                arena = ArenaFactory.CreateStarter("Starter Arena");
            }

            builder.Build(gameManager, arena);
        }

        if (CustomArenaSession.IsPlaytesting && gameManager.GetComponent<PlaytestOverlay>() == null)
            gameManager.gameObject.AddComponent<PlaytestOverlay>().arenaName = builder.Arena.DisplayName;

        return builder;
    }

    public void Build(GameManager gameManager, ArenaData arena)
    {
        if (Built)
            return;

        manager = gameManager;
        Arena = arena != null ? arena.Clone() : ArenaFactory.CreateStarter();
        Scene scene = gameObject.scene;

        if (!ResolveTemplates(scene))
        {
            Debug.LogError("[CustomArena] The CustomArena scene is missing the Arena_01 template objects; can't build the arena.");
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (StockLayoutRoots.Contains(root.name))
                root.SetActive(false);
        }

        var rootObject = new GameObject(LayoutRootName);
        SceneManager.MoveGameObjectToScene(rootObject, scene);
        LayoutRoot = rootObject.transform;

        var portals = new Dictionary<int, PortalTeleporter>();
        var exits = new Dictionary<int, Transform>();
        var slowZones = new List<CustomSlowZoneSensor>();
        ParticleSystem blueGoalBurst = null;
        ParticleSystem redGoalBurst = null;
        Dictionary<int, int> pairColors = PortalPairColors(Arena);

        foreach (ArenaObjectData obj in Arena.objects.Where(o => o != null).OrderBy(o => o.id))
        {
            if (!IsBuildable(obj))
                continue;

            GameObject built = null;
            switch (obj.Kind)
            {
                case ArenaObjectType.Wall:
                    built = BuildBar(wallTemplate, obj, "Wall");
                    break;

                case ArenaObjectType.Platform:
                    built = BuildBar(platformTemplate, obj, "Platform");
                    break;

                case ArenaObjectType.Portal:
                    built = BuildPortal(obj, pairColors, out PortalTeleporter teleporter, out Transform exit);
                    portals[obj.id] = teleporter;
                    exits[obj.id] = exit;
                    break;

                case ArenaObjectType.BlueGoal:
                case ArenaObjectType.RedGoal:
                    built = BuildGoal(obj, out ParticleSystem burst);
                    if (obj.Kind == ArenaObjectType.BlueGoal && blueGoalBurst == null)
                        blueGoalBurst = burst;
                    if (obj.Kind == ArenaObjectType.RedGoal && redGoalBurst == null)
                        redGoalBurst = burst;
                    break;

                case ArenaObjectType.SlowZone:
                    built = BuildSlowZone(obj, out CustomSlowZoneSensor sensor);
                    slowZones.Add(sensor);
                    break;

                case ArenaObjectType.Ball:
                    PlaceBall(obj.Position);
                    break;

                case ArenaObjectType.BlueSpawn:
                    PlacePlayer(manager.player1, manager.player1SpawnPoint, obj.Position);
                    break;

                case ArenaObjectType.RedSpawn:
                    PlacePlayer(manager.player2, manager.player2SpawnPoint, obj.Position);
                    break;
            }

            if (built != null)
                ObjectsById[obj.id] = built;
        }

        // Connect portals: entering a portal teleports to its partner's exit point.
        foreach (KeyValuePair<int, PortalTeleporter> pair in portals)
        {
            ArenaObjectData portal = Arena.Find(pair.Key);
            ArenaObjectData partner = Arena.Find(portal.linkedPortalId);
            bool linked = partner != null && partner.IsPortal && partner.id != portal.id &&
                          partner.linkedPortalId == portal.id && exits.ContainsKey(partner.id);
            pair.Value.exitPoint = linked ? exits[partner.id] : null;
        }

        // The goal Blue defends is where Red scores (GameManager plays leftGoalBurst for Red), and vice versa.
        manager.leftGoalBurst = blueGoalBurst;
        manager.rightGoalBurst = redGoalBurst;

        if (slowZones.Count > 0)
        {
            SlowMotion = rootObject.AddComponent<CustomArenaSlowMotion>();
            SlowMotion.Configure(slowZoneTemplate.GetComponent<GoalSlowMotionZone>(), slowZones);
        }

        // Colliders must match the new transforms right away: the CPU (Vs Computer) reads the goal bounds in its
        // Start, in this same frame, before the first physics step.
        Physics2D.SyncTransforms();
        Built = true;
    }

    static bool IsBuildable(ArenaObjectData obj)
    {
        return obj.Kind != ArenaObjectType.Unknown &&
               ArenaRules.IsFinite(obj.x) && ArenaRules.IsFinite(obj.y) && ArenaRules.IsFinite(obj.width) &&
               ArenaRules.IsFinite(obj.height) && ArenaRules.IsFinite(obj.rotation);
    }

    // ------------------------------------------------------------------ templates

    bool ResolveTemplates(Scene scene)
    {
        Dictionary<string, GameObject> roots = new Dictionary<string, GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!roots.ContainsKey(root.name))
                roots[root.name] = root;
        }

        Transform FindTemplate(string path)
        {
            string[] parts = path.Split('/');
            if (!roots.TryGetValue(parts[0], out GameObject root))
                return null;
            return parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts, 1, parts.Length - 1));
        }

        Transform wall = FindTemplate(WallTemplatePath);
        Transform platform = FindTemplate(PlatformTemplatePath);
        portalTemplate = FindTemplate(PortalTemplatePath);
        blueGoalTemplate = FindTemplate(BlueGoalTemplatePath);
        redGoalTemplate = FindTemplate(RedGoalTemplatePath);
        slowZoneTemplate = FindTemplate(SlowZoneTemplatePath);

        if (wall == null || platform == null || portalTemplate == null || blueGoalTemplate == null ||
            redGoalTemplate == null || slowZoneTemplate == null || manager.ball == null)
        {
            return false;
        }

        wallTemplate = MeasureBar(wall);
        platformTemplate = MeasureBar(platform);

        SpriteRenderer wallRenderer = wall.GetComponent<SpriteRenderer>();
        if (wallRenderer != null)
        {
            squareSprite = wallRenderer.sprite;
            spriteMaterial = wallRenderer.sharedMaterial;
        }

        SpriteRenderer ballRenderer = manager.ball.GetComponent<SpriteRenderer>();
        if (ballRenderer != null)
            circleSprite = ballRenderer.sprite;

        return true;
    }

    static string PathOf(Transform root, Transform child)
    {
        var parts = new List<string>();
        for (Transform t = child; t != null && t != root; t = t.parent)
            parts.Insert(0, t.GetSiblingIndex().ToString());
        return string.Join("/", parts);
    }

    static Transform ByPath(Transform root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return root;

        Transform t = root;
        foreach (string part in path.Split('/'))
        {
            int index = int.Parse(part);
            if (index < 0 || index >= t.childCount)
                return null;
            t = t.GetChild(index);
        }

        return t;
    }

    static float ScaleXBetween(Transform root, Transform node)
    {
        float scale = 1f;
        for (Transform t = node; t != null && t != root; t = t.parent)
            scale *= t.localScale.x;
        return scale;
    }

    static BarTemplate MeasureBar(Transform source)
    {
        var bar = new BarTemplate { Source = source };
        Vector3 s = source.lossyScale;
        bar.ScaleZ = source.localScale.z;

        foreach (Transform child in source.GetComponentsInChildren<Transform>(true))
        {
            if (child == source)
                continue;

            string path = PathOf(source, child);
            if (child.parent == source && child.GetComponent<BoxCollider2D>() != null && bar.OutlinePath == null)
            {
                bar.OutlinePath = path;
                bar.OutlineMarginX = (child.localScale.x - 1f) * Mathf.Abs(s.x) * 0.5f;
                bar.OutlineMarginY = (child.localScale.y - 1f) * Mathf.Abs(s.y) * 0.5f;
            }
            else if (child.name.StartsWith("Circle"))
            {
                float parentScaleX = Mathf.Abs(s.x) * ScaleXBetween(source, child.parent);
                bar.CapWorldWidths[path] = Mathf.Abs(child.localScale.x) * parentScaleX;
            }
        }

        return bar;
    }

    // ------------------------------------------------------------------ builders

    GameObject BuildBar(BarTemplate template, ArenaObjectData obj, string label)
    {
        float length = Mathf.Max(0.05f, obj.width);
        float thickness = Mathf.Max(0.05f, obj.height);
        float rotation = obj.rotation;

        // Stock bars are long along X with round caps at the ends: build tall ones as rotated long ones.
        if (thickness > length)
        {
            float swap = length;
            length = thickness;
            thickness = swap;
            rotation += 90f;
        }

        GameObject go = Instantiate(template.Source.gameObject, LayoutRoot);
        go.name = label + " #" + obj.id;
        Transform t = go.transform;
        t.position = new Vector3(obj.x, obj.y, template.Source.position.z);
        t.rotation = Quaternion.Euler(0f, 0f, rotation);
        t.localScale = new Vector3(length, thickness, template.ScaleZ);

        if (template.OutlinePath != null)
        {
            Transform outline = ByPath(t, template.OutlinePath);
            if (outline != null)
            {
                outline.localScale = new Vector3(
                    (length + 2f * template.OutlineMarginX) / length,
                    (thickness + 2f * template.OutlineMarginY) / thickness,
                    outline.localScale.z);
            }
        }

        foreach (KeyValuePair<string, float> cap in template.CapWorldWidths)
        {
            Transform capTransform = ByPath(t, cap.Key);
            if (capTransform == null)
                continue;

            float parentScaleX = length * ScaleXBetween(t, capTransform.parent);
            if (parentScaleX > 0.0001f)
            {
                Vector3 scale = capTransform.localScale;
                scale.x = cap.Value / parentScaleX;
                capTransform.localScale = scale;
            }
        }

        return go;
    }

    GameObject BuildPortal(ArenaObjectData obj, Dictionary<int, int> pairColors, out PortalTeleporter teleporter, out Transform exit)
    {
        float width = Mathf.Max(0.05f, obj.width);
        float depth = Mathf.Max(0.05f, obj.height);

        GameObject go = Instantiate(portalTemplate.gameObject, LayoutRoot);
        go.name = "Portal #" + obj.id;
        Transform t = go.transform;
        t.position = new Vector3(obj.x, obj.y, portalTemplate.position.z);
        t.rotation = Quaternion.Euler(0f, 0f, obj.rotation);
        t.localScale = Vector3.one;

        BoxCollider2D box = go.GetComponent<BoxCollider2D>();
        if (box != null)
        {
            box.offset = Vector2.zero;
            box.size = new Vector2(width, depth);
        }

        teleporter = go.GetComponent<PortalTeleporter>();
        teleporter.exitPoint = null;

        var exitObject = new GameObject(PortalExitName);
        exit = exitObject.transform;
        exit.SetParent(t, false);
        exit.localPosition = new Vector3(0f, depth * 0.5f + ArenaRules.PortalExitGap, 0f);
        exit.localRotation = Quaternion.identity;

        Color color = pairColors.TryGetValue(obj.id, out int index)
            ? PortalColors[index % PortalColors.Length]
            : new Color(0.6f, 0.6f, 0.6f);

        AddVisual(t, "Visual", squareSprite, new Color(color.r, color.g, color.b, 0.3f), Vector2.zero, new Vector2(width, depth), 1);
        AddVisual(t, "ExitFace", squareSprite, new Color(color.r, color.g, color.b, 0.9f),
            new Vector2(0f, depth * 0.5f - 0.04f), new Vector2(width, 0.08f), 2);
        return go;
    }

    GameObject BuildGoal(ArenaObjectData obj, out ParticleSystem burst)
    {
        Transform template = obj.Kind == ArenaObjectType.BlueGoal ? blueGoalTemplate : redGoalTemplate;
        BoxCollider2D templateBox = template.GetComponent<BoxCollider2D>();
        Vector2 colliderSize = templateBox != null ? templateBox.size : Vector2.one;
        if (Mathf.Abs(colliderSize.x) < 0.0001f) colliderSize.x = 1f;
        if (Mathf.Abs(colliderSize.y) < 0.0001f) colliderSize.y = 1f;

        GameObject go = Instantiate(template.gameObject, LayoutRoot);
        go.name = ArenaRules.DisplayName(obj.Kind) + " #" + obj.id;
        Transform t = go.transform;
        t.position = new Vector3(obj.x, obj.y, template.position.z);
        t.rotation = Quaternion.Euler(0f, 0f, obj.rotation);
        t.localScale = new Vector3(
            Mathf.Max(0.05f, obj.width) / colliderSize.x,
            Mathf.Max(0.05f, obj.height) / colliderSize.y,
            template.lossyScale.z);

        GoalZone zone = go.GetComponent<GoalZone>();
        if (zone != null)
            zone.scoringTeam = ArenaRules.ScoringTeam(obj.Kind);

        burst = null;
        foreach (ParticleSystem particles in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (particles.gameObject.name.Contains("Burst"))
            {
                burst = particles;
                break;
            }
        }

        return go;
    }

    GameObject BuildSlowZone(ArenaObjectData obj, out CustomSlowZoneSensor sensor)
    {
        float diameter = Mathf.Max(0.05f, obj.width);

        GameObject go = Instantiate(slowZoneTemplate.gameObject, LayoutRoot);
        go.name = "Slow Zone #" + obj.id;
        Transform t = go.transform;
        t.position = new Vector3(obj.x, obj.y, slowZoneTemplate.position.z);
        t.rotation = Quaternion.identity;
        t.localScale = Vector3.one;

        CircleCollider2D circle = go.GetComponent<CircleCollider2D>();
        if (circle != null)
        {
            circle.offset = Vector2.zero;
            circle.radius = diameter * 0.5f;
        }

        // Time slow-down is applied by CustomArenaSlowMotion (keeps the stock two-zone behaviour).
        GoalSlowMotionZone slowMotion = go.GetComponent<GoalSlowMotionZone>();
        if (slowMotion != null)
            slowMotion.enabled = false;

        sensor = go.AddComponent<CustomSlowZoneSensor>();
        AddVisual(t, "Visual", circleSprite, new Color(0.2f, 1f, 0.85f, 0.1f), Vector2.zero, new Vector2(diameter, diameter), 1);
        return go;
    }

    void AddVisual(Transform parent, string name, Sprite sprite, Color color, Vector2 localPosition, Vector2 size, int sortingOrder)
    {
        if (sprite == null)
            return;

        var visual = new GameObject(name);
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
        visual.transform.localRotation = Quaternion.identity;

        Vector2 spriteSize = sprite.bounds.size;
        visual.transform.localScale = new Vector3(
            size.x / Mathf.Max(0.0001f, spriteSize.x),
            size.y / Mathf.Max(0.0001f, spriteSize.y),
            1f);

        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        if (spriteMaterial != null)
            renderer.sharedMaterial = spriteMaterial;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
    }

    void PlaceBall(Vector2 position)
    {
        if (manager.ballSpawnPoint != null)
            manager.ballSpawnPoint.position = new Vector3(position.x, position.y, manager.ballSpawnPoint.position.z);

        if (manager.ball != null)
            manager.ball.transform.position = new Vector3(position.x, position.y, manager.ball.transform.position.z);
    }

    static void PlacePlayer(Transform player, Transform spawnPoint, Vector2 position)
    {
        if (spawnPoint != null)
            spawnPoint.position = new Vector3(position.x, position.y, spawnPoint.position.z);

        if (player != null)
            player.position = new Vector3(position.x, position.y, player.position.z);
    }

    /// <summary>Colour index per portal id: both portals of a linked pair share an index.</summary>
    public static Dictionary<int, int> PortalPairColors(ArenaData arena)
    {
        var result = new Dictionary<int, int>();
        int next = 0;
        foreach (ArenaObjectData portal in arena.objects.Where(o => o != null && o.IsPortal).OrderBy(o => o.id))
        {
            if (result.ContainsKey(portal.id))
                continue;

            ArenaObjectData partner = arena.Find(portal.linkedPortalId);
            if (partner == null || !partner.IsPortal || partner.linkedPortalId != portal.id || partner.id == portal.id)
                continue;

            result[portal.id] = next;
            result[partner.id] = next;
            next++;
        }

        return result;
    }
}
