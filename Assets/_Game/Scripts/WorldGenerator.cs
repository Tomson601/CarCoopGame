using System.Collections.Generic;
using UnityEngine;

// Nieskończony świat: droga i teren generowane w kawałkach wokół graczy, aut i przedmiotów.
// Wszystko wynika z ziarna (seed), więc każdy gracz generuje u siebie identyczny świat.
public class WorldGenerator : MonoBehaviour
{
    [Header("Ziarno świata (takie samo u wszystkich graczy)")]
    public int seed = 12345;

    [Header("Droga")]
    public float segmentLength = 100f;
    public int samplesPerSegment = 25;
    public float roadHalfWidth = 3.5f;
    [Tooltip("Maks. zmiana kierunku drogi na odcinek (stopnie)")]
    public float maxTurnPerSegment = 18f;
    [Tooltip("Maks. odchylenie drogi od kierunku na wprost (stopnie). Poniżej 90 = droga nie zawraca.")]
    public float maxHeading = 55f;
    [Tooltip("Ile pierwszych odcinków jest prostych (miejsce startu)")]
    public int straightStartSegments = 3;

    [Header("Teren")]
    public float tileSize = 100f;
    public int tileResolution = 25;
    public float hillHeight = 7f;
    public float hillScale = 0.004f;
    public float detailHeight = 1.5f;
    public float detailScale = 0.03f;
    [Tooltip("Od tej odległości od osi drogi teren wraca do naturalnej wysokości")]
    public float roadBlendDistance = 25f;
    [Tooltip("Promień płaskiego terenu wokół miejsca startu")]
    public float spawnFlatRadius = 40f;

    [Header("Ładowanie")]
    public float loadRadius = 250f;
    public float unloadRadius = 320f;
    [Tooltip("Promień terenu trzymanego pod leżącymi przedmiotami")]
    public float itemRadius = 30f;
    public int maxBuildsPerUpdate = 3;

    [Header("Materiały")]
    public Material roadMaterial;
    public Material lineMaterial;
    public Material terrainMaterial;
    [System.Serializable]
    public class ScatterItem
    {
        public GameObject prefab;
        [Tooltip("Jak często pojawia się w porównaniu z innymi")]
        public float weight = 1f;
        public float minScale = 0.8f;
        public float maxScale = 1.3f;
        [Tooltip("Minimalna odległość od osi drogi")]
        public float minRoadDistance = 8f;
    }

    [Header("Przyroda")]
    public ScatterItem[] nature;
    [Tooltip("Ile obiektów przyrody na jeden kawałek terenu (100 x 100 m)")]
    public float natureDensity = 25f;

    [Header("Przy drodze")]
    public GameObject polePrefab;
    [Tooltip("Co ile próbek drogi stoi słup (próbka = ok. 4 m)")]
    public int poleEverySamples = 12;
    [Tooltip("Odległość słupów i znaków od krawędzi drogi")]
    public float roadsideOffset = 3f;
    public GameObject kmSignPrefab;
    [Tooltip("Co ile odcinków drogi stoi znak kilometrowy (odcinek = ok. 100 m)")]
    public int kmSignEverySegments = 10;
    // Punkty kontrolne drogi. Indeks listy = numer punktu + PointOffset (punkty zaczynają się od -3).
    private const int PointOffset = 3;
    private const int FirstSegment = -2;
    private readonly List<Vector3> controlPoints = new List<Vector3>();
    private float heading;

    private readonly Dictionary<int, Vector3[]> roadSamples = new Dictionary<int, Vector3[]>();
    private readonly Dictionary<int, GameObject> roadObjects = new Dictionary<int, GameObject>();
    private readonly Dictionary<Vector2Int, GameObject> tileObjects = new Dictionary<Vector2Int, GameObject>();

    private Vector2 noiseOffset;
    private float nextUpdate;

    struct Focus
    {
        public Vector3 position;
        public float radius;
        public Focus(Vector3 p, float r) { position = p; radius = r; }
    }

    // ---------- Start ----------

    void Awake()
    {
        noiseOffset = new Vector2(Hash(0, 100) * 10000f, Hash(0, 101) * 10000f);
        GetControlPoint(straightStartSegments + 2);

        // Teren wokół startu budujemy od razu, zanim cokolwiek na scenie zdąży spaść
        UpdateChunks(GatherFocus(), int.MaxValue);
    }

