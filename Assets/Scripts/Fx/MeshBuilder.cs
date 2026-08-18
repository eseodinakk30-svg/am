// -----------------------------------------------------------------------------
//  NEBULA NINE - lightweight procedural mesh assembler.
//  Used to bake the whole station floor/wall geometry into a couple of meshes so
//  the map costs 2-6 draw calls instead of thousands of separate objects.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Fx
{
    public class MeshBuilder
    {
        private readonly List<Vector3> _verts = new List<Vector3>(4096);
        private readonly List<Vector3> _normals = new List<Vector3>(4096);
        private readonly List<Vector2> _uvs = new List<Vector2>(4096);
        private readonly List<Color> _colors = new List<Color>(4096);
        private readonly List<int> _tris = new List<int>(8192);

        public int VertexCount => _verts.Count;

        public void Clear()
        {
            _verts.Clear(); _normals.Clear(); _uvs.Clear(); _colors.Clear(); _tris.Clear();
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color, Vector2 uvScale)
        {
            int i = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            for (int k = 0; k < 4; k++) { _normals.Add(normal); _colors.Add(color); }
            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(uvScale.x, 0));
            _uvs.Add(new Vector2(uvScale.x, uvScale.y));
            _uvs.Add(new Vector2(0, uvScale.y));
            _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 1);
            _tris.Add(i); _tris.Add(i + 3); _tris.Add(i + 2);
        }

        /// <summary>Horizontal slab (floor / ceiling) spanning the given XZ rectangle.</summary>
        public void AddFloor(float x0, float z0, float x1, float z1, float y, Color color, bool faceUp = true)
        {
            var n = faceUp ? Vector3.up : Vector3.down;
            var a = new Vector3(x0, y, z0);
            var b = new Vector3(x1, y, z0);
            var c = new Vector3(x1, y, z1);
            var d = new Vector3(x0, y, z1);
            var uv = new Vector2(Mathf.Abs(x1 - x0) * 0.25f, Mathf.Abs(z1 - z0) * 0.25f);
            if (faceUp) AddQuad(a, b, c, d, n, color, uv);
            else AddQuad(d, c, b, a, n, color, uv);
        }

        /// <summary>Vertical wall panel from (x0,z0) to (x1,z1), height h, double sided.</summary>
        public void AddWall(float x0, float z0, float x1, float z1, float y0, float height, Color color)
        {
            var p0 = new Vector3(x0, y0, z0);
            var p1 = new Vector3(x1, y0, z1);
            var p2 = new Vector3(x1, y0 + height, z1);
            var p3 = new Vector3(x0, y0 + height, z0);
            var dir = (p1 - p0).normalized;
            var n = Vector3.Cross(Vector3.up, dir).normalized;
            var uv = new Vector2(Vector3.Distance(p0, p1) * 0.35f, height * 0.35f);
            AddQuad(p0, p1, p2, p3, n, color, uv);
            AddQuad(p1, p0, p3, p2, -n, color, uv);
        }

        public void AddBox(Vector3 center, Vector3 size, Color color)
        {
            var h = size * 0.5f;
            float x0 = center.x - h.x, x1 = center.x + h.x;
            float y0 = center.y - h.y, y1 = center.y + h.y;
            float z0 = center.z - h.z, z1 = center.z + h.z;
            var uvx = new Vector2(size.x * 0.4f, size.y * 0.4f);
            var uvz = new Vector2(size.z * 0.4f, size.y * 0.4f);
            var uvy = new Vector2(size.x * 0.4f, size.z * 0.4f);

            AddQuad(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.up, color, uvy);
            AddQuad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), Vector3.down, color, uvy);
            AddQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, color, uvx);
            AddQuad(new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), Vector3.forward, color, uvx);
            AddQuad(new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), Vector3.left, color, uvz);
            AddQuad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right, color, uvz);
        }

        public Mesh Build(string name, bool markNoLongerReadable = true)
        {
            var mesh = new Mesh { name = name };
            if (_verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (markNoLongerReadable) mesh.UploadMeshData(false);
            return mesh;
        }
    }
}
