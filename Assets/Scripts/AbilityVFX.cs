using System.Collections;
using UnityEngine;

// Plays quick primitive-based effects for attacks and abilities so it's easy
// to see what just happened. Added automatically by BoardVisual — no scene
// setup needed. Listens to BoardManager's OnAttackVfx / OnAbilityVfx events,
// which fire BEFORE the board changes, so this script can read the pre-cast
// state (Voltix's charge count, piece positions for Pull, etc.).
public class AbilityVFX : MonoBehaviour
{
    BoardManager board;
    BoardVisual  visual;
    CameraController shakeCam;

    // The player-authored on-hit flipbook, loaded from Resources/OnHit and
    // ordered by frame name (Untitled_Artwork-1 … -6). Cached across the session.
    static Sprite[] hitFrames;
    static Sprite[] HitFrames => hitFrames != null ? hitFrames : (hitFrames = LoadHitFrames());

    static Sprite[] LoadHitFrames()
    {
        var s = Resources.LoadAll<Sprite>("OnHit");
        System.Array.Sort(s, (a, b) => string.CompareOrdinal(a.name, b.name));
        if (s.Length == 0) Debug.LogWarning("AbilityVFX: no on-hit sprites found in Resources/OnHit");
        return s;
    }

    static readonly Color AttackCol   = new(1.00f, 0.35f, 0.25f); // melee hit
    static readonly Color FireCol     = new(1.00f, 0.55f, 0.15f); // Inferno Burst
    static readonly Color WindCol     = new(0.75f, 0.95f, 1.00f); // Gale Slash
    static readonly Color IceCol      = new(0.55f, 0.85f, 1.00f); // Glacial Impact / Vine Snare
    static readonly Color ShockCol    = new(1.00f, 0.95f, 0.30f); // Shockwave
    static readonly Color FortressCol = new(0.65f, 0.65f, 0.70f); // Magnetic Fortress
    static readonly Color BarrierCol  = new(0.70f, 0.40f, 1.00f); // Barrier
    static readonly Color HealCol     = new(0.40f, 1.00f, 0.45f); // Heal
    static readonly Color DecoyCol    = new(0.85f, 0.44f, 0.84f); // Phantom Lantern
    static readonly Color PullCol     = new(0.80f, 0.60f, 1.00f); // Gravitic Distortion

    void Awake()
    {
        board  = GetComponent<BoardManager>();
        visual = GetComponent<BoardVisual>();
    }

    void Start()
    {
        board.OnAttackVfx   += PlayAttack;
        board.OnAbilityVfx  += PlayAbility;
        board.OnDamageTaken += OnDamage;
    }

    Vector3 Tile(int r, int c, float height) => visual.WorldPos(r, c, height);

    void PlayAttack(int r, int c, int tr, int tc)
    {
        // Trail from attacker to victim, then the player-drawn on-hit flipbook
        // bursts on the victim in place of the old primitive impact pop.
        StartCoroutine(Beam(Tile(r, c, 0.45f), Tile(tr, tc, 0.45f), AttackCol));
        StartCoroutine(HitFlipbook(Tile(tr, tc, 0.6f)));
    }

    // Camera kick whenever a piece actually loses shards (any damage source).
    void OnDamage(int r, int c, int amount)
    {
        if (shakeCam == null && Camera.main != null)
            shakeCam = Camera.main.GetComponent<CameraController>();
        // Bigger hits shake a little harder
        if (shakeCam != null) shakeCam.Shake(0.16f, Mathf.Clamp(0.7f + amount * 0.3f, 0.7f, 1.6f));
    }

