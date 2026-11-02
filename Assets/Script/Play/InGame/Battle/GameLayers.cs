using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// GameLayers : 프로젝트에서 쓰는 레이어 이름을 한 곳에 모아 둔 도우미
//   레이어 이름이 바뀌면 이 파일의 문자열만 고치면 된다.
//   (LayerMask.NameToLayer 는 느려서 처음 한 번만 계산해 보관한다)
// ─────────────────────────────────────────────────────────────────────────────

public static class GameLayers
{
    public const string GroundName = "Ground";
    public const string WallName = "Wall";
    public const string BulletName = "Bullet";
    public const string ConfinerName = "Confiner";
    public const string DeadBodyName = "DeadBody";
    public const string PlayerName = "Player";
    public const string BarrierName = "Barrier";

    private static int ground = -2, wall = -2, bullet = -2, confiner = -2, deadBody = -2, player = -2, barrier = -2;

    private static int Cache(ref int field, string name)
    {
        if (field == -2) field = LayerMask.NameToLayer(name);   // 없으면 -1
        return field;
    }

    public static int Ground { get { return Cache(ref ground, GroundName); } }
    public static int Wall { get { return Cache(ref wall, WallName); } }
    public static int Bullet { get { return Cache(ref bullet, BulletName); } }
    public static int Confiner { get { return Cache(ref confiner, ConfinerName); } }
    public static int DeadBody { get { return Cache(ref deadBody, DeadBodyName); } }
    public static int Player { get { return Cache(ref player, PlayerName); } }
    public static int Barrier { get { return Cache(ref barrier, BarrierName); } }

    public static int GroundMask { get { return LayerMask.GetMask(GroundName); } }

    /// <summary>총알이 아예 무시하는 레이어 (다른 총알, 맵 경계, 시체)</summary>
    public static bool IsIgnoredByBullet(int layer)
    {
        return layer == Bullet || layer == Confiner || layer == DeadBody;
    }

    /// <summary>총알이 멈추는 "맵" 레이어 (땅, 벽)</summary>
    public static bool IsWorld(int layer)
    {
        return layer == Ground || layer == Wall;
    }

    /// <summary>오브젝트와 모든 자식의 레이어를 바꾼다.</summary>
    public static void SetRecursively(GameObject obj, int layer)
    {
        if (layer < 0) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetRecursively(child.gameObject, layer);
    }
}
