using UnityEngine;

// Attached to each piece's root object by BoardVisual.
// The root glides toward targetPos every frame. While "carried" (drag & drop)
// it floats with extra lag and leans in the direction it is moving.
public class PieceView : MonoBehaviour
{
    public Vector3 targetPos;
    public bool    carried;

    // Shield visuals, created by BoardVisual.CreateView and toggled per refresh
    [HideInInspector] public GameObject shieldBubble;    // translucent orb around the body
    [HideInInspector] public GameObject barrierPip;      // cyan pip beside the shard row
    [HideInInspector] public GameObject energyPip;       // violet pip (Aegis Energy Shield)
    [HideInInspector] public Vector3    bubbleBaseScale; // pulse animates around this

    const float MoveSmoothTime  = 0.09f; // normal glide between tiles
    const float CarrySmoothTime = 0.16f; // floatier while held, drifts behind the cursor
    const float TiltPerSpeed    = 5f;    // degrees of lean per unit/sec of drift velocity
    const float MaxTilt         = 20f;
    const float TiltRecover     = 10f;   // how quickly it levels back out

    Vector3 velocity;

    void Update()
    {
        float smooth = carried ? CarrySmoothTime : MoveSmoothTime;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, smooth);

        // Lean into the direction of travel while carried, settle flat otherwise
        Quaternion targetRot = Quaternion.identity;
        if (carried)
        {
            float tiltX = Mathf.Clamp( velocity.z * TiltPerSpeed, -MaxTilt, MaxTilt);
            float tiltZ = Mathf.Clamp(-velocity.x * TiltPerSpeed, -MaxTilt, MaxTilt);
            targetRot = Quaternion.Euler(tiltX, 0f, tiltZ);
        }
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * TiltRecover);

        // Active shield bubble breathes gently and drifts around so it reads
        // as an energy field rather than a solid ball
        if (shieldBubble != null && shieldBubble.activeSelf)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 3f) * 0.05f;
            shieldBubble.transform.localScale = bubbleBaseScale * pulse;
            shieldBubble.transform.Rotate(0f, 25f * Time.deltaTime, 0f);
        }
    }

    // Place instantly with no glide (used when the piece is first created)
    public void SnapTo(Vector3 pos)
    {
        targetPos = pos;
        transform.position = pos;
        velocity = Vector3.zero;
    }
}
