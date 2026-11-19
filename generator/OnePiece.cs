using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// One-piece chip for AMS printing (no gluing):
//   z 0.0-0.4 : back design as a flat colour inlay on the bottom face (printed on the bed)
//   z 0.4-2.8 : orange core (edge stripes run the full height)
//   z 2.8-3.2 : front design in relief (white ring 2.8-3.0)
public static class OnePiece
{
    const int N = CoinGen.N;
    public const float ZB = 0.4f, ZC = 2.8f, ZT = 3.2f, ZR = 3.0f;
    public static readonly string[] Names = { "orange", "white", "black", "navy" };
    public static readonly string[] Hex = { "#F28A1E", "#F4F2EC", "#262626", "#1F3A8F" };

    class Layers
    {
        public float[] alo = new float[N * N], ahi = new float[N * N], blo = new float[N * N], bhi = new float[N * N];
        public void Put(int k, float lo, float hi)
        {
            if (!(ahi[k] > alo[k])) { alo[k] = lo; ahi[k] = hi; }
            else if (Math.Abs(ahi[k] - lo) < 1e-6) ahi[k] = hi;
            else if (!(bhi[k] > blo[k])) { blo[k] = lo; bhi[k] = hi; }
            else if (Math.Abs(bhi[k] - lo) < 1e-6) bhi[k] = hi;
            else throw new Exception("more than two intervals in a column");
        }
        public List<float[]> At(int k)
        {
            var l = new List<float[]>();
            if (ahi[k] > alo[k]) l.Add(new float[] { alo[k], ahi[k] });
            if (bhi[k] > blo[k]) l.Add(new float[] { blo[k], bhi[k] });
            return l;
        }
    }

    public class Mesh
    {
        public List<float> V = new List<float>();
        public List<int> T = new List<int>();
        Dictionary<long, int> idx = new Dictionary<long, int>();
        int Vid(float x, float y, float z)
        {
            long kx = (long)Math.Round((x + 50) * 1000), ky = (long)Math.Round((y + 50) * 1000), kz = (long)Math.Round((z + 50) * 1000);
            long key = (kx * 200000L + ky) * 200000L + kz;
            int id;
            if (!idx.TryGetValue(key, out id)) { id = V.Count / 3; V.Add(x); V.Add(y); V.Add(z); idx[key] = id; }
            return id;
        }
        public void Tri(float[] a, float[] b, float[] c) { T.Add(Vid(a[0], a[1], a[2])); T.Add(Vid(b[0], b[1], b[2])); T.Add(Vid(c[0], c[1], c[2])); }
        public void Quad(float[] a, float[] b, float[] c, float[] d) { Tri(a, b, c); Tri(a, c, d); }
        public int Tris { get { return T.Count / 3; } }
    }

    static List<float[]> Subtract(float a, float b, List<float[]> nb)
    {
        var segs = new List<float[]> { new float[] { a, b } };
        foreach (var n in nb)
        {
            var o = new List<float[]>();
            foreach (var s in segs)
            {
                if (n[1] <= s[0] || n[0] >= s[1]) { o.Add(s); continue; }
                if (n[0] > s[0]) o.Add(new float[] { s[0], n[0] });
                if (n[1] < s[1]) o.Add(new float[] { n[1], s[1] });
            }
            segs = o;
        }
        return segs;
    }