    void Update()
    {
        if (Time.time < nextUpdate) return;
        nextUpdate = Time.time + 0.25f;
        UpdateChunks(GatherFocus(), maxBuildsPerUpdate);
    }

    List<Focus> GatherFocus()
    {
        List<Focus> focus = new List<Focus>();

        foreach (FirstPersonController p in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
            focus.Add(new Focus(p.transform.position, loadRadius));

        foreach (CarController c in FindObjectsByType<CarController>(FindObjectsSortMode.None))
            focus.Add(new Focus(c.transform.position, loadRadius));

        // Leżące przedmioty trzymają teren pod sobą, żeby nie spadły w nicość
        foreach (Pickupable item in FindObjectsByType<Pickupable>(FindObjectsSortMode.None))
            focus.Add(new Focus(item.transform.position, itemRadius));

        // W menu, zanim ktokolwiek się pojawi: okolica startu
        if (focus.Count == 0) focus.Add(new Focus(Vector3.zero, loadRadius));
        return focus;
    }

    // ---------- Ładowanie i usuwanie kawałków ----------

    void UpdateChunks(List<Focus> focus, int budget)
    {
        int built = 0;
        float margin = unloadRadius - loadRadius;

        // Teren: potrzebne kawałki, od najbliższych
        Dictionary<Vector2Int, float> neededTiles = new Dictionary<Vector2Int, float>();
        foreach (Focus f in focus)
        {
            Vector2Int min = TileOf(f.position - new Vector3(f.radius, 0, f.radius));
            Vector2Int max = TileOf(f.position + new Vector3(f.radius, 0, f.radius));

            for (int x = min.x; x <= max.x; x++)
                for (int z = min.y; z <= max.y; z++)
                {
                    Vector2Int tile = new Vector2Int(x, z);
                    float d = DistanceToTile(f.position, tile);
                    if (d > f.radius) continue;
                    if (!neededTiles.TryGetValue(tile, out float old) || d < old) neededTiles[tile] = d;
                }
        }

        List<KeyValuePair<Vector2Int, float>> sortedTiles = new List<KeyValuePair<Vector2Int, float>>(neededTiles);
        sortedTiles.Sort((a, b) => a.Value.CompareTo(b.Value));

        foreach (KeyValuePair<Vector2Int, float> t in sortedTiles)
        {
            if (built >= budget) break;
            if (tileObjects.ContainsKey(t.Key)) continue;
            tileObjects[t.Key] = BuildTile(t.Key);
            built++;
        }

        // Droga: potrzebne odcinki
        HashSet<int> neededRoads = new HashSet<int>();
        foreach (Focus f in focus)
        {
            foreach (int seg in SegmentsInZRange(f.position.z - f.radius, f.position.z + f.radius))
            {
                Vector3[] s = GetRoadSamples(seg);
                Vector3 mid = s[s.Length / 2];
                if (FlatDistance(mid, f.position) <= f.radius + segmentLength) neededRoads.Add(seg);
            }
        }

        foreach (int seg in neededRoads)
        {
            if (built >= budget) break;
            if (roadObjects.ContainsKey(seg)) continue;
            roadObjects[seg] = BuildRoad(seg);
            built++;
        }

        // Usuwanie dalekich kawałków terenu
        List<Vector2Int> tilesToRemove = new List<Vector2Int>();
        foreach (Vector2Int tile in tileObjects.Keys)
        {
            bool keep = false;
            foreach (Focus f in focus)
                if (DistanceToTile(f.position, tile) <= f.radius + margin) { keep = true; break; }
            if (!keep) tilesToRemove.Add(tile);
        }
        foreach (Vector2Int tile in tilesToRemove)
        {
            DestroyChunk(tileObjects[tile]);
            tileObjects.Remove(tile);
        }

        // Usuwanie dalekich odcinków drogi
        List<int> roadsToRemove = new List<int>();
        foreach (int seg in roadObjects.Keys)
        {
            Vector3[] s = GetRoadSamples(seg);
            Vector3 mid = s[s.Length / 2];
            bool keep = false;
            foreach (Focus f in focus)
                if (FlatDistance(mid, f.position) <= f.radius + segmentLength + margin) { keep = true; break; }
            if (!keep) roadsToRemove.Add(seg);
        }
        foreach (int seg in roadsToRemove)
        {
            DestroyChunk(roadObjects[seg]);
            roadObjects.Remove(seg);
            roadSamples.Remove(seg);
        }
    }

    void DestroyChunk(GameObject go)
    {
        // Usuwamy tylko siatki wygenerowane przez nas. Siatki z prefabów są wspólne i muszą zostać.
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Gen_"))
                Destroy(mf.sharedMesh);
        Destroy(go);
    }

    Vector2Int TileOf(Vector3 p)
    {
        return new Vector2Int(Mathf.FloorToInt(p.x / tileSize), Mathf.FloorToInt(p.z / tileSize));
    }

    float DistanceToTile(Vector3 p, Vector2Int tile)
    {
        float x0 = tile.x * tileSize, z0 = tile.y * tileSize;
        float dx = Mathf.Max(x0 - p.x, 0f, p.x - (x0 + tileSize));
        float dz = Mathf.Max(z0 - p.z, 0f, p.z - (z0 + tileSize));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ---------- Losowość (deterministyczna) ----------

    // Ta sama liczba dla tego samego seeda, indeksu i "soli". Wynik 0-1.
    float Hash(int index, int salt)
    {
        unchecked
        {
            uint h = (uint)seed * 374761393u + (uint)index * 668265263u + (uint)salt * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
    // Jak Hash, ale z trzech liczb (np. współrzędne kawałka i numer obiektu). Wynik 0-1.
    float Hash3(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u ^ (uint)a * 2246822519u
                   ^ (uint)b * 3266489917u ^ (uint)c * 668265263u;
            h = (h ^ (h >> 15)) * 2246822519u;
            h = (h ^ (h >> 13)) * 3266489917u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
    // ---------- Wysokość terenu ----------

    float NaturalHeight(float x, float z)
    {
        float h = (Mathf.PerlinNoise(x * hillScale + noiseOffset.x, z * hillScale + noiseOffset.y) - 0.5f) * 2f * hillHeight;
        h += (Mathf.PerlinNoise(x * detailScale + noiseOffset.y, z * detailScale + noiseOffset.x) - 0.5f) * 2f * detailHeight;

        // Płasko wokół miejsca startu
        float d = Mathf.Sqrt(x * x + z * z);
        float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(spawnFlatRadius, spawnFlatRadius * 3f, d));
        return h * k;
    }

    float TerrainHeight(float x, float z, List<Vector3> road)
    {
        float natural = NaturalHeight(x, z);
        if (road.Count < 2) return natural;

        NearestRoad(x, z, road, out float dist, out float roadY);

        // Pod drogą i tuż przy niej teren jest na wysokości drogi (trochę niżej), dalej wraca do naturalnej
        float roadBed = roadY - 0.2f;
        float t = Mathf.InverseLerp(roadHalfWidth + 1f, roadBlendDistance, dist);
        return Mathf.Lerp(roadBed, natural, Mathf.SmoothStep(0f, 1f, t));
    }

    static void NearestRoad(float x, float z, List<Vector3> road, out float dist, out float roadY)
    {
        Vector2 p = new Vector2(x, z);
        float best = float.MaxValue;
        roadY = 0f;

        for (int i = 0; i < road.Count - 1; i++)
        {
            Vector2 a = new Vector2(road[i].x, road[i].z);
            Vector2 ab = new Vector2(road[i + 1].x, road[i + 1].z) - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            float d = (a + ab * t - p).sqrMagnitude;

            if (d < best)
            {
                best = d;
                roadY = Mathf.Lerp(road[i].y, road[i + 1].y, t);
            }
        }
        dist = Mathf.Sqrt(best);
    }

    // ---------- Droga: punkty kontrolne i krzywa ----------

    Vector3 GetControlPoint(int i)
    {
        while (controlPoints.Count <= i + PointOffset) AddNextControlPoint();
        return controlPoints[i + PointOffset];
    }

    void AddNextControlPoint()
    {
        int i = controlPoints.Count - PointOffset;
        Vector2 flat;

        if (i <= straightStartSegments)
        {
            flat = new Vector2(0f, i * segmentLength);   // prosty początek
        }
        else
        {
            float turn = (Hash(i, 1) * 2f - 1f) * maxTurnPerSegment;
            heading = Mathf.Clamp(heading + turn, -maxHeading, maxHeading);

            Vector3 prev = controlPoints[controlPoints.Count - 1];
            float rad = heading * Mathf.Deg2Rad;
            flat = new Vector2(prev.x, prev.z) + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * segmentLength;
        }

        // Przy starcie droga jest dokładnie na wysokości 0
        float y = Mathf.Abs(i) <= 1 ? 0f : NaturalHeight(flat.x, flat.y);
        controlPoints.Add(new Vector3(flat.x, y, flat.y));
    }

    Vector3[] GetRoadSamples(int seg)
    {
        if (roadSamples.TryGetValue(seg, out Vector3[] cached)) return cached;

        Vector3 p0 = GetControlPoint(seg - 1), p1 = GetControlPoint(seg);
        Vector3 p2 = GetControlPoint(seg + 1), p3 = GetControlPoint(seg + 2);

        Vector3[] s = new Vector3[samplesPerSegment + 1];
        for (int k = 0; k <= samplesPerSegment; k++)
            s[k] = CatmullRom(p0, p1, p2, p3, (float)k / samplesPerSegment);

        roadSamples[seg] = s;
        return s;
    }

    // Odcinki drogi, które mogą leżeć w danym przedziale Z (droga zawsze idzie w stronę +Z)
    List<int> SegmentsInZRange(float zMin, float zMax)
    {
        List<int> result = new List<int>();
        for (int seg = FirstSegment; ; seg++)
        {
            Vector3 a = GetControlPoint(seg);
            Vector3 b = GetControlPoint(seg + 1);
            if (Mathf.Min(a.z, b.z) > zMax) break;
            if (Mathf.Max(a.z, b.z) >= zMin) result.Add(seg);
        }
        return result;
    }

    List<Vector3> CollectRoadPolyline(float zMin, float zMax)
    {
        List<Vector3> points = new List<Vector3>();
        foreach (int seg in SegmentsInZRange(zMin, zMax))
            points.AddRange(GetRoadSamples(seg));
        return points;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t
                       + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                       + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        return 0.5f * ((-p0 + p2)
                       + 2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t
                       + 3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t2);
    }

    // ---------- Budowanie siatek ----------

    GameObject BuildTile(Vector2Int tile)
    {
        float x0 = tile.x * tileSize, z0 = tile.y * tileSize;
        int res = tileResolution;
        float step = tileSize / res;

        List<Vector3> road = CollectRoadPolyline(z0 - roadBlendDistance - step,
                                                 z0 + tileSize + roadBlendDistance + step);

        // Wysokości z ramką szerszą o 1 punkt: normalne liczone z sąsiadów = brak szwów między kawałkami
        int g = res + 3;
        float[,] h = new float[g, g];
        for (int iz = 0; iz < g; iz++)
            for (int ix = 0; ix < g; ix++)
                h[ix, iz] = TerrainHeight(x0 + (ix - 1) * step, z0 + (iz - 1) * step, road);

        int row = res + 1;
        Vector3[] verts = new Vector3[row * row];
        Vector3[] normals = new Vector3[row * row];
        Vector2[] uvs = new Vector2[row * row];

        for (int iz = 0; iz <= res; iz++)
            for (int ix = 0; ix <= res; ix++)
            {
                int gx = ix + 1, gz = iz + 1;
                int v = iz * row + ix;
                verts[v] = new Vector3(ix * step, h[gx, gz], iz * step);
                normals[v] = new Vector3(h[gx - 1, gz] - h[gx + 1, gz], 2f * step, h[gx, gz - 1] - h[gx, gz + 1]).normalized;
                uvs[v] = new Vector2(x0 + ix * step, z0 + iz * step) * 0.1f;
            }

        int[] tris = new int[res * res * 6];
        int ti = 0;
        for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                int a = iz * row + ix, b = a + 1, c = a + row, d = c + 1;
                tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
            }

        GameObject go = CreateMeshObject($"Teren {tile.x},{tile.y}", new Vector3(x0, 0f, z0),
                                         verts, uvs, tris, terrainMaterial, true, normals);
        go.AddComponent<Surface>().type = SurfaceType.Grass;
        ScatterNature(tile, go.transform, road);
        return go;
    }

    GameObject BuildRoad(int seg)
    {
        Vector3[] s = GetRoadSamples(seg);
        Vector3 p0 = GetControlPoint(seg - 1), p1 = GetControlPoint(seg);
        Vector3 p2 = GetControlPoint(seg + 1), p3 = GetControlPoint(seg + 2);

        int n = s.Length;
        Vector3 origin = s[0];
        const float shoulder = 0.8f;   // pobocze
        const float drop = 0.25f;      // pobocze schodzi w dół pod teren
        const float lineHalf = 0.08f;

        // Na każdą próbkę 4 punkty: lewe pobocze, lewa krawędź, prawa krawędź, prawe pobocze
        Vector3[] verts = new Vector3[n * 4];
        Vector2[] uvs = new Vector2[n * 4];
        Vector3[] rights = new Vector3[n];
        List<int> tris = new List<int>();
        float dist = 0f;

        for (int k = 0; k < n; k++)
        {
            if (k > 0) dist += Vector3.Distance(s[k], s[k - 1]);

            // Kierunek z pochodnej krzywej, żeby kolejne odcinki łączyły się bez szczelin
            Vector3 fwd = CatmullRomTangent(p0, p1, p2, p3, (float)k / (n - 1));
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            rights[k] = right;

            Vector3 c = s[k] - origin;
            verts[k * 4 + 0] = c - right * (roadHalfWidth + shoulder) + Vector3.down * drop;
            verts[k * 4 + 1] = c - right * roadHalfWidth;
            verts[k * 4 + 2] = c + right * roadHalfWidth;
            verts[k * 4 + 3] = c + right * (roadHalfWidth + shoulder) + Vector3.down * drop;

            float v = dist / (roadHalfWidth * 2f);
            uvs[k * 4 + 0] = new Vector2(0f, v);
            uvs[k * 4 + 1] = new Vector2(0.1f, v);
            uvs[k * 4 + 2] = new Vector2(0.9f, v);
            uvs[k * 4 + 3] = new Vector2(1f, v);

            if (k < n - 1)
            {
                for (int j = 0; j < 3; j++)
                {
                    int a = k * 4 + j, b = a + 1, cc = a + 4, d = cc + 1;
                    tris.Add(a); tris.Add(cc); tris.Add(b);
                    tris.Add(b); tris.Add(cc); tris.Add(d);
                }
            }
        }

        GameObject go = CreateMeshObject($"Droga {seg}", origin, verts, uvs, tris.ToArray(), roadMaterial, true, null);
        go.AddComponent<Surface>().type = SurfaceType.Asphalt;

        // Przerywana linia środkowa: co drugi odstęp między próbkami
        List<Vector3> lineVerts = new List<Vector3>();
        List<int> lineTris = new List<int>();
        Vector3 up = Vector3.up * 0.02f;
        for (int k = 0; k < n - 1; k += 2)
        {
            int start = lineVerts.Count;
            lineVerts.Add(s[k] - origin - rights[k] * lineHalf + up);
            lineVerts.Add(s[k] - origin + rights[k] * lineHalf + up);
            lineVerts.Add(s[k + 1] - origin - rights[k + 1] * lineHalf + up);
            lineVerts.Add(s[k + 1] - origin + rights[k + 1] * lineHalf + up);
            lineTris.Add(start); lineTris.Add(start + 2); lineTris.Add(start + 1);
            lineTris.Add(start + 1); lineTris.Add(start + 2); lineTris.Add(start + 3);
        }

        GameObject line = CreateMeshObject("Linia", origin, lineVerts.ToArray(), null,
                                           lineTris.ToArray(), lineMaterial, false, null);
        line.transform.SetParent(go.transform, true);
        DecorateRoad(seg, go.transform, s, rights);
        return go;
    }

    GameObject CreateMeshObject(string name, Vector3 position, Vector3[] verts, Vector2[] uvs, int[] tris,
                                Material material, bool withCollider, Vector3[] normals)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Gen_" + name; // znacznik: tę siatkę wolno usunąć razem z kawałkiem
        mesh.vertices = verts;
        if (uvs != null) mesh.uv = uvs;
        mesh.triangles = tris;
        if (normals != null) mesh.normals = normals;
        else mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (withCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }
    // ---------- Przyroda i obiekty przy drodze ----------

    void ScatterNature(Vector2Int tile, Transform parent, List<Vector3> road)
    {
        if (nature == null || nature.Length == 0) return;

        float totalWeight = 0f;
        foreach (ScatterItem item in nature)
            if (item.prefab != null) totalWeight += item.weight;
        if (totalWeight <= 0f) return;

        float x0 = tile.x * tileSize, z0 = tile.y * tileSize;
        int count = Mathf.RoundToInt(natureDensity * (tileSize * tileSize) / 10000f);

        for (int i = 0; i < count; i++)
        {
            // Pięć "losowych" liczb na obiekt, zawsze takich samych dla tego kawałka
            float x = x0 + Hash3(tile.x, tile.y, i * 5) * tileSize;
            float z = z0 + Hash3(tile.x, tile.y, i * 5 + 1) * tileSize;
            ScatterItem item = PickItem(Hash3(tile.x, tile.y, i * 5 + 2) * totalWeight);
            if (item == null) continue;

            // Plac startowy zostaje pusty
            if (Mathf.Sqrt(x * x + z * z) < spawnFlatRadius) continue;

            // Nie na drodze ani tuż przy niej
            if (road.Count >= 2)
            {
                NearestRoad(x, z, road, out float dist, out _);
                if (dist < item.minRoadDistance) continue;
            }

            float y = TerrainHeight(x, z, road) - 0.05f; // lekko w ziemi, żeby nie "wisiało"
            float scale = Mathf.Lerp(item.minScale, item.maxScale, Hash3(tile.x, tile.y, i * 5 + 3));
            Quaternion rot = Quaternion.Euler(0f, Hash3(tile.x, tile.y, i * 5 + 4) * 360f, 0f);

            GameObject obj = Instantiate(item.prefab, new Vector3(x, y, z), rot, parent);
            obj.transform.localScale *= scale;
        }
    }

    ScatterItem PickItem(float r)
    {
        foreach (ScatterItem item in nature)
        {
            if (item.prefab == null) continue;
            if (r < item.weight) return item;
            r -= item.weight;
        }
        return null;
    }

    void DecorateRoad(int seg, Transform parent, Vector3[] s, Vector3[] rights)
    {
        List<Vector3> road = CollectRoadPolyline(s[0].z - 40f, s[s.Length - 1].z + 40f);

        // Słupy po prawej stronie. Ostatnia próbka należy już do następnego odcinka.
        if (polePrefab != null)
        {
            for (int k = 0; k < s.Length - 1; k += poleEverySamples)
                PlaceBeside(polePrefab, s[k], rights[k], false, parent, road);
        }

        // Znak kilometrowy po lewej, na początku co któregoś odcinka
        if (kmSignPrefab != null && seg >= 0 && seg % kmSignEverySegments == 0)
        {
            GameObject sign = PlaceBeside(kmSignPrefab, s[0], rights[0], true, parent, road);
            TMPro.TextMeshPro text = sign.GetComponentInChildren<TMPro.TextMeshPro>();
            if (text != null) text.text = (seg / kmSignEverySegments).ToString();
        }
    }

    GameObject PlaceBeside(GameObject prefab, Vector3 roadPoint, Vector3 right, bool leftSide,
                           Transform parent, List<Vector3> road)
    {
        Vector3 side = leftSide ? -right : right;
        Vector3 p = roadPoint + side * (roadHalfWidth + roadsideOffset);
        p.y = TerrainHeight(p.x, p.z, road) - 0.05f;

        // Kierunek jazdy (z wektora "w prawo"). Obiekt patrzy w tę stronę,
        // więc znak jest czytelny dla nadjeżdżającego kierowcy.
        Vector3 roadForward = Vector3.Cross(right, Vector3.up);
        return Instantiate(prefab, p, Quaternion.LookRotation(roadForward), parent);
    }
}