using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 정이십면체를 분할해 구를 만든다.
///
/// Unity 기본 Sphere는 UV 구라 극점에 정점이 몰린다. 소프트바디로 쓰면
/// 극점만 과하게 출렁이고 적도는 둔해져서 변형이 고르지 않다.
/// 이코스피어는 정점이 고르게 퍼져 있어 스프링 물리에 적합하다.
/// </summary>
public static class IcoSphere
{
    /// <summary>
    /// subdivisions별 정점 수: 0=12, 1=42, 2=162, 3=642, 4=2562.
    /// 실험에는 2(162개)면 충분하고 가볍다.
    /// </summary>
    public static Mesh Create(int subdivisions, float radius = 1f)
    {
        subdivisions = Mathf.Clamp(subdivisions, 0, 5);

        var verts = new List<Vector3>();
        var tris = new List<int>();
        var midCache = new Dictionary<long, int>();

        // 정이십면체 — 황금비로 만든 세 직사각형의 꼭짓점
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

        void AddV(float x, float y, float z) => verts.Add(new Vector3(x, y, z).normalized);

        AddV(-1,  t,  0); AddV( 1,  t,  0); AddV(-1, -t,  0); AddV( 1, -t,  0);
        AddV( 0, -1,  t); AddV( 0,  1,  t); AddV( 0, -1, -t); AddV( 0,  1, -t);
        AddV( t,  0, -1); AddV( t,  0,  1); AddV(-t,  0, -1); AddV(-t,  0,  1);

        int[] baseTris = {
            0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
            1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
            3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
            4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
        };
        tris.AddRange(baseTris);

        // 각 삼각형을 넷으로 쪼개고, 새 정점을 구면으로 밀어낸다.
        for (int s = 0; s < subdivisions; s++)
        {
            var next = new List<int>(tris.Count * 4);
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = MidPoint(a, b, verts, midCache);
                int bc = MidPoint(b, c, verts, midCache);
                int ca = MidPoint(c, a, verts, midCache);

                next.AddRange(new[] { a, ab, ca,  b, bc, ab,  c, ca, bc,  ab, bc, ca });
            }
            tris = next;
        }

        var mesh = new Mesh { name = $"IcoSphere_{subdivisions}" };
        if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        var scaled = new Vector3[verts.Count];
        var normals = new Vector3[verts.Count];
        var uvs = new Vector2[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            var n = verts[i];
            scaled[i] = n * radius;
            normals[i] = n;
            // 구면 좌표 UV. 이음새에서 한 줄 늘어나지만 실험에는 지장 없다.
            uvs[i] = new Vector2(
                0.5f + Mathf.Atan2(n.z, n.x) / (2f * Mathf.PI),
                0.5f - Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) / Mathf.PI);
        }

        mesh.vertices = scaled;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>두 정점의 중점을 만들되, 공유 모서리는 한 번만 만든다.</summary>
    static int MidPoint(int a, int b, List<Vector3> verts, Dictionary<long, int> cache)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int existing)) return existing;

        var mid = ((verts[a] + verts[b]) * 0.5f).normalized;
        verts.Add(mid);
        int index = verts.Count - 1;
        cache[key] = index;
        return index;
    }

    /// <summary>삼각형 목록에서 중복 없는 모서리를 뽑는다. 스프링 연결에 쓴다.</summary>
    public static List<(int a, int b)> BuildEdges(int[] triangles)
    {
        var seen = new HashSet<long>();
        var edges = new List<(int, int)>();

        void TryAdd(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (seen.Add(key)) edges.Add((a, b));
        }

        for (int i = 0; i < triangles.Length; i += 3)
        {
            TryAdd(triangles[i], triangles[i + 1]);
            TryAdd(triangles[i + 1], triangles[i + 2]);
            TryAdd(triangles[i + 2], triangles[i]);
        }
        return edges;
    }
}
