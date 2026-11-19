using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;

// Poker-chip style "MakerChip": 4 colours (0 orange, 1 white, 2 black, 3 navy)
public static class Chip
{
    const int N = CoinGen.N;
    public const double R = 20.0;       // outer radius
    public const double RIDGE = 17.1;   // rim ridge inner radius
    public const double RING = 16.7;    // white thin ring inner radius (RING..RIDGE)
    public const double ZBASE = 1.2, ZTOP = 1.6, ZRING = 1.4; // per half (two halves glued = 3.2mm)
    public const int STRIPES = 8; public const double STRIPE_DEG = 13.0;

    // ---------- image ----------
    public static byte[] LoadRgb(string path, out int w, out int h)
    {
        using (var s = File.OpenRead(path))
        {
            var dec = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var c = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
            w = c.PixelWidth; h = c.PixelHeight;
            var buf = new byte[w * h * 4];
            c.CopyPixels(buf, w * 4, 0);
            return buf;
        }
    }

    static void Px(byte[] img, int w, int h, double x, double y, out double r, out double g, out double b)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0; r = g = b = 0;
        for (int k = 0; k < 4; k++)
        {
            int xx = Math.Max(0, Math.Min(w - 1, x0 + (k & 1))), yy = Math.Max(0, Math.Min(h - 1, y0 + (k >> 1)));
            double wt = ((k & 1) == 1 ? fx : 1 - fx) * ((k >> 1) == 1 ? fy : 1 - fy);
            int o = (yy * w + xx) * 4;
            b += img[o] * wt; g += img[o + 1] * wt; r += img[o + 2] * wt;
        }
    }

    public static int Classify(double r, double g, double b)
    {
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        double lum = 0.3 * r + 0.59 * g + 0.11 * b;
        if (b > r + 15 && b >= g) return 3;
        if (lum < 95) return 2;
        if (mx - mn < 60 && lum > 140) return 1;
        if (r - b > 90) return 0;
        return lum >= 125 ? 1 : 2;
    }

    static bool IsWhite(byte[] img, int w, int h, double x, double y)
    {
        double r, g, b; Px(img, w, h, x, y, out r, out g, out b);
        return Math.Min(r, Math.Min(g, b)) > 175 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 55;
    }

    // Fit the thin white ring (axis-aligned ellipse) starting from a rough guess.
    public static double[] FitRing(byte[] img, int w, int h, double cx, double cy, double rx, double ry)
    {
        var el = new double[] { cx, cy, rx, ry };
        for (int it = 0; it < 3; it++)
        {
            var pts = new List<double[]>();
            for (int a = 0; a < 360; a += 2)
            {
                double t = a * Math.PI / 180, ct = Math.Cos(t), st = Math.Sin(t);
                double best = -1, bestd = 1e9;
                // find white runs between 0.9 and 1.1, choose the one closest to 1
                double runStart = -1;
                for (double rho = 0.88; rho <= 1.12; rho += 0.004)
                {
                    bool wh = IsWhite(img, w, h, el[0] + ct * rho * el[2], el[1] + st * rho * el[3]);
                    if (wh && runStart < 0) runStart = rho;
                    if ((!wh || rho + 0.004 > 1.12) && runStart >= 0)
                    {
                        double mid = (runStart + rho) / 2, len = rho - runStart;
                        if (len < 0.05 && Math.Abs(mid - 1) < bestd) { bestd = Math.Abs(mid - 1); best = mid; }
                        runStart = -1;
                    }
                }
                if (best > 0) pts.Add(new double[] { el[0] + ct * best * el[2], el[1] + st * best * el[3] });
            }
            // robust: drop outliers vs current ellipse
            var keep = new List<double[]>();
            foreach (var p in pts)
            {
                double u = (p[0] - el[0]) / el[2], v = (p[1] - el[1]) / el[3];
                double rr = Math.Sqrt(u * u + v * v);
                if (Math.Abs(rr - 1) < (it == 0 ? 0.06 : 0.025)) keep.Add(p);
            }
            if (keep.Count < 30) break;
            el = FitEllipse(keep);
        }
        return el;
    }

    static double[] FitEllipse(List<double[]> pts)
    {
        var M = new double[4, 5];
        foreach (var p in pts)
        {
            double[] row = { p[0] * p[0], p[1] * p[1], p[0], p[1] };
            for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) M[i, j] += row[i] * row[j]; M[i, 4] += row[i]; }
        }
        for (int i = 0; i < 4; i++)
        {
            int piv = i;
            for (int r = i + 1; r < 4; r++) if (Math.Abs(M[r, i]) > Math.Abs(M[piv, i])) piv = r;
            for (int c = 0; c <= 4; c++) { double t = M[i, c]; M[i, c] = M[piv, c]; M[piv, c] = t; }
            for (int r = 0; r < 4; r++) { if (r == i) continue; double f = M[r, i] / M[i, i]; for (int c = i; c <= 4; c++) M[r, c] -= f * M[i, c]; }
        }
        double A = M[0, 4] / M[0, 0], B = M[1, 4] / M[1, 1], C = M[2, 4] / M[2, 2], D = M[3, 4] / M[3, 3];
        double cx = -C / (2 * A), cy = -D / (2 * B), G = 1 + C * C / (4 * A) + D * D / (4 * B);
        return new double[] { cx, cy, Math.Sqrt(G / A), Math.Sqrt(G / B) };
    }

    // Classify each grid cell inside the field (r < RING) from the image. el maps r=RING+0.2 (ring centre).
    public static int[] ClassMap(byte[] img, int w, int h, double[] el)
    {
        double rRef = (RING + RIDGE) / 2;
        var cls = new int[N * N];
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                double x = CoinGen.Cx(i), y = CoinGen.Cy(j);
                if (x * x + y * y >= (RING - 0.15) * (RING - 0.15)) continue;
                var votes = new int[4];
                for (int sy = -1; sy <= 1; sy++) for (int sx = -1; sx <= 1; sx++)
                    {
                        double u = (x + sx * CoinGen.P / 3) / rRef, v = (y + sy * CoinGen.P / 3) / rRef;
                        double r, g, b; Px(img, w, h, el[0] + u * el[2], el[1] - v * el[3], out r, out g, out b);
                        votes[Classify(r, g, b)]++;
                    }
                int best = 0; for (int k = 1; k < 4; k++) if (votes[k] > votes[best]) best = k;
                cls[j * N + i] = best;
            }
        return cls;
    }

    public static bool[] Eq(int[] cls, int v) { var m = new bool[N * N]; for (int k = 0; k < m.Length; k++) m[k] = cls[k] == v; return m; }

    public static bool[] Dilate(bool[] m, bool[] limit)
    {
        var o = (bool[])m.Clone();
        for (int j = 1; j < N - 1; j++) for (int i = 1; i < N - 1; i++)
            {
                int k = j * N + i;
                if (!m[k] && limit[k] && (m[k - 1] || m[k + 1] || m[k - N] || m[k + N])) o[k] = true;
            }
        return o;
    }

    public static bool[] Erode(bool[] m, int n)
    {
        for (int it = 0; it < n; it++)
        {
            var o = new bool[N * N];
            for (int j = 1; j < N - 1; j++) for (int i = 1; i < N - 1; i++)
                {
                    int k = j * N + i;
                    o[k] = m[k] && m[k - 1] && m[k + 1] && m[k - N] && m[k + N];
                }
            m = o;
        }
        return m;
    }

    public static bool[] Grow(bool[] m, int n)
    {
        var all = new bool[N * N]; for (int k = 0; k < all.Length; k++) all[k] = true;
        for (int it = 0; it < n; it++) m = Dilate(m, all);
        return m;
    }

    public static bool[] KeepLargest(bool[] m)
    {
        var lab = new int[N * N]; int cur = 0, best = 0, bestN = 0;
        var st = new Stack<int>();
        for (int s = 0; s < N * N; s++)
        {
            if (!m[s] || lab[s] != 0) continue;
            cur++; int cnt = 0; st.Push(s); lab[s] = cur;
            while (st.Count > 0)
            {
                int p = st.Pop(); cnt++; int x = p % N, y = p / N;
                int[] nb = { x > 0 ? p - 1 : -1, x < N - 1 ? p + 1 : -1, y > 0 ? p - N : -1, y < N - 1 ? p + N : -1 };
                foreach (int q in nb) if (q >= 0 && m[q] && lab[q] == 0) { lab[q] = cur; st.Push(q); }
            }
            if (cnt > bestN) { bestN = cnt; best = cur; }
        }
        var o = new bool[N * N]; for (int k = 0; k < o.Length; k++) o[k] = lab[k] == best && best > 0; return o;
    }

    public static bool[] LastSil, LastBg; public static List<double[]> DarkZones = new List<double[]>(); public static int CloseR = 3; public static double BgMin = 250;
    public static double AdaptT = 0, AdaptMax = 215; public static int AdaptR = 9;   // local-contrast line detection
    // hand-traced strokes: polylines in image px { x0,y0, x1,y1, ... }, drawn black StrokeWidthMm wide
    public static List<double[]> Strokes = new List<double[]>(); public static double StrokeWidthMm = 0.45;
    // rectangles in image px { x0, y0, x1, y1 } where automatic line detection is off (only very dark ink counts)
    public static List<double[]> NoAutoZones = new List<double[]>(); public static double QuietBlackThr = 90;
    // tonal-edge zones in image px: { x0, y0, x1, y1, whiteThreshold }
    public static List<double[]> EdgeZones = new List<double[]>(); public static int EdgeW = 2, EdgeMinBlob = 3000, EdgeHoleMax = 600, EdgeOpen = 5, EdgeLook = 4; public static double EdgeDark = 205; public static double EdgeReach = 0.8; public static bool[] LastEdge;
    // ---------- front from the pencil icon (white round background) ----------
    // Returns per-cell class: -1 none, 1 white, 2 black. Also returns the image->mm mapping
    // map = { imgCx, imgCy, mmPerPx } so other features (laptop text) can be placed.
    public static int[] IconFront(byte[] g, int w, int h, double ccx, double ccy, double cr,
                                  int lineThr, int blackThr, double fitR, out double[] map)
    {
        // blur 3x3
        var gb = new double[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double s = 0; int n = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    { int xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < w && yy < h) { s += g[yy * w + xx]; n++; } }
                gb[y * w + x] = s / n;
            }
        Func<int, int, double> dist = (x, y) => Math.Sqrt((x - ccx) * (x - ccx) + (y - ccy) * (y - ccy));
        var inside = new bool[w * h]; var line = new bool[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int k = y * w + x; inside[k] = dist(x, y) < cr - 4;
                line[k] = inside[k] && gb[k] < lineThr;
            }
        // close gaps in outlines
        int CL = CloseR;
        var closed = new bool[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (!line[y * w + x]) continue;
                for (int dy = -CL; dy <= CL; dy++) for (int dx = -CL; dx <= CL; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (dx * dx + dy * dy <= CL * CL && xx >= 0 && yy >= 0 && xx < w && yy < h) closed[yy * w + xx] = true;
                    }
            }
        // flood background from the rim of the white circle
        var bg = new bool[w * h]; var st = new Stack<int>();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int k = y * w + x; double d = dist(x, y);
                if (inside[k] && d > cr - 10 && !closed[k]) { bg[k] = true; st.Push(k); }
            }
        while (st.Count > 0)
        {
            int p = st.Pop(); int x = p % w, y = p / w;
            int[] nb = { x > 0 ? p - 1 : -1, x < w - 1 ? p + 1 : -1, y > 0 ? p - w : -1, y < h - 1 ? p + w : -1 };
            foreach (int q in nb) if (q >= 0 && !bg[q] && inside[q] && !closed[q] && gb[q] >= BgMin) { bg[q] = true; st.Push(q); }
        }
        var sil = new bool[w * h];
        for (int k = 0; k < w * h; k++) sil[k] = inside[k] && !bg[k];
        // undo the closing padding (erode CL px)
        for (int it = 0; it < CL - 1; it++)
        {
            var o = new bool[w * h];
            for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
                { int k = y * w + x; o[k] = sil[k] && sil[k - 1] && sil[k + 1] && sil[k - w] && sil[k + w]; }
            sil = o;
        }
        LastSil = sil; LastBg = bg;
        // bbox -> fit into a circle of radius fitR (mm)
        int x0 = w, x1 = 0, y0 = h, y1 = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (sil[y * w + x]) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        double icx = (x0 + x1) / 2.0, icy = (y0 + y1) / 2.0, maxd = 0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (sil[y * w + x])
                    maxd = Math.Max(maxd, Math.Sqrt((x - icx) * (x - icx) + (y - icy) * (y - icy)));
        double mmpp = fitR / maxd;
        map = new double[] { icx, icy, mmpp };
        // local mean (box filter) for adaptive line detection
        var integ = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++) { double row = 0; for (int x = 0; x < w; x++) { row += gb[y * w + x]; integ[(y + 1) * (w + 1) + x + 1] = integ[y * (w + 1) + x + 1] + row; } }
        Func<int, int, double> mean = (xc, yc) =>
        {
            int xa = Math.Max(0, xc - AdaptR), xb = Math.Min(w, xc + AdaptR + 1), ya = Math.Max(0, yc - AdaptR), yb = Math.Min(h, yc + AdaptR + 1);
            double s = integ[yb * (w + 1) + xb] - integ[ya * (w + 1) + xb] - integ[yb * (w + 1) + xa] + integ[ya * (w + 1) + xa];
            return s / ((xb - xa) * (yb - ya));
        };
        // tonal edges: outline of bright (white) regions inside EdgeZones, e.g. the white face
        // patch against the grey-shaded forehead, which is drawn by shading rather than a line
        var edge = new bool[w * h];
        if (EdgeZones.Count > 0)
        {
            // 5x5 box blur of gb
            var gs = new double[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    int xa = Math.Max(0, x - 2), xb = Math.Min(w, x + 3), ya = Math.Max(0, y - 2), yb = Math.Min(h, y + 3);
                    double s = integ[yb * (w + 1) + xb] - integ[ya * (w + 1) + xb] - integ[yb * (w + 1) + xa] + integ[ya * (w + 1) + xa];
                    gs[y * w + x] = s / ((xb - xa) * (yb - ya));
                }
            foreach (var z in EdgeZones)
            {
                int zx0 = (int)z[0], zy0 = (int)z[1], zx1 = (int)z[2], zy1 = (int)z[3]; double wt = z[4];
                // white mask in the zone, small blobs removed
                int zw = zx1 - zx0, zh = zy1 - zy0;
                var wm = new bool[zw * zh];
                for (int y = 0; y < zh; y++) for (int x = 0; x < zw; x++) { int k = (y + zy0) * w + x + zx0; wm[y * zw + x] = sil[k] && gs[k] > wt; }
                // opening (erode then dilate) cuts thin bright streaks off the white face areas
                for (int pass = 0; pass < 2; pass++)
                    for (int it = 0; it < EdgeOpen; it++)
                    {
                        var o2 = new bool[zw * zh];
                        for (int y = 0; y < zh; y++) for (int x = 0; x < zw; x++)
                            {
                                int p = y * zw + x;
                                bool l = x > 0 ? wm[p - 1] : wm[p], r = x < zw - 1 ? wm[p + 1] : wm[p], u = y > 0 ? wm[p - zw] : wm[p], d = y < zh - 1 ? wm[p + zw] : wm[p];
                                o2[p] = pass == 0 ? (wm[p] && l && r && u && d) : (wm[p] || l || r || u || d);
                            }
                        wm = o2;
                    }
                var seen = new bool[zw * zh]; var stk = new Stack<int>(); var comp = new List<int>();
                for (int s0 = 0; s0 < wm.Length; s0++)
                {
                    if (!wm[s0] || seen[s0]) continue;
                    comp.Clear(); stk.Push(s0); seen[s0] = true;
                    while (stk.Count > 0)
                    {
                        int p = stk.Pop(); comp.Add(p); int px = p % zw, py = p / zw;
                        int[] nb = { px > 0 ? p - 1 : -1, px < zw - 1 ? p + 1 : -1, py > 0 ? p - zw : -1, py < zh - 1 ? p + zw : -1 };
                        foreach (int q in nb) if (q >= 0 && wm[q] && !seen[q]) { seen[q] = true; stk.Push(q); }
                    }
                    // keep only big white areas that reach the lower part of the zone (the face, not head sheen)
                    int maxY = 0; foreach (int p in comp) maxY = Math.Max(maxY, p / zw);
                    if (comp.Count < EdgeMinBlob || maxY < zh * EdgeReach) foreach (int p in comp) wm[p] = false;
                }
                // fill small grey holes inside the white areas (fur tufts) so they don't get outlined
                var seen2 = new bool[zw * zh];
                for (int s0 = 0; s0 < wm.Length; s0++)
                {
                    if (wm[s0] || seen2[s0]) continue;
                    comp.Clear(); stk.Push(s0); seen2[s0] = true; bool border = false;
                    while (stk.Count > 0)
                    {
                        int p = stk.Pop(); comp.Add(p); int px = p % zw, py = p / zw;
                        if (px == 0 || py == 0 || px == zw - 1 || py == zh - 1) border = true;
                        int[] nb = { px > 0 ? p - 1 : -1, px < zw - 1 ? p + 1 : -1, py > 0 ? p - zw : -1, py < zh - 1 ? p + zw : -1 };
                        foreach (int q in nb) if (q >= 0 && !wm[q] && !seen2[q]) { seen2[q] = true; stk.Push(q); }
                    }
                    if (!border && comp.Count < EdgeHoleMax) foreach (int p in comp) wm[p] = true;
                }
                // boundary of the white mask where the other side is grey (not background, not already a dark line)
                for (int y = 1; y < zh - 1; y++) for (int x = 1; x < zw - 1; x++)
                    {
                        int p = y * zw + x; if (!wm[p]) continue;
                        bool b = !wm[p - 1] || !wm[p + 1] || !wm[p - zw] || !wm[p + zw];
                        if (!b) continue;
                        // only strong edges: there must be clearly darker grey nearby (weak light-grey fades are skipped)
                        double mn = 255;
                        for (int dy = -EdgeLook; dy <= EdgeLook; dy++) for (int dx = -EdgeLook; dx <= EdgeLook; dx++)
                            {
                                int xx = x + zx0 + dx, yy = y + zy0 + dy;
                                if (xx >= 0 && yy >= 0 && xx < w && yy < h) mn = Math.Min(mn, gs[yy * w + xx]);
                            }
                        if (mn > EdgeDark) continue;
                        for (int dy = -EdgeW; dy <= EdgeW; dy++) for (int dx = -EdgeW; dx <= EdgeW; dx++)
                            {
                                int xx = x + zx0 + dx, yy = y + zy0 + dy;
                                if (dx * dx + dy * dy <= EdgeW * EdgeW && xx >= 0 && yy >= 0 && xx < w && yy < h && sil[yy * w + xx]) edge[yy * w + xx] = true;
                            }
                    }
            }
        }
        LastEdge = edge;
        // hand-traced strokes (image px polylines, Catmull-Rom smoothed)
        var segs = new List<double[]>();
        foreach (var s in Strokes)
        {
            int n = s.Length / 2;
            Func<int, int, double> P = (i, c) => s[Math.Max(0, Math.Min(n - 1, i)) * 2 + c];
            double px = P(0, 0), py = P(0, 1);
            for (int i = 0; i < n - 1; i++)
                for (int t = 1; t <= 12; t++)
                {
                    double u = t / 12.0, u2 = u * u, u3 = u2 * u;
                    double[] q = new double[2];
                    for (int c = 0; c < 2; c++)
                        q[c] = 0.5 * (2 * P(i, c) + (-P(i - 1, c) + P(i + 1, c)) * u + (2 * P(i - 1, c) - 5 * P(i, c) + 4 * P(i + 1, c) - P(i + 2, c)) * u2 + (-P(i - 1, c) + 3 * P(i, c) - 3 * P(i + 1, c) + P(i + 2, c)) * u3);
                    segs.Add(new double[] { px, py, q[0], q[1] }); px = q[0]; py = q[1];
                }
        }
        double strokeHalfPx = StrokeWidthMm / 2 / mmpp;
        Func<double, double, bool> onStroke = (qx, qy) =>
        {
            foreach (var sg in segs)
            {
                double dx = sg[2] - sg[0], dy = sg[3] - sg[1], l2 = dx * dx + dy * dy;
                double tt = l2 > 0 ? Math.Max(0, Math.Min(1, ((qx - sg[0]) * dx + (qy - sg[1]) * dy) / l2)) : 0;
                double ex = sg[0] + tt * dx - qx, ey = sg[1] + tt * dy - qy;
                if (ex * ex + ey * ey <= strokeHalfPx * strokeHalfPx) return true;
            }
            return false;
        };
        var top = new int[N * N];
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                int k = j * N + i; top[k] = -1;
                double ix = icx + CoinGen.Cx(i) / mmpp, iy = icy - CoinGen.Cy(j) / mmpp;
                int xi = (int)Math.Round(ix), yi = (int)Math.Round(iy);
                if (xi < 0 || yi < 0 || xi >= w || yi >= h || !sil[yi * w + xi]) continue;
                double v = CoinGen.Sample(g, w, h, ix, iy);
                double thr = blackThr;
                foreach (var z in DarkZones) { double ux = (ix - z[0]) / z[2], uy = (iy - z[1]) / z[3]; if (ux * ux + uy * uy < 1) thr = z[4]; }
                bool quiet = false;   // zones where automatic line detection is switched off
                foreach (var z in NoAutoZones) if (ix >= z[0] && iy >= z[1] && ix <= z[2] && iy <= z[3]) quiet = true;
                bool adaptive = !quiet && AdaptT > 0 && v < AdaptMax && mean(xi, yi) - v > AdaptT;
                bool dark = quiet ? v < QuietBlackThr : v < thr;
                top[k] = dark || adaptive || edge[yi * w + xi] || onStroke(ix, iy) ? 2 : 1;
            }
        return top;
    }

    // centroid / width / angle of a mask (for placing replacement text)
    public static double[] Stats(bool[] m)
    {
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0; int n = 0;
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++) if (m[j * N + i])
                { double x = CoinGen.Cx(i), y = CoinGen.Cy(j); sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y; n++; }
        if (n == 0) return new double[] { 0, 0, 0, 0, 0 };
        double mx = sx / n, my = sy / n, cxx = sxx / n - mx * mx, cyy = syy / n - my * my, cxy = sxy / n - mx * my;
        double ang = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
        double ca = Math.Cos(ang), sa = Math.Sin(ang), lo = 1e9, hi = -1e9;
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++) if (m[j * N + i])
                { double t = (CoinGen.Cx(i) - mx) * ca + (CoinGen.Cy(j) - my) * sa; lo = Math.Min(lo, t); hi = Math.Max(hi, t); }
        return new double[] { mx, my, hi - lo, ang * 180 / Math.PI, n };
    }

    // Render text; returns [fill, outline] masks. Text centred at (cx,cy) mm, given width (mm) and rotation (deg, CCW).
    public static bool[][] Text(string s, string font, double cx, double cy, double width, double angDeg, double outline)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(font), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 100, Brushes.Black, 1.0);
        var geo = ft.BuildGeometry(new Point(0, 0));
        var bb = geo.Bounds;
        double sc = width / bb.Width;
        var tg = new TransformGroup();
        tg.Children.Add(new TranslateTransform(-(bb.Left + bb.Width / 2), -(bb.Top + bb.Height / 2)));
        tg.Children.Add(new ScaleTransform(sc, -sc));          // flip y: WPF y down -> mm y up
        tg.Children.Add(new RotateTransform(angDeg));
        tg.Children.Add(new TranslateTransform(cx, cy));
        var g = geo.Clone(); g.Transform = tg;
        var flat = g.GetFlattenedPathGeometry(0.01, ToleranceType.Absolute);
        var b2 = flat.Bounds;
        var pen = new Pen(Brushes.Black, outline * 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var fill = new bool[N * N]; var ol = new bool[N * N];
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                double x = CoinGen.Cx(i), y = CoinGen.Cy(j);
                if (x < b2.Left - outline - 0.2 || x > b2.Right + outline + 0.2 || y < b2.Top - outline - 0.2 || y > b2.Bottom + outline + 0.2) continue;
                var p = new Point(x, y);
                bool f = flat.FillContains(p);
                fill[j * N + i] = f;
                ol[j * N + i] = f || (outline > 0 && flat.StrokeContains(pen, p));
            }
        return new bool[][] { fill, ol };
    }

    public static bool[] RoundRect(double cx, double cy, double half, double rad)
    {
        var m = new bool[N * N];
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                double dx = Math.Max(0, Math.Abs(CoinGen.Cx(i) - cx) - (half - rad)), dy = Math.Max(0, Math.Abs(CoinGen.Cy(j) - cy) - (half - rad));
                m[j * N + i] = dx * dx + dy * dy <= rad * rad;
            }
        return m;
    }

    // X emblem: black rounded-square badge with a white X whose "\" stroke is a hollow band
    // and "/" stroke is solid. Returns per-cell: -1 outside badge, 1 white, 2 black.
    // u = half height of the X (mm); badgeHalf/badgeR = badge size and corner radius (mm).
    public static int[] XEmblem(double cx, double cy, double u, double badgeHalf, double badgeR,
                                double slant, double hw1, double t, double hw2)
    {
        var o = new int[N * N];
        // unit normals of the two stroke centre lines
        double l1 = Math.Sqrt(slant * slant + 1), l2 = l1;
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                int k = j * N + i; o[k] = -1;
                double X = CoinGen.Cx(i) - cx, Y = CoinGen.Cy(j) - cy;
                double dx = Math.Max(0, Math.Abs(X) - (badgeHalf - badgeR)), dy = Math.Max(0, Math.Abs(Y) - (badgeHalf - badgeR));
                if (dx * dx + dy * dy > badgeR * badgeR) continue;
                double x = X / u, y = Y / u;
                // "\" : through (-slant,1)-(slant,-1)  -> x*1 + y*slant = 0
                double d1 = (x + y * slant) / l1;
                // "/" : through (-slant,-1)-(slant,1) -> x*1 - y*slant = 0
                double d2 = (x - y * slant) / l2;
                bool inY = Math.Abs(y) <= 1;
                bool band = inY && Math.Abs(d1) <= hw1;
                bool hollow = Math.Abs(y) <= 1 - t && Math.Abs(d1) <= hw1 - t;
                bool slash = inY && Math.Abs(d2) <= hw2 && Math.Abs(d1) > hw1;
                o[k] = (band && !hollow) || slash ? 1 : 2;
            }
        return o;
    }

    public static bool[] QrModules(bool[,] qr, double module, double cx, double cy)
    {
        int n = qr.GetLength(0); double half = n * module / 2;
        var m = new bool[N * N];
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                double x = CoinGen.Cx(i) - cx, y = CoinGen.Cy(j) - cy;
                if (Math.Abs(x) >= half || Math.Abs(y) >= half) continue;
                int col = Math.Min(n - 1, (int)Math.Floor((x + half) / module)), row = Math.Min(n - 1, (int)Math.Floor((half - y) / module));
                m[j * N + i] = qr[row, col];
            }
        return m;
    }

    // top: per-cell colour of the raised design inside the field (-1 none)
    // Writes 4 STLs (orange/white/black/navy) for one half, printed face up.
    public static void BuildHalf(int[] top, string prefix, string previewPath)
    {
        var lo = new float[4][]; var hi = new float[4][];
        for (int c = 0; c < 4; c++) { lo[c] = new float[N * N]; hi[c] = new float[N * N]; }
        var surf = new int[N * N]; // colour visible from top, -1 outside
        for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
            {
                int k = j * N + i; double x = CoinGen.Cx(i), y = CoinGen.Cy(j), r = Math.Sqrt(x * x + y * y);
                surf[k] = -1;
                if (r > R) continue;
                if (r >= RIDGE)
                {
                    double a = Math.Atan2(y, x) * 180 / Math.PI;
                    double rel = ((a - 22.5) % 45 + 45) % 45; rel = Math.Min(rel, 45 - rel);
                    int c = rel <= STRIPE_DEG / 2 ? 1 : 0;
                    lo[c][k] = 0; hi[c][k] = (float)ZTOP; surf[k] = c; continue;
                }
                lo[0][k] = 0; hi[0][k] = (float)ZBASE; surf[k] = 0;
                if (r >= RING) { lo[1][k] = (float)ZBASE; hi[1][k] = (float)ZRING; surf[k] = 1; continue; }
                int t = top[k];
                if (t >= 0) { lo[t][k] = (float)ZBASE; hi[t][k] = (float)ZTOP; surf[k] = t; }
            }
        string[] names = { "orange", "white", "black", "navy" };
        for (int c = 0; c < 4; c++)
        {
            int tris = CoinGen.WriteStl(prefix + "_" + c + "_" + names[c] + ".stl", lo[c], hi[c]);
            Console.WriteLine("{0,-40} {1,8} tris", Path.GetFileName(prefix + "_" + c + "_" + names[c] + ".stl"), tris);
        }
        // preview
        int S = 3, W = N * S; var px = new byte[W * W * 3];
        byte[][] col = { new byte[] { 245, 140, 30 }, new byte[] { 245, 243, 238 }, new byte[] { 30, 30, 30 }, new byte[] { 30, 55, 140 } };
        for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
            {
                int k = (N - 1 - y / S) * N + x / S; int c = surf[k]; int o = (y * W + x) * 3;
                if (c < 0) { px[o] = px[o + 1] = px[o + 2] = 210; }
                else { px[o] = col[c][0]; px[o + 1] = col[c][1]; px[o + 2] = col[c][2]; }
            }
        var bmp = BitmapSource.Create(W, W, 96, 96, PixelFormats.Rgb24, null, px, W * 3);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(previewPath)) enc.Save(f);
    }
}