    static Mesh Build(Layers L)
    {
        var m = new Mesh();
        double P = CoinGen.P, R = CoinGen.R;
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                int k = j * N + i;
                var own = L.At(k); if (own.Count == 0) continue;
                float x0 = (float)(i * P - R), x1 = (float)((i + 1) * P - R), y0 = (float)(j * P - R), y1 = (float)((j + 1) * P - R);
                foreach (var s in own)
                {
                    float a = s[0], b = s[1];
                    m.Quad(new[] { x0, y0, b }, new[] { x1, y0, b }, new[] { x1, y1, b }, new[] { x0, y1, b });
                    m.Quad(new[] { x0, y0, a }, new[] { x0, y1, a }, new[] { x1, y1, a }, new[] { x1, y0, a });
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = i + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        var nb = (ni >= 0 && nj >= 0 && ni < N && nj < N) ? L.At(nj * N + ni) : new List<float[]>();
                        foreach (var g in Subtract(a, b, nb))
                        {
                            float za = g[0], zb = g[1];
                            if (zb - za < 1e-5f) continue;
                            if (d == 0) m.Quad(new[] { x1, y0, za }, new[] { x1, y1, za }, new[] { x1, y1, zb }, new[] { x1, y0, zb });
                            else if (d == 1) m.Quad(new[] { x0, y0, za }, new[] { x0, y0, zb }, new[] { x0, y1, zb }, new[] { x0, y1, za });
                            else if (d == 2) m.Quad(new[] { x1, y1, za }, new[] { x0, y1, za }, new[] { x0, y1, zb }, new[] { x1, y1, zb });
                            else m.Quad(new[] { x1, y0, za }, new[] { x1, y0, zb }, new[] { x0, y0, zb }, new[] { x0, y0, za });
                        }
                    }
                }
            }
        return m;
    }

    // colour of the (half-design) surface at cell k for a given design map
    static int Surface(int[] design, int k, double x, double y, out bool ridge)
    {
        double r = Math.Sqrt(x * x + y * y); ridge = false;
        if (r >= Chip.RIDGE)
        {
            ridge = true;
            double a = Math.Atan2(y, x) * 180 / Math.PI;
            double rel = ((a - 22.5) % 45 + 45) % 45; rel = Math.Min(rel, 45 - rel);
            return rel <= Chip.STRIPE_DEG / 2 ? 1 : 0;
        }
        if (r >= Chip.RING) return 1;
        return design[k] >= 0 ? design[k] : 0;
    }

    public static Mesh[] Make(int[] front, int[] back)
    {
        var L = new Layers[4]; for (int c = 0; c < 4; c++) L[c] = new Layers();
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                int k = j * N + i; double x = CoinGen.Cx(i), y = CoinGen.Cy(j);
                if (x * x + y * y > Chip.R * Chip.R) continue;
                bool ridge;
                // bottom face is seen from below -> mirror the back design in x
                int kb = j * N + (N - 1 - i);
                int cb = Surface(back, kb, -x, y, out ridge);
                if (ridge) { L[cb].Put(k, 0, ZT); continue; }   // edge stripes: full height
                L[cb].Put(k, 0, ZB);
                L[0].Put(k, ZB, ZC);
                double r = Math.Sqrt(x * x + y * y);
                if (r >= Chip.RING) { L[1].Put(k, ZC, ZR); continue; }
                if (front[k] >= 0) L[front[k]].Put(k, ZC, ZT);
            }
        var meshes = new Mesh[4];
        for (int c = 0; c < 4; c++) meshes[c] = Build(L[c]);
        return meshes;
    }

    public static void WriteStl(string path, params Mesh[] ms)
    {
        using (var bw = new BinaryWriter(File.Create(path)))
        {
            bw.Write(new byte[80]); int n = 0; foreach (var m in ms) n += m.Tris; bw.Write((uint)n);
            foreach (var m in ms)
                for (int t = 0; t < m.T.Count; t += 3)
                {
                    int a = m.T[t] * 3, b = m.T[t + 1] * 3, c = m.T[t + 2] * 3;
                    float ux = m.V[b] - m.V[a], uy = m.V[b + 1] - m.V[a + 1], uz = m.V[b + 2] - m.V[a + 2];
                    float vx = m.V[c] - m.V[a], vy = m.V[c + 1] - m.V[a + 1], vz = m.V[c + 2] - m.V[a + 2];
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l > 0) { nx /= l; ny /= l; nz /= l; }
                    bw.Write(nx); bw.Write(ny); bw.Write(nz);
                    foreach (int v in new[] { a, b, c }) { bw.Write(m.V[v]); bw.Write(m.V[v + 1]); bw.Write(m.V[v + 2]); }
                    bw.Write((ushort)0);
                }
        }
    }

    // 3MF with one object made of 4 parts; Bambu Studio / OrcaSlicer read the per-part filament
    // from Metadata/model_settings.config.
    public static void Write3mf(string path, string name, Mesh[] ms)
    {
        if (File.Exists(path)) File.Delete(path);
        var ci = CultureInfo.InvariantCulture;
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Action<string, string> add = (n, s) =>
            {
                var e = zip.CreateEntry(n, CompressionLevel.Optimal);
                using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(s);
            };
            add("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/>" +
                "<Default Extension=\"config\" ContentType=\"text/xml\"/></Types>");
            add("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>");

            var e3 = zip.CreateEntry("3D/3dmodel.model", CompressionLevel.Optimal);
            using (var w = new StreamWriter(e3.Open(), new UTF8Encoding(false)))
            {
                w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<model unit=\"millimeter\" xml:lang=\"en-US\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\" xmlns:BambuStudio=\"http://schemas.bambulab.com/package/2021\">\n");
                w.Write(" <metadata name=\"Application\">BambuStudio-02.00.00.00</metadata>\n <metadata name=\"BambuStudio:3mfVersion\">1</metadata>\n <metadata name=\"Title\">" + name + "</metadata>\n <resources>\n");
                w.Write("  <basematerials id=\"100\">");
                for (int c = 0; c < 4; c++) w.Write("<base name=\"" + Names[c] + "\" displaycolor=\"" + Hex[c] + "\"/>");
                w.Write("</basematerials>\n");
                int oid = 1; var ids = new List<int>();
                for (int c = 0; c < ms.Length; c++)
                {
                    var m = ms[c]; if (m.Tris == 0) continue;
                    ids.Add(oid);
                    w.Write("  <object id=\"" + oid + "\" name=\"" + Names[c] + "\" type=\"model\" pid=\"100\" pindex=\"" + c + "\">\n   <mesh>\n    <vertices>\n");
                    for (int v = 0; v < m.V.Count; v += 3)
                        w.Write("     <vertex x=\"" + m.V[v].ToString("0.###", ci) + "\" y=\"" + m.V[v + 1].ToString("0.###", ci) + "\" z=\"" + m.V[v + 2].ToString("0.###", ci) + "\"/>\n");
                    w.Write("    </vertices>\n    <triangles>\n");
                    for (int t = 0; t < m.T.Count; t += 3)
                        w.Write("     <triangle v1=\"" + m.T[t] + "\" v2=\"" + m.T[t + 1] + "\" v3=\"" + m.T[t + 2] + "\"/>\n");
                    w.Write("    </triangles>\n   </mesh>\n  </object>\n");
                    oid++;
                }
                int parent = oid;
                w.Write("  <object id=\"" + parent + "\" name=\"" + name + "\" type=\"model\">\n   <components>\n");
                foreach (int id in ids) w.Write("    <component objectid=\"" + id + "\"/>\n");
                w.Write("   </components>\n  </object>\n </resources>\n <build>\n");
                w.Write("  <item objectid=\"" + parent + "\" transform=\"1 0 0 0 1 0 0 0 1 128 128 0\" printable=\"1\"/>\n </build>\n</model>\n");

                // slicer part settings (extruder = AMS slot)
                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<config>\n  <object id=\"" + parent + "\">\n");
                sb.Append("    <metadata key=\"name\" value=\"" + name + "\"/>\n    <metadata key=\"extruder\" value=\"1\"/>\n");
                int pi = 0;
                for (int c = 0; c < ms.Length; c++)
                {
                    if (ms[c].Tris == 0) continue;
                    sb.Append("    <part id=\"" + ids[pi] + "\" subtype=\"normal_part\">\n      <metadata key=\"name\" value=\"" + Names[c] + "\"/>\n      <metadata key=\"matrix\" value=\"1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1\"/>\n");
                    sb.Append("      <metadata key=\"extruder\" value=\"" + (c + 1) + "\"/>\n    </part>\n");
                    pi++;
                }
                sb.Append("  </object>\n</config>\n");
                bambu = sb.ToString();


            }
            add("Metadata/model_settings.config", bambu);
        }
    }
    static string bambu;
}
