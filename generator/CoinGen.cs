using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public static class CoinGen
{
    // Coin geometry (mm)
    public const int N = 320;          // grid cells per side
    public const double P = 0.125;     // cell size (mm) -> 40mm
    public const double R = 20.0;      // coin radius
    public const double RIN = 18.8;    // inner (design) radius; rim = RIN..R

    // ---------- image ----------
    public static byte[] LoadGray(string path, out int w, out int h)
    {
        using (var s = File.OpenRead(path))
        {
            var dec = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var c = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Gray8, null, 0);
            w = c.PixelWidth; h = c.PixelHeight;
            var buf = new byte[w * h];
            c.CopyPixels(buf, w, 0);
            return buf;
        }
    }

    // Fit axis-aligned ellipse to the dark inner disc boundary of the coin face in the image.
    public static double[] FitDarkEllipse(byte[] g, int w, int h, double cx0, double cy0, double rStart, int darkThr)
    {
        var pts = new List<double[]>();
        for (int a = 0; a < 360; a += 3)
        {
            double t = a * Math.PI / 180, dx = Math.Cos(t), dy = Math.Sin(t);
            int run = 0;
            for (double r = rStart; r > 50; r -= 0.5)
            {
                int x = (int)Math.Round(cx0 + dx * r), y = (int)Math.Round(cy0 + dy * r);
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                if (g[y * w + x] < darkThr) { run++; if (run >= 4) { pts.Add(new double[] { cx0 + dx * (r + 1.5), cy0 + dy * (r + 1.5) }); break; } }
                else run = 0;
            }
        }
        var el = FitPts(pts);
        for (int it = 0; it < 3; it++)
        {
            var keep = new List<double[]>();
            foreach (var p in pts)
            {
                double u = (p[0] - el[0]) / el[2], v = (p[1] - el[1]) / el[3];
                double rr = Math.Sqrt(u * u + v * v);
                if (rr > 0.985 && rr < 1.015) keep.Add(p);
            }
            if (keep.Count < 20) break;
            pts = keep; el = FitPts(pts);
        }
        return el;
    }

    static double[] FitPts(List<double[]> pts)
    {
        // least squares: A x^2 + B y^2 + C x + D y = 1
        var M = new double[4, 5];
        foreach (var p in pts)
        {
            double[] row = { p[0] * p[0], p[1] * p[1], p[0], p[1] };
            for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) M[i, j] += row[i] * row[j]; M[i, 4] += row[i]; }
        }
        var sol = Solve(M, 4);
        double A = sol[0], B = sol[1], C = sol[2], D = sol[3];
        double cx = -C / (2 * A), cy = -D / (2 * B);
        double G = 1 + C * C / (4 * A) + D * D / (4 * B);
        return new double[] { cx, cy, Math.Sqrt(G / A), Math.Sqrt(G / B), pts.Count };
    }

    static double[] Solve(double[,] M, int n)
    {
        for (int i = 0; i < n; i++)
        {
            int piv = i;
            for (int r = i + 1; r < n; r++) if (Math.Abs(M[r, i]) > Math.Abs(M[piv, i])) piv = r;
            for (int c = 0; c <= n; c++) { double t = M[i, c]; M[i, c] = M[piv, c]; M[piv, c] = t; }
            for (int r = 0; r < n; r++)
            {
                if (r == i) continue;
                double f = M[r, i] / M[i, i];
                for (int c = i; c <= n; c++) M[r, c] -= f * M[i, c];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = M[i, n] / M[i, i];
        return x;
    }

    public static double Sample(byte[] g, int w, int h, double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        Func<int, int, double> at = (xx, yy) =>
        {
            xx = Math.Max(0, Math.Min(w - 1, xx)); yy = Math.Max(0, Math.Min(h - 1, yy));
            return g[yy * w + xx];
        };
        return at(x0, y0) * (1 - fx) * (1 - fy) + at(x0 + 1, y0) * fx * (1 - fy) + at(x0, y0 + 1) * (1 - fx) * fy + at(x0 + 1, y0 + 1) * fx * fy;
    }

    // ---------- grid helpers ----------
    public static double Cx(int i) { return (i + 0.5) * P - R; }
    public static double Cy(int j) { return (j + 0.5) * P - R; }   // j=0 is bottom (y=-R)

    public static bool[] Disc()
    {
        var m = new bool[N * N];
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            { double x = Cx(i), y = Cy(j); m[j * N + i] = x * x + y * y <= R * R; }
        return m;
    }

    public static bool[] Rim()
    {
        var m = new bool[N * N];
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            { double x = Cx(i), y = Cy(j), r2 = x * x + y * y; m[j * N + i] = r2 <= R * R && r2 >= RIN * RIN; }
        return m;
    }

    public static bool[] FrontFromImage(byte[] g, int w, int h, double[] el, int thr)
    {
        var m = new bool[N * N];
        double inset = 0.985; // ignore bevel at the very edge of the dark disc
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                double u = Cx(i) / RIN, v = Cy(j) / RIN;
                if (u * u + v * v >= inset * inset) continue;
                // 3x3 supersample
                double acc = 0;
                for (int sy = -1; sy <= 1; sy++) for (int sx = -1; sx <= 1; sx++)
                    {
                        double uu = (Cx(i) + sx * P / 3) / RIN, vv = (Cy(j) + sy * P / 3) / RIN;
                        acc += Sample(g, w, h, el[0] + uu * el[2], el[1] - vv * el[3]);
                    }
                m[j * N + i] = acc / 9 > thr;
            }
        return m;
    }

    public static bool[] Majority(bool[] m, int iters)
    {
        for (int k = 0; k < iters; k++)
        {
            var o = new bool[N * N];
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
                {
                    int c = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        {
                            int x = i + dx, y = j + dy;
                            if (x >= 0 && y >= 0 && x < N && y < N && m[y * N + x]) c++;
                        }
                    o[j * N + i] = c >= 5;
                }
            m = o;
        }
        return m;
    }

    // remove connected components (value==val) smaller than minSize, flipping them
    public static bool[] RemoveSmall(bool[] m, bool val, int minSize, bool[] limit)
    {
        var seen = new bool[N * N];
        var o = (bool[])m.Clone();
        var stack = new Stack<int>();
        var comp = new List<int>();
        for (int s = 0; s < N * N; s++)
        {
            if (seen[s] || m[s] != val || !limit[s]) continue;
            comp.Clear(); stack.Push(s); seen[s] = true;
            while (stack.Count > 0)
            {
                int p = stack.Pop(); comp.Add(p);
                int x = p % N, y = p / N;
                int[] nb = { x > 0 ? p - 1 : -1, x < N - 1 ? p + 1 : -1, y > 0 ? p - N : -1, y < N - 1 ? p + N : -1 };
                foreach (int q in nb) if (q >= 0 && !seen[q] && m[q] == val && limit[q]) { seen[q] = true; stack.Push(q); }
            }
            if (comp.Count < minSize) foreach (int p in comp) o[p] = !val;
        }
        return o;
    }

    // Back face: QR (modules raised when inverted=true, mockup style) + centre "X"
    public static bool[] BackQr(bool[,] qr, double module, bool inverted, int clearHalf)
    {
        int n = qr.GetLength(0);
        var m = new bool[N * N];
        double half = n * module / 2;
        int c0 = n / 2 - clearHalf, c1 = n / 2 + clearHalf;
        double xs = (clearHalf + 0.5) * module - 0.35; // X extent
        double hw = 0.42;                              // half bar width of X
        double quiet = half + 2 * module;
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                double x = Cx(i), y = Cy(j);
                bool inX = Math.Abs(x) <= xs && Math.Abs(y) <= xs &&
                           (Math.Abs(y - x) / Math.Sqrt(2) <= hw || Math.Abs(y + x) / Math.Sqrt(2) <= hw);
                bool dark = false, cleared = false;
                if (Math.Abs(x) < half && Math.Abs(y) < half)
                {
                    int col = (int)Math.Floor((x + half) / module);
                    int row = (int)Math.Floor((half - y) / module);
                    col = Math.Min(n - 1, col); row = Math.Min(n - 1, row);
                    cleared = col >= c0 && col <= c1 && row >= c0 && row <= c1;
                    dark = qr[row, col] && !cleared;
                }
                if (inverted) m[j * N + i] = dark || inX;
                else m[j * N + i] = Math.Abs(x) < quiet && Math.Abs(y) < quiet && !dark && !inX;
            }
        return m;
    }

    public static bool[] Or(bool[] a, bool[] b) { var o = new bool[N * N]; for (int k = 0; k < o.Length; k++) o[k] = a[k] || b[k]; return o; }
    public static bool[] MirrorX(bool[] a) { var o = new bool[N * N]; for (int j = 0; j < N; j++) for (int i = 0; i < N; i++) o[j * N + i] = a[j * N + (N - 1 - i)]; return o; }

    // ---------- STL (2.5D columns: each cell is solid in [lo,hi]) ----------
    public static int WriteStl(string path, float[] lo, float[] hi)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(new byte[80]); bw.Write((uint)0);
        int tris = 0;
        Action<float[], float[], float[], float[]> tri = (n, a, b, c) =>
        {
            bw.Write(n[0]); bw.Write(n[1]); bw.Write(n[2]);
            bw.Write(a[0]); bw.Write(a[1]); bw.Write(a[2]);
            bw.Write(b[0]); bw.Write(b[1]); bw.Write(b[2]);
            bw.Write(c[0]); bw.Write(c[1]); bw.Write(c[2]);
            bw.Write((ushort)0); tris++;
        };
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                int k = j * N + i;
                if (!(hi[k] > lo[k])) continue;
                float x0 = (float)(i * P - R), x1 = (float)((i + 1) * P - R);
                float y0 = (float)(j * P - R), y1 = (float)((j + 1) * P - R);
                float a = lo[k], b = hi[k];
                // top / bottom
                tri(new float[] { 0, 0, 1 }, new float[] { x0, y0, b }, new float[] { x1, y0, b }, new float[] { x1, y1, b });
                tri(new float[] { 0, 0, 1 }, new float[] { x0, y0, b }, new float[] { x1, y1, b }, new float[] { x0, y1, b });
                tri(new float[] { 0, 0, -1 }, new float[] { x0, y0, a }, new float[] { x1, y1, a }, new float[] { x1, y0, a });
                tri(new float[] { 0, 0, -1 }, new float[] { x0, y0, a }, new float[] { x0, y1, a }, new float[] { x1, y1, a });
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    float c = 0, e = 0; bool has = false;
                    if (ni >= 0 && nj >= 0 && ni < N && nj < N) { int q = nj * N + ni; if (hi[q] > lo[q]) { has = true; c = lo[q]; e = hi[q]; } }
                    var segs = new List<float[]>();
                    if (!has || e <= a || c >= b) segs.Add(new float[] { a, b });
                    else
                    {
                        if (c > a) segs.Add(new float[] { a, Math.Min(b, c) });
                        if (e < b) segs.Add(new float[] { Math.Max(a, e), b });
                    }
                    foreach (var s in segs)
                    {
                        float za = s[0], zb = s[1];
                        if (zb - za < 1e-5f) continue;
                        if (d == 0)
                        {
                            var n = new float[] { 1, 0, 0 };
                            tri(n, new float[] { x1, y0, za }, new float[] { x1, y1, za }, new float[] { x1, y1, zb });
                            tri(n, new float[] { x1, y0, za }, new float[] { x1, y1, zb }, new float[] { x1, y0, zb });
                        }
                        else if (d == 1)
                        {
                            var n = new float[] { -1, 0, 0 };
                            tri(n, new float[] { x0, y0, za }, new float[] { x0, y1, zb }, new float[] { x0, y1, za });
                            tri(n, new float[] { x0, y0, za }, new float[] { x0, y0, zb }, new float[] { x0, y1, zb });
                        }
                        else if (d == 2)
                        {
                            var n = new float[] { 0, 1, 0 };
                            tri(n, new float[] { x1, y1, za }, new float[] { x0, y1, za }, new float[] { x0, y1, zb });
                            tri(n, new float[] { x1, y1, za }, new float[] { x0, y1, zb }, new float[] { x1, y1, zb });
                        }
                        else
                        {
                            var n = new float[] { 0, -1, 0 };
                            tri(n, new float[] { x1, y0, za }, new float[] { x0, y0, zb }, new float[] { x0, y0, za });
                            tri(n, new float[] { x1, y0, za }, new float[] { x1, y0, zb }, new float[] { x0, y0, zb });
                        }
                    }
                }
            }
        bw.Flush();
        ms.Position = 80; bw.Write((uint)tris); bw.Flush();
        File.WriteAllBytes(path, ms.ToArray());
        return tris;
    }

    public static void Layer(bool[] mask, float zlo, float zhi, out float[] lo, out float[] hi)
    {
        lo = new float[N * N]; hi = new float[N * N];
        for (int k = 0; k < N * N; k++) if (mask[k]) { lo[k] = zlo; hi[k] = zhi; }
    }

    // ---------- preview PNG ----------
    public static void SavePreview(string path, bool[] raised, bool[] disc, int scale)
    {
        int W = N * scale;
        var px = new byte[W * W];
        for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
            {
                int i = x / scale, j = N - 1 - y / scale;
                int k = j * N + i;
                px[y * W + x] = (byte)(!disc[k] ? 200 : raised[k] ? 235 : 25);
            }
        var bmp = BitmapSource.Create(W, W, 96, 96, PixelFormats.Gray8, null, px, W);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(path)) enc.Save(f);
    }

    // ---------- QR encoder (byte mode, fixed version / EC level) ----------
    // ecl: 0=M,1=L,2=H,3=Q (format bits). Only the version 3-H configuration is tabulated here.
    public static bool[,] MakeQr(string text, out int maskUsed) { return MakeQr(text, 3, out maskUsed); }
    public static bool[,] MakeQr(string text, int ver, out int maskUsed)
    {
        int size = 17 + 4 * ver;
        // supported: V2-M, V3-H, V4-H
        int dataCw, ecPerBlock, blocks, fmtEcl;
        if (ver == 2) { dataCw = 28; ecPerBlock = 16; blocks = 1; fmtEcl = 0; }
        else if (ver == 3) { dataCw = 26; ecPerBlock = 22; blocks = 2; fmtEcl = 2; }
        else if (ver == 4) { dataCw = 36; ecPerBlock = 16; blocks = 4; fmtEcl = 2; }
        else throw new Exception("unsupported QR version");
        int ap = size - 7;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var bits = new List<bool>();
        Action<int, int> put = (v, n) => { for (int i = n - 1; i >= 0; i--) bits.Add(((v >> i) & 1) != 0); };
        put(4, 4); put(bytes.Length, 8);
        foreach (var b in bytes) put(b, 8);
        if (bits.Count > dataCw * 8) throw new Exception("text too long");
        put(0, Math.Min(4, dataCw * 8 - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        for (int pad = 0xEC; bits.Count < dataCw * 8; pad ^= 0xEC ^ 0x11) put(pad, 8);
        var data = new byte[dataCw];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) data[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        int per = dataCw / blocks;
        var div = RsDivisor(ecPerBlock);
        var dBlocks = new List<byte[]>(); var eBlocks = new List<byte[]>();
        for (int b = 0; b < blocks; b++)
        {
            var d = new byte[per]; Array.Copy(data, b * per, d, 0, per);
            dBlocks.Add(d); eBlocks.Add(RsRemainder(d, div));
        }
        var cw = new List<byte>();
        for (int i = 0; i < per; i++) foreach (var d in dBlocks) cw.Add(d[i]);
        for (int i = 0; i < ecPerBlock; i++) foreach (var e in eBlocks) cw.Add(e[i]);

        var mod = new bool[size, size]; var fn = new bool[size, size]; // [y,x]
        Action<int, int, bool> setF = (x, y, v) => { mod[y, x] = v; fn[y, x] = true; };
        for (int i = 0; i < size; i++) { setF(6, i, i % 2 == 0); setF(i, 6, i % 2 == 0); }
        int[][] finders = { new[] { 3, 3 }, new[] { size - 4, 3 }, new[] { 3, size - 4 } };
        foreach (var f in finders)
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
                {
                    int x = f[0] + dx, y = f[1] + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    setF(x, y, dist != 2 && dist != 4);
                }
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                setF(ap + dx, ap + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        DrawFormat(setF, size, 0, fmtEcl);

        int bi = 0, total = cw.Count * 8;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool up = ((right + 1) & 2) == 0;
                    int y = up ? size - 1 - vert : vert;
                    if (!fn[y, x] && bi < total) { mod[y, x] = ((cw[bi >> 3] >> (7 - (bi & 7))) & 1) != 0; bi++; }
                }
        }

        int best = -1; long bestPen = long.MaxValue; bool[,] bestM = null;
        for (int m = 0; m < 8; m++)
        {
            var t = (bool[,])mod.Clone();
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    if (!fn[y, x] && MaskBit(m, x, y)) t[y, x] = !t[y, x];
            Action<int, int, bool> setT = (x, y, v) => { t[y, x] = v; };
            DrawFormat(setT, size, m, fmtEcl);
            long pen = Penalty(t, size);
            if (pen < bestPen) { bestPen = pen; best = m; bestM = t; }
        }
        maskUsed = best;
        return bestM;
    }

    static void DrawFormat(Action<int, int, bool> set, int size, int mask, int ecl)
    {
        int data = (ecl << 3) | mask;
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;
        Func<int, bool> bit = i => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) set(8, i, bit(i));
        set(8, 7, bit(6)); set(8, 8, bit(7)); set(7, 8, bit(8));
        for (int i = 9; i < 15; i++) set(14 - i, 8, bit(i));
        for (int i = 0; i < 8; i++) set(size - 1 - i, 8, bit(i));
        for (int i = 8; i < 15; i++) set(8, size - 15 + i, bit(i));
        set(8, size - 8, true);
    }

    static bool MaskBit(int m, int x, int y)
    {
        switch (m)
        {
            case 0: return (x + y) % 2 == 0;
            case 1: return y % 2 == 0;
            case 2: return x % 3 == 0;
            case 3: return (x + y) % 3 == 0;
            case 4: return (x / 3 + y / 2) % 2 == 0;
            case 5: return x * y % 2 + x * y % 3 == 0;
            case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
            default: return ((x + y) % 2 + x * y % 3) % 2 == 0;
        }
    }

    static long Penalty(bool[,] t, int n)
    {
        long p = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < n; a++)
            {
                int run = 1;
                for (int b = 1; b < n; b++)
                {
                    bool cur = pass == 0 ? t[a, b] : t[b, a], prev = pass == 0 ? t[a, b - 1] : t[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                    else run = 1;
                }
                for (int b = 0; b + 7 <= n; b++)
                {
                    Func<int, bool> g = k => pass == 0 ? t[a, k] : t[k, a];
                    if (g(b) && !g(b + 1) && g(b + 2) && g(b + 3) && g(b + 4) && !g(b + 5) && g(b + 6))
                    {
                        bool before = true, after = true;
                        for (int k = 1; k <= 4; k++) { if (b - k >= 0 && g(b - k)) before = false; if (b + 6 + k < n && g(b + 6 + k)) after = false; }
                        if (before || after) p += 40;
                    }
                }
            }
        int dark = 0;
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                if (t[y, x]) dark++;
                if (x < n - 1 && y < n - 1 && t[y, x] == t[y, x + 1] && t[y, x] == t[y + 1, x] && t[y, x] == t[y + 1, x + 1]) p += 3;
            }
        int k2 = (int)(Math.Abs(dark * 20 - n * n * 10) / (n * n));
        p += k2 * 10;
        return p;
    }

    static int Mul(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
        return z;
    }

    static byte[] RsDivisor(int deg)
    {
        var r = new int[deg]; r[deg - 1] = 1; int root = 1;
        for (int i = 0; i < deg; i++)
        {
            for (int j = 0; j < deg; j++) { r[j] = Mul(r[j], root); if (j + 1 < deg) r[j] ^= r[j + 1]; }
            root = Mul(root, 2);
        }
        var o = new byte[deg]; for (int i = 0; i < deg; i++) o[i] = (byte)r[i]; return o;
    }

    static byte[] RsRemainder(byte[] data, byte[] div)
    {
        var r = new int[div.Length];
        foreach (byte b in data)
        {
            int f = b ^ r[0];
            Array.Copy(r, 1, r, 0, r.Length - 1); r[r.Length - 1] = 0;
            for (int i = 0; i < r.Length; i++) r[i] ^= Mul(div[i], f);
        }
        var o = new byte[r.Length]; for (int i = 0; i < r.Length; i++) o[i] = (byte)r[i]; return o;
    }
}
