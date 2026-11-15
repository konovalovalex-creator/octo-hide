using System.Collections.Generic;
using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// «Оживляет» модель осьминога из Tripo: сама находит 8 щупалец, тело (голова + мантия)
    /// и каждый кадр гнёт щупальца модели по цепочкам физики <see cref="TentacleRig"/>.
    ///
    /// Требования к модели: осьминог лежит плоско, щупальца звездой и не касаются друг друга,
    /// меш нормализован (низ y = 0, размах ~1), импорт с Read/Write.
    ///
    /// Как гнём: для каждой вершины щупальца при старте запоминаем ближайшую точку на средней линии
    /// руки (параметр u) и смещение в локальной рамке линии (касательная / нормаль / бинормаль).
    /// В игре берём ту же рамку на цепочке физики и ставим вершину туда же — кожа, присоски
    /// и текстура едут вместе со щупальцем. У основания руки плавно переходят в жёсткое тело.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class OctopusModelRig : MonoBehaviour
    {
        public Mesh sourceMesh;
        public OctopusMotor motor;
        public TentacleRig rig;
        [Tooltip("Трансформ тела (дышит и меняет форму в позах) — за ним следует голова и мантия")]
        public Transform body;
        [Tooltip("Средняя длина щупальца в локальных единицах осьминога")]
        public float armLength = 1.1f;
        [Tooltip("Где низ модели относительно центра осьминога (низ коллайдера)")]
        public float groundOffset = -0.35f;
        [Tooltip("Ширина перехода «тело → щупальце», в долях размаха модели")]
        public float rootBlend = 0.07f;

        public bool IsRigged { get; private set; }
        public string Report { get; private set; } = "";

        const int Arms = 8;
        const float Cell = 0.015f;

        Mesh mesh;
        int nV, segs;
        Vector3[] bindQ;       // поза покоя в локальных координатах осьминога
        Vector3[] bindNQ;
        Vector4[] bindTQ;
        int[] armOf;
        float[] tParam;        // 0..segs-1
        float[] weight;
        Vector3[] offP, offN, offT;
        Vector3 bodyRest;

        Vector3[] outP, outN;
        Vector4[] outT;
        Vector3[][] frT, frN, frB;
        float[] thick;     // утолщение руки: сократилась — стала толще (объём сохраняется)
        float flat;        // на дне руки распластываются вширь
        float skirt;       // сидя на месте, основания рук с перепонкой раздуваются «юбкой»
        float[] mantleW;   // насколько вершина принадлежит мантии (она поднимается торчком)
        Vector3 mantlePivot;
        float mantleAngle;
        [Tooltip("Наклон мантии вверх, градусы: сидя / на ходу")]
        public Vector2 mantleTilt = new Vector2(55f, 35f);

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<OctopusMotor>();
            if (rig == null && motor != null) rig = motor.GetComponentInChildren<TentacleRig>();
            if (body == null && motor != null) body = motor.body;
            IsRigged = sourceMesh != null && rig != null && body != null && Bind();
            if (!IsRigged)
            {
                Debug.LogWarning("[OctopusModelRig] Не удалось разобрать модель: " + Report, this);
                GetComponent<MeshRenderer>().enabled = false;
                if (rig != null)
                {
                    rig.renderTubes = true;
                    var tr = rig.GetComponent<MeshRenderer>();
                    if (tr != null) tr.enabled = true;
                }
                enabled = false;
            }
        }

        // ------------------------------------------------------------------ разбор модели

        bool Bind()
        {
            if (!sourceMesh.isReadable) { Report = "меш не читаемый (Read/Write)"; return false; }
            Vector3[] v = sourceMesh.vertices;
            nV = v.Length;
            segs = rig.Segments;

            // 1) Тело — где модель «толстая»: карта высоты (верх − низ) по сетке XZ
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue, maxY = 0f;
            foreach (var p in v)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                maxY = Mathf.Max(maxY, p.y);
            }
            float ox = minX - Cell * 3f, oz = minZ - Cell * 3f;
            int gw = Mathf.CeilToInt((maxX - ox) / Cell) + 4, gh = Mathf.CeilToInt((maxZ - oz) / Cell) + 4;
            var top = new float[gw, gh];
            var bot = new float[gw, gh];
            for (int x = 0; x < gw; x++) for (int z = 0; z < gh; z++) { top[x, z] = float.MinValue; bot[x, z] = float.MaxValue; }
            var gx = new int[nV]; var gz = new int[nV];
            for (int i = 0; i < nV; i++)
            {
                gx[i] = (int)((v[i].x - ox) / Cell); gz[i] = (int)((v[i].z - oz) / Cell);
                top[gx[i], gz[i]] = Mathf.Max(top[gx[i], gz[i]], v[i].y);
                bot[gx[i], gz[i]] = Mathf.Min(bot[gx[i], gz[i]], v[i].y);
            }
            var mask = new bool[gw, gh];
            for (int x = 0; x < gw; x++)
                for (int z = 0; z < gh; z++)
                    mask[x, z] = top[x, z] > float.MinValue && top[x, z] - bot[x, z] > 0.42f * maxY;
            mask = LargestComponent(mask, gw, gh);
            mask = Dilate(mask, gw, gh, 2); mask = Erode(mask, gw, gh, 2);
            mask = FillHoles(mask, gw, gh);
            mask = Dilate(mask, gw, gh, 1);
            float[,] distOut = DistanceOutside(mask, gw, gh);

            // 2) Направления щупалец по кольцу 0.6–0.9 от центра
            Vector2 cen = Vector2.zero;
            foreach (var p in v) cen += new Vector2(p.x, p.z);
            cen /= nV;
            float R = 0f;
            foreach (var p in v) R = Mathf.Max(R, (new Vector2(p.x, p.z) - cen).magnitude);
            var ringAngles = new List<float>();
            foreach (var p in v)
            {
                Vector2 d = new Vector2(p.x, p.z) - cen;
                float m = d.magnitude;
                if (m > 0.6f * R && m < 0.9f * R) ringAngles.Add(Mathf.Atan2(d.y, d.x));
            }
            float[] armAngles = FindArmAngles(ringAngles);
            if (armAngles == null) { Report = "не нашлось 8 щупалец"; return false; }

            // 3) Средние линии: от кончика внутрь, по полосам расстояния, с отслеживанием угла
            var chainsM = new Vector3[Arms][];
            for (int k = 0; k < Arms; k++)
            {
                var pts = new List<Vector3>();
                float cur = armAngles[k];
                for (int s = 0; s < 40; s++)
                {
                    float r = Mathf.Lerp(0.97f * R, 0.04f * R, s / 39f);
                    Vector3 sum = Vector3.zero; int cnt = 0;
                    for (int i = 0; i < nV; i++)
                    {
                        Vector2 d = new Vector2(v[i].x, v[i].z) - cen;
                        if (Mathf.Abs(d.magnitude - r) > 0.02f * R) continue;
                        float da = Mathf.DeltaAngle(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, cur * Mathf.Rad2Deg);
                        if (Mathf.Abs(da) > 14f) continue;
                        sum += v[i]; cnt++;
                    }
                    if (cnt < 3) continue;
                    Vector3 c = sum / cnt;
                    if (mask[(int)((c.x - ox) / Cell), (int)((c.z - oz) / Cell)]) break; // дошли до тела
                    pts.Add(c);
                    cur = Mathf.Atan2(c.z - cen.y, c.x - cen.x);
                }
                if (pts.Count < 4) { Report = $"щупальце {k}: слишком короткая линия"; return false; }
                pts.Reverse(); // корень → кончик
                chainsM[k] = Resample(pts, segs);
            }

            // 4) Поворот: мантия должна смотреть назад (−Z), центр — в корнях щупалец
            Vector2 hub = Vector2.zero;
            for (int k = 0; k < Arms; k++) hub += new Vector2(chainsM[k][0].x, chainsM[k][0].z);
            hub /= Arms;
            Vector2 c0 = Vector2.zero; int nTall = 0;
            foreach (var p in v) if (p.y > 0.5f * maxY) { c0 += new Vector2(p.x, p.z); nTall++; }
            c0 = nTall > 0 ? c0 / nTall : hub;
            Vector2 mantle = (c0 - hub).sqrMagnitude > 1e-8f ? (c0 - hub).normalized : new Vector2(0f, -1f);
            float yaw = 180f - Mathf.Atan2(mantle.x, mantle.y) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            float avgArm = 0f;
            for (int k = 0; k < Arms; k++) avgArm += PolyLength(chainsM[k]);
            avgArm /= Arms;
            float scale = armLength / Mathf.Max(1e-4f, avgArm);
            Vector3 shift = new Vector3(hub.x, 0f, hub.y);
            System.Func<Vector3, Vector3> toQ = p => rot * ((p - shift) * scale) + new Vector3(0f, groundOffset, 0f);

            var chainsQ = new Vector3[Arms][];
            for (int k = 0; k < Arms; k++)
            {
                chainsQ[k] = new Vector3[segs];
                for (int i = 0; i < segs; i++) chainsQ[k][i] = toQ(chainsM[k][i]);
            }

            // 5) Привязка вершин
            Vector3[] n = sourceMesh.normals;
            Vector4[] t = sourceMesh.tangents;
            bool hasT = t != null && t.Length == nV;
            bindQ = new Vector3[nV]; bindNQ = new Vector3[nV]; bindTQ = new Vector4[nV];
            armOf = new int[nV]; tParam = new float[nV]; weight = new float[nV];
            mantleW = new float[nV];
            mantlePivot = new Vector3(0f, groundOffset + maxY * scale * 0.35f, -0.12f * armLength);
            offP = new Vector3[nV]; offN = new Vector3[nV]; offT = new Vector3[nV];

            var bT = new Vector3[Arms][]; var bN = new Vector3[Arms][]; var bB = new Vector3[Arms][];
            for (int k = 0; k < Arms; k++)
            {
                bT[k] = new Vector3[segs]; bN[k] = new Vector3[segs]; bB[k] = new Vector3[segs];
                Frames(chainsQ[k], Vector3.up, bT[k], bN[k], bB[k]);
            }

            int armVerts = 0;
            float blend = rootBlend; // в единицах модели
            for (int i = 0; i < nV; i++)
            {
                Vector3 q = toQ(v[i]);
                bindQ[i] = q;
                bindNQ[i] = rot * (n != null && n.Length == nV ? n[i] : Vector3.up);
                Vector3 tq = rot * (hasT ? (Vector3)t[i] : Vector3.right);
                bindTQ[i] = new Vector4(tq.x, tq.y, tq.z, hasT ? t[i].w : 1f);

                float sd = distOut[gx[i], gz[i]];
                float w = Mathf.Clamp01(sd / blend);
                w = w * w * (3f - 2f * w);
                float mw = Mathf.Clamp01((-q.z - 0.12f * armLength) / (0.3f * armLength));
                mantleW[i] = mw * mw * (3f - 2f * mw) * (1f - w);
                if (w <= 0f) { armOf[i] = -1; continue; }

                int best = -1; float bestD = float.MaxValue, bestT = 0f;
                for (int k = 0; k < Arms; k++)
                {
                    var C = chainsQ[k];
                    for (int j = 0; j < segs - 1; j++)
                    {
                        Vector3 a = C[j], ab = C[j + 1] - a;
                        float f = Mathf.Clamp01(Vector3.Dot(q - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                        float d = (q - (a + ab * f)).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = k; bestT = j + f; }
                    }
                }
                armOf[i] = best;
                tParam[i] = bestT;
                weight[i] = w;
                Sample(chainsQ[best], bT[best], bN[best], bB[best], bestT, out Vector3 P, out Vector3 T, out Vector3 N, out Vector3 B);
                Vector3 dq = q - P;
                offP[i] = new Vector3(Vector3.Dot(dq, T), Vector3.Dot(dq, N), Vector3.Dot(dq, B));
                offN[i] = new Vector3(Vector3.Dot(bindNQ[i], T), Vector3.Dot(bindNQ[i], N), Vector3.Dot(bindNQ[i], B));
                offT[i] = new Vector3(Vector3.Dot(tq, T), Vector3.Dot(tq, N), Vector3.Dot(tq, B));
                armVerts++;
            }

            // 6) Подставляем корни щупалец физике
            var roots = new Vector3[Arms]; var outs = new Vector3[Arms];
            for (int k = 0; k < Arms; k++)
            {
                roots[k] = chainsQ[k][0];
                Vector3 o = chainsQ[k][segs - 1] - chainsQ[k][0]; o.y = 0f;
                outs[k] = o.normalized;
            }
            rig.renderTubes = false;
            var tubeRenderer = rig.GetComponent<MeshRenderer>();
            if (tubeRenderer != null) tubeRenderer.enabled = false;
            rig.SetCustomRoots(roots, outs, armLength);

            // 7) Рабочий меш
            bodyRest = body.localPosition;
            mesh = Instantiate(sourceMesh);
            mesh.name = sourceMesh.name + " (rig)";
            mesh.MarkDynamic();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (armLength * 2f + 2f) * 2f);
            GetComponent<MeshFilter>().sharedMesh = mesh;
            outP = new Vector3[nV]; outN = new Vector3[nV]; outT = new Vector4[nV];
            frT = new Vector3[Arms][]; frN = new Vector3[Arms][]; frB = new Vector3[Arms][];
            thick = new float[Arms];
            for (int k = 0; k < Arms; k++) thick[k] = 1f;
            for (int k = 0; k < Arms; k++) { frT[k] = new Vector3[segs]; frN[k] = new Vector3[segs]; frB[k] = new Vector3[segs]; }

            Report = $"тело {nV - armVerts} вершин, щупальца {armVerts}, масштаб {scale:0.00}";
            return true;
        }

        // ------------------------------------------------------------------ каждый кадр

        void LateUpdate()
        {
            if (!IsRigged) return;
            Transform root = motor.transform;
            float S = root.lossyScale.x;
            Vector3 up = root.up;

            for (int k = 0; k < Arms; k++)
            {
                var C = rig.Chain(k);
                if (C == null) return;
                Frames(C, up, frT[k], frN[k], frB[k]);
                float len = 0f;
                for (int j = 1; j < C.Length; j++) len += Vector3.Distance(C[j - 1], C[j]);
                float ratio = len / Mathf.Max(1e-5f, armLength * S);
                float want = Mathf.Clamp(1f / Mathf.Sqrt(Mathf.Max(0.2f, ratio)), 0.85f, 1.6f);
                thick[k] = Mathf.Lerp(thick[k], want, 1f - Mathf.Exp(-8f * Time.deltaTime));
            }
            flat = Mathf.MoveTowards(flat, motor.IsGrounded ? 1f : 0f, Time.deltaTime * 3f);
            float moving = Mathf.Clamp01(motor.NormalizedSpeed * 1.5f);
            skirt = Mathf.MoveTowards(skirt, motor.IsGrounded ? 1f - moving : 0f, Time.deltaTime * 2f);
            float wantTilt = motor.IsGrounded && motor.Pose == OctopusPose.Normal ? Mathf.Lerp(mantleTilt.x, mantleTilt.y, moving) : 0f;
            mantleAngle = Mathf.Lerp(mantleAngle, wantTilt, 1f - Mathf.Exp(-4f * Time.deltaTime));

            // Жёсткая часть повторяет тело: поза покоя → текущая поза тела (сдвиг, поворот, «дыхание»)
            Matrix4x4 bodyM = body.parent.localToWorldMatrix
                              * Matrix4x4.TRS(body.localPosition, body.localRotation, body.localScale)
                              * Matrix4x4.Translate(-bodyRest);
            Matrix4x4 toLocal = transform.worldToLocalMatrix;

            for (int i = 0; i < nV; i++)
            {
                Vector3 bq = bindQ[i], bn = bindNQ[i];
                Vector4 bt = bindTQ[i];
                Vector3 btv = new Vector3(bt.x, bt.y, bt.z);
                float mwt = mantleW[i];
                if (mwt > 0.001f && mantleAngle > 0.1f)
                {
                    // мантия поднимается торчком: поворот вокруг поперечной оси у «шеи»
                    Quaternion mr = Quaternion.AngleAxis(mantleAngle * mwt, Vector3.right);
                    bq = mantlePivot + mr * (bq - mantlePivot);
                    bn = mr * bn;
                    btv = mr * btv;
                }
                Vector3 rp = bodyM.MultiplyPoint3x4(bq);
                Vector3 rn = bodyM.MultiplyVector(bn);
                Vector3 rt = bodyM.MultiplyVector(btv);
                int k = armOf[i];
                if (k >= 0)
                {
                    Sample(rig.Chain(k), frT[k], frN[k], frB[k], tParam[i], out Vector3 P, out Vector3 T, out Vector3 N, out Vector3 B);
                    Vector3 o = offP[i];
                    // толщина по состоянию руки + распластывание у основания, когда лежит на дне
                    float baseU = 1f - Mathf.Clamp01(tParam[i] / (segs - 1));
                    float wide = thick[k] * (1f + (0.3f * flat + 0.7f * skirt) * baseU * baseU);
                    float tall = thick[k] * (1f - 0.12f * flat * baseU + 0.25f * skirt * baseU * baseU);
                    Vector3 ap = P + (T * o.x + N * (o.y * tall) + B * (o.z * wide)) * S;
                    Vector3 on = offN[i]; Vector3 an = T * on.x + N * on.y + B * on.z;
                    Vector3 ot = offT[i]; Vector3 at = T * ot.x + N * ot.y + B * ot.z;
                    float w = weight[i];
                    rp = Vector3.LerpUnclamped(rp, ap, w);
                    rn = Vector3.LerpUnclamped(rn.normalized, an, w);
                    rt = Vector3.LerpUnclamped(rt.normalized, at, w);
                }
                outP[i] = toLocal.MultiplyPoint3x4(rp);
                outN[i] = toLocal.MultiplyVector(rn).normalized;
                Vector3 lt = toLocal.MultiplyVector(rt).normalized;
                outT[i] = new Vector4(lt.x, lt.y, lt.z, bt.w);
            }
            mesh.vertices = outP;
            mesh.normals = outN;
            mesh.tangents = outT;
        }

        // ------------------------------------------------------------------ геометрия

        /// <summary>Рамка вдоль цепочки с параллельным переносом (без перекручивания).</summary>
        static void Frames(Vector3[] C, Vector3 n0, Vector3[] T, Vector3[] N, Vector3[] B)
        {
            int n = C.Length;
            Vector3 nrm = n0;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = i == 0 ? C[1] - C[0] : (i == n - 1 ? C[n - 1] - C[n - 2] : C[i + 1] - C[i - 1]);
                if (t.sqrMagnitude < 1e-12f) t = i > 0 ? T[i - 1] : Vector3.forward;
                t.Normalize();
                nrm -= t * Vector3.Dot(nrm, t);
                if (nrm.sqrMagnitude < 1e-10f) nrm = Vector3.Cross(t, Vector3.right);
                nrm.Normalize();
                T[i] = t; N[i] = nrm; B[i] = Vector3.Cross(t, nrm);
            }
        }

        static void Sample(Vector3[] C, Vector3[] T, Vector3[] N, Vector3[] B, float t,
                           out Vector3 p, out Vector3 tt, out Vector3 nn, out Vector3 bb)
        {
            int i = Mathf.Clamp((int)t, 0, C.Length - 2);
            float f = Mathf.Clamp01(t - i);
            p = Vector3.LerpUnclamped(C[i], C[i + 1], f);
            tt = Vector3.LerpUnclamped(T[i], T[i + 1], f).normalized;
            nn = Vector3.LerpUnclamped(N[i], N[i + 1], f);
            nn = (nn - tt * Vector3.Dot(nn, tt)).normalized;
            bb = Vector3.Cross(tt, nn);
        }

        static float PolyLength(Vector3[] p)
        {
            float L = 0f;
            for (int i = 1; i < p.Length; i++) L += Vector3.Distance(p[i - 1], p[i]);
            return L;
        }

        static Vector3[] Resample(List<Vector3> pts, int count)
        {
            var s = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) s[i] = s[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float L = s[pts.Count - 1];
            var res = new Vector3[count];
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                float target = L * i / (count - 1);
                while (j < pts.Count - 2 && s[j + 1] < target) j++;
                float f = (target - s[j]) / Mathf.Max(1e-6f, s[j + 1] - s[j]);
                res[i] = Vector3.Lerp(pts[j], pts[j + 1], Mathf.Clamp01(f));
            }
            return res;
        }

        /// <summary>Гистограмма углов → 8 пиков → уточнение k-средними на окружности.</summary>
        static float[] FindArmAngles(List<float> angles)
        {
            const int bins = 180;
            var h = new float[bins];
            foreach (float a in angles) h[Mathf.Clamp((int)((a + Mathf.PI) / (2f * Mathf.PI) * bins), 0, bins - 1)]++;
            var hs = new float[bins];
            float maxH = 0f;
            for (int i = 0; i < bins; i++)
            {
                float sum = 0f;
                for (int d = -2; d <= 2; d++) sum += h[(i + d + bins) % bins];
                hs[i] = sum / 5f;
                maxH = Mathf.Max(maxH, hs[i]);
            }
            var peaks = new List<int>();
            for (int i = 0; i < bins; i++)
                if (hs[i] >= hs[(i + bins - 1) % bins] && hs[i] > hs[(i + 1) % bins] && hs[i] > maxH * 0.15f) peaks.Add(i);
            // слишком много пиков — оставляем самые высокие, разнесённые хотя бы на 15°
            peaks.Sort((a, b) => hs[b].CompareTo(hs[a]));
            var chosen = new List<int>();
            foreach (int p in peaks)
            {
                bool far = true;
                foreach (int c in chosen) if (Mathf.Abs(Mathf.DeltaAngle(p * 2f, c * 2f)) < 15f) far = false;
                if (far) chosen.Add(p);
                if (chosen.Count == Arms) break;
            }
            if (chosen.Count != Arms) return null;

            var cent = new float[Arms];
            for (int k = 0; k < Arms; k++) cent[k] = (chosen[k] + 0.5f) / bins * 2f * Mathf.PI - Mathf.PI;
            for (int it = 0; it < 20; it++)
            {
                var sum = new float[Arms]; var cnt = new int[Arms];
                foreach (float a in angles)
                {
                    int best = 0; float bd = float.MaxValue, bdiff = 0f;
                    for (int k = 0; k < Arms; k++)
                    {
                        float d = Mathf.DeltaAngle(cent[k] * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                        if (Mathf.Abs(d) < bd) { bd = Mathf.Abs(d); best = k; bdiff = d; }
                    }
                    sum[best] += bdiff; cnt[best]++;
                }
                for (int k = 0; k < Arms; k++) if (cnt[k] > 0) cent[k] += sum[k] / cnt[k];
            }
            return cent;
        }

        static bool[,] LargestComponent(bool[,] m, int w, int h)
        {
            var label = new int[w, h];
            int bestLabel = 0, bestSize = 0, next = 0;
            var stack = new Stack<Vector2Int>();
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    if (!m[x, z] || label[x, z] != 0) continue;
                    next++; int size = 0;
                    stack.Push(new Vector2Int(x, z)); label[x, z] = next;
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop(); size++;
                        for (int d = 0; d < 4; d++)
                        {
                            int nx = c.x + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = c.y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                            if (nx < 0 || nz < 0 || nx >= w || nz >= h || !m[nx, nz] || label[nx, nz] != 0) continue;
                            label[nx, nz] = next; stack.Push(new Vector2Int(nx, nz));
                        }
                    }
                    if (size > bestSize) { bestSize = size; bestLabel = next; }
                }
            var r = new bool[w, h];
            for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) r[x, z] = label[x, z] == bestLabel && bestLabel != 0;
            return r;
        }

        static bool[,] Dilate(bool[,] m, int w, int h, int it)
        {
            for (int k = 0; k < it; k++)
            {
                var r = new bool[w, h];
                for (int x = 0; x < w; x++)
                    for (int z = 0; z < h; z++)
                        r[x, z] = m[x, z] || (x > 0 && m[x - 1, z]) || (x < w - 1 && m[x + 1, z]) || (z > 0 && m[x, z - 1]) || (z < h - 1 && m[x, z + 1]);
                m = r;
            }
            return m;
        }

        static bool[,] Erode(bool[,] m, int w, int h, int it)
        {
            for (int k = 0; k < it; k++)
            {
                var r = new bool[w, h];
                for (int x = 1; x < w - 1; x++)
                    for (int z = 1; z < h - 1; z++)
                        r[x, z] = m[x, z] && m[x - 1, z] && m[x + 1, z] && m[x, z - 1] && m[x, z + 1];
                m = r;
            }
            return m;
        }

        static bool[,] FillHoles(bool[,] m, int w, int h)
        {
            var outside = new bool[w, h];
            var stack = new Stack<Vector2Int>();
            for (int x = 0; x < w; x++) { stack.Push(new Vector2Int(x, 0)); stack.Push(new Vector2Int(x, h - 1)); }
            for (int z = 0; z < h; z++) { stack.Push(new Vector2Int(0, z)); stack.Push(new Vector2Int(w - 1, z)); }
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h || outside[c.x, c.y] || m[c.x, c.y]) continue;
                outside[c.x, c.y] = true;
                stack.Push(new Vector2Int(c.x + 1, c.y)); stack.Push(new Vector2Int(c.x - 1, c.y));
                stack.Push(new Vector2Int(c.x, c.y + 1)); stack.Push(new Vector2Int(c.x, c.y - 1));
            }
            var r = new bool[w, h];
            for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) r[x, z] = !outside[x, z];
            return r;
        }

        /// <summary>Расстояние (в единицах модели) от клетки до тела; внутри тела — 0. Двухпроходная фаска 3-4.</summary>
        static float[,] DistanceOutside(bool[,] m, int w, int h)
        {
            var d = new float[w, h];
            const float INF = 1e9f;
            for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) d[x, z] = m[x, z] ? 0f : INF;
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    float v = d[x, z];
                    if (x > 0) v = Mathf.Min(v, d[x - 1, z] + 1f);
                    if (z > 0) v = Mathf.Min(v, d[x, z - 1] + 1f);
                    if (x > 0 && z > 0) v = Mathf.Min(v, d[x - 1, z - 1] + 1.4142f);
                    if (x < w - 1 && z > 0) v = Mathf.Min(v, d[x + 1, z - 1] + 1.4142f);
                    d[x, z] = v;
                }
            for (int x = w - 1; x >= 0; x--)
                for (int z = h - 1; z >= 0; z--)
                {
                    float v = d[x, z];
                    if (x < w - 1) v = Mathf.Min(v, d[x + 1, z] + 1f);
                    if (z < h - 1) v = Mathf.Min(v, d[x, z + 1] + 1f);
                    if (x < w - 1 && z < h - 1) v = Mathf.Min(v, d[x + 1, z + 1] + 1.4142f);
                    if (x > 0 && z < h - 1) v = Mathf.Min(v, d[x - 1, z + 1] + 1.4142f);
                    d[x, z] = v;
                }
            for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) d[x, z] *= Cell;
            return d;
        }
    }
}