    void PlayAbility(int r, int c, int tr, int tc, AbilityType type)
    {
        switch (type)
        {
            case AbilityType.Damage:
                StartCoroutine(Projectile(Tile(r, c, 0.5f), Tile(tr, tc, 0.5f), FireCol));
                break;

            case AbilityType.Line:
            {
                // Trace the shot the same way the ability will: stop at the first piece
                int dr = System.Math.Sign(tr - r), dc = System.Math.Sign(tc - c);
                int nr = r + dr, nc = c + dc, er = r, ec = c;
                while (BoardManager.InBounds(nr, nc))
                {
                    er = nr; ec = nc;
                    if (board.Board[nr, nc] != null) break;
                    nr += dr; nc += dc;
                }
                StartCoroutine(Beam(Tile(r, c, 0.45f), Tile(er, ec, 0.45f), WindCol));
                if (board.Board[er, ec] != null)
                    StartCoroutine(ImpactPop(Tile(er, ec, 0.45f), WindCol, 0.5f));
                break;
            }

            case AbilityType.Freeze:
                for (int ddr = -1; ddr <= 1; ddr++) for (int ddc = -1; ddc <= 1; ddc++)
                {
                    int nr = tr + ddr, nc = tc + ddc;
                    if (BoardManager.InBounds(nr, nc)) StartCoroutine(TileFlash(nr, nc, IceCol));
                }
                break;

            case AbilityType.Shockwave:
            {
                var caster = board.Board[r, c];
                int radius = caster != null && caster.staticCharges > 0 ? caster.staticCharges : 1;
                StartCoroutine(RingWave(Tile(r, c, 0.15f), (radius + 0.5f) * visual.tileSize, ShockCol));
                break;
            }

            case AbilityType.Fortress:
                StartCoroutine(Dome(Tile(r, c, 0.35f), 1.4f, FortressCol));
                break;

            case AbilityType.Barrier:
                StartCoroutine(Dome(Tile(tr, tc, 0.45f), 0.95f, BarrierCol));
                break;

            case AbilityType.Heal:
                for (int ddr = -1; ddr <= 1; ddr++) for (int ddc = -1; ddc <= 1; ddc++)
                {
                    if (ddr == 0 && ddc == 0) continue;
                    int nr = r + ddr, nc = c + ddc;
                    if (!BoardManager.InBounds(nr, nc)) continue;
                    var ally = board.Board[nr, nc];
                    var self = board.Board[r, c];
                    if (ally != null && self != null && ally.player == self.player && ally.shards < ally.maxShards)
                        StartCoroutine(RisingSpark(Tile(nr, nc, 0.3f), HealCol));
                }
                break;

            case AbilityType.Decoy:
                StartCoroutine(ImpactPop(Tile(tr, tc, 0.45f), DecoyCol, 0.6f));
                break;

            case AbilityType.Pull:
            {
                var caster = board.Board[r, c];
                int range = caster != null ? caster.ability.range : 3;
                for (int rr = 0; rr < BoardManager.BS; rr++) for (int cc = 0; cc < BoardManager.BS; cc++)
                {
                    if ((rr == r && cc == c) || board.Board[rr, cc] == null) continue;
                    if (BoardManager.Cheb(rr, cc, r, c) <= range)
                        StartCoroutine(PullStreak(Tile(rr, cc, 0.4f), Tile(r, c, 0.45f), PullCol));
                }
                StartCoroutine(RingWave(Tile(r, c, 0.15f), (range + 0.5f) * visual.tileSize, PullCol));
                break;
            }
        }
    }

    // ── effect building blocks ────────────────────────────────────────────────

    GameObject Spawn(PrimitiveType type, Vector3 pos, Vector3 scale, Color col)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.position   = pos;
        go.transform.localScale = scale;
        FlatKitMaterials.Tint(go, col);
        return go;
    }

    // Sphere that pops outward then vanishes
    IEnumerator ImpactPop(Vector3 pos, Color col, float size)
    {
        var go = Spawn(PrimitiveType.Sphere, pos, Vector3.one * 0.1f, col);
        float t = 0f, dur = 0.3f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = t / dur;
            float s = k < 0.6f ? Mathf.Lerp(0.1f, size, k / 0.6f)
                               : Mathf.Lerp(size, 0f, (k - 0.6f) / 0.4f);
            go.transform.localScale = Vector3.one * s;
            yield return null;
        }
        Destroy(go);
    }

    // Camera-facing sprite flipbook that plays the on-hit frames once and vanishes
    IEnumerator HitFlipbook(Vector3 pos)
    {
        var frames = HitFrames;
        if (frames == null || frames.Length == 0) yield break;

        var go = new GameObject("OnHitEffect");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];

        // Normalize to a steady world size no matter the source PNG resolution
        float target  = visual.tileSize * 2.4f; // overall size of the hit sprite
        float camPull = 1.4f;                    // how far to float it off the piece toward the camera
        float srcSize = Mathf.Max(frames[0].bounds.size.x, frames[0].bounds.size.y);
        go.transform.localScale = Vector3.one * (srcSize > 0.0001f ? target / srcSize : 1f);
        go.transform.position   = pos;

        var cam = Camera.main;
        float dur = 0.32f, t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            int idx = Mathf.Clamp(Mathf.FloorToInt(t / dur * frames.Length), 0, frames.Length - 1);
            sr.sprite = frames[idx];
            if (cam != null)
            {
                // Sit between the piece and the camera so nothing occludes it
                Vector3 toCam = cam.transform.position - pos;
                go.transform.position = pos + toCam.normalized * camPull;
                go.transform.rotation = cam.transform.rotation; // billboard toward camera
            }
            yield return null;
        }
        Destroy(go);
    }

    // Small orb that arcs from caster to target, then bursts
    IEnumerator Projectile(Vector3 from, Vector3 to, Color col)
    {
        var go = Spawn(PrimitiveType.Sphere, from, Vector3.one * 0.18f, col);
        float t = 0f, dur = 0.22f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            var p = Vector3.Lerp(from, to, k);
            p.y += Mathf.Sin(k * Mathf.PI) * 0.4f;
            go.transform.position = p;
            yield return null;
        }
        Destroy(go);
        StartCoroutine(ImpactPop(to, col, 0.5f));
    }

    // Thin cylinder stretched between two points that fades out
    IEnumerator Beam(Vector3 from, Vector3 to, Color col)
    {
        float len = Vector3.Distance(from, to);
        var go = Spawn(PrimitiveType.Cylinder, (from + to) * 0.5f, new Vector3(0.15f, len * 0.5f, 0.15f), col);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, (to - from).normalized);
        float t = 0f, dur = 0.3f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float w = Mathf.Lerp(0.15f, 0f, t / dur);
            go.transform.localScale = new Vector3(w, len * 0.5f, w);
            yield return null;
        }
        Destroy(go);
    }

    // Flat square that flashes on one tile
    IEnumerator TileFlash(int r, int c, Color col)
    {
        float full = visual.tileSize * 0.9f;
        var go = Spawn(PrimitiveType.Cube, Tile(r, c, 0.12f), new Vector3(0.2f, 0.05f, 0.2f), col);
        float t = 0f, dur = 0.4f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = t / dur;
            float s = k < 0.5f ? Mathf.Lerp(0.2f, full, k / 0.5f)
                               : Mathf.Lerp(full, 0f, (k - 0.5f) / 0.5f);
            go.transform.localScale = new Vector3(s, 0.05f, s);
            yield return null;
        }
        Destroy(go);
    }

    // Flat disc that expands outward from a point
    IEnumerator RingWave(Vector3 center, float worldRadius, Color col)
    {
        var go = Spawn(PrimitiveType.Cylinder, center, new Vector3(0.2f, 0.03f, 0.2f), col);
        float t = 0f, dur = 0.35f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float d = Mathf.Lerp(0.2f, worldRadius * 2f, t / dur);
            go.transform.localScale = new Vector3(d, 0.03f, d);
            yield return null;
        }
        Destroy(go);
    }

    // Sphere shell that swells up around a piece, holds, then shrinks
    IEnumerator Dome(Vector3 center, float size, Color col)
    {
        var go = Spawn(PrimitiveType.Sphere, center, Vector3.one * 0.2f, col);
        float t = 0f, dur = 0.5f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = t / dur;
            float s = k < 0.35f ? Mathf.Lerp(0.2f, size, k / 0.35f)
                    : k < 0.65f ? size
                                : Mathf.Lerp(size, 0f, (k - 0.65f) / 0.35f);
            go.transform.localScale = Vector3.one * s;
            yield return null;
        }
        Destroy(go);
    }

    // Little mote that floats upward and disappears
    IEnumerator RisingSpark(Vector3 pos, Color col)
    {
        var go = Spawn(PrimitiveType.Sphere, pos, Vector3.one * 0.14f, col);
        float t = 0f, dur = 0.45f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = t / dur;
            go.transform.position   = pos + Vector3.up * (k * 0.6f);
            go.transform.localScale = Vector3.one * Mathf.Lerp(0.14f, 0.02f, k);
            yield return null;
        }
        Destroy(go);
    }

    // Small orb dragged from a piece toward the caster
    IEnumerator PullStreak(Vector3 from, Vector3 to, Color col)
    {
        var go = Spawn(PrimitiveType.Sphere, from, Vector3.one * 0.14f, col);
        float t = 0f, dur = 0.3f;
        while (t < dur)
        {
            t += Time.deltaTime;
            go.transform.position = Vector3.Lerp(from, to, t / dur);
            yield return null;
        }
        Destroy(go);
    }
}
