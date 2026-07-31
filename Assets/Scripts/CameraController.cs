using UnityEngine;

// Attach to the Main Camera.
// Locked mode (default): Q/E rotate 90° around board center.
// Free mode: WASD fly, Q/E still rotate. Toggle with C key.
public class CameraController : MonoBehaviour
{
    [Header("Orbit (Locked mode)")]
    public Vector3 orbitCenter    = Vector3.zero;
    public float   orbitDistance  = 13f;
    public float   orbitElevation = 45f;

    [Header("Snap Spring")]
    public float snapStiffness = 600f;  // higher = faster snap
    public float snapDamping   = 38f;   // lower relative to stiffness = more jitter

    [Header("Free Cam")]
    public float flySpeed = 8f;
    public float flyShift = 20f;

    [Header("Shake")]
    public float shakeMagnitude = 0.22f; // world-space amplitude of a damage shake
    public float shakeFrequency = 28f;   // how jittery the shake feels

    bool  freeCam;
    float currentAzimuth;
    float targetAzimuth;
    float azimuthVelocity;

    // Additive damage-shake state. The offset is removed at the start of each
    // Update before the camera repositions, so it never drifts the free cam.
    float   shakeTime;
    float   shakeDuration;
    float   shakeStrength;
    Vector3 shakeOffset;

    // Kick the camera. Call whenever a piece takes damage.
    public void Shake(float duration = 0.16f, float strength = 1f)
    {
        // Don't let a fresh light hit cut short a bigger ongoing shake
        if (shakeTime <= 0f || strength >= shakeStrength)
        {
            shakeDuration = duration;
            shakeStrength = strength;
        }
        shakeTime = Mathf.Max(shakeTime, duration);
    }

    void Start()
    {
        targetAzimuth  = 45f;
        currentAzimuth = 45f;
        ApplyOrbit();
    }

    void Update()
    {
        // Undo last frame's shake so orbit/fly math works from the true position
        transform.position -= shakeOffset;
        shakeOffset = Vector3.zero;

        if (Input.GetKeyDown(KeyCode.C))
        {
            freeCam = !freeCam;
            Debug.Log(freeCam ? "[Camera] Free cam ON" : "[Camera] Locked cam ON");
        }

        HandleRotation();

        if (freeCam) HandleFly();
        else         ApplyOrbit();

        ApplyShake();
    }

    // Random offset that decays over the shake's lifetime, layered on top of
    // whatever position orbit/fly just set.
    void ApplyShake()
    {
        if (shakeTime <= 0f) return;

        shakeTime -= Time.deltaTime;
        float falloff = Mathf.Clamp01(shakeTime / shakeDuration);
        float amp     = shakeMagnitude * shakeStrength * falloff;
        float t       = Time.time * shakeFrequency;

        // Two out-of-phase sine axes read as a sharp jolt rather than white noise
        shakeOffset = new Vector3(Mathf.Sin(t) * amp, Mathf.Sin(t * 1.3f + 1.7f) * amp * 0.6f, Mathf.Cos(t * 0.9f) * amp);
        transform.position += shakeOffset;
    }

    void HandleRotation()
    {
        if (Input.GetKeyDown(KeyCode.Q)) targetAzimuth += 90f;
        if (Input.GetKeyDown(KeyCode.E)) targetAzimuth -= 90f;

        // Underdamped spring on azimuth — overshoots slightly then snaps into place
        float displacement = Mathf.DeltaAngle(currentAzimuth, targetAzimuth);
        float spring  = displacement * snapStiffness;
        float damper  = -azimuthVelocity * snapDamping;
        azimuthVelocity += (spring + damper) * Time.deltaTime;
        currentAzimuth  += azimuthVelocity  * Time.deltaTime;
    }

    void ApplyOrbit()
    {
        float yaw   = currentAzimuth * Mathf.Deg2Rad;
        float pitch = orbitElevation  * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            Mathf.Sin(yaw)   * Mathf.Cos(pitch),
            Mathf.Sin(pitch),
            Mathf.Cos(yaw)   * Mathf.Cos(pitch)
        ) * orbitDistance;

        transform.position = orbitCenter + offset;
        transform.LookAt(orbitCenter, Vector3.up);
    }

    void HandleFly()
    {
        float speed = Input.GetKey(KeyCode.LeftShift) ? flyShift : flySpeed;

        Vector3 fwd   = transform.forward; fwd.y   = 0; fwd.Normalize();
        Vector3 right = transform.right;   right.y = 0; right.Normalize();

        if (Input.GetKey(KeyCode.W))           transform.position += fwd   *  speed * Time.deltaTime;
        if (Input.GetKey(KeyCode.S))           transform.position += fwd   * -speed * Time.deltaTime;
        if (Input.GetKey(KeyCode.A))           transform.position += right * -speed * Time.deltaTime;
        if (Input.GetKey(KeyCode.D))           transform.position += right *  speed * Time.deltaTime;
        if (Input.GetKey(KeyCode.Space))        transform.position += Vector3.up *  speed * Time.deltaTime;
        if (Input.GetKey(KeyCode.LeftControl))  transform.position += Vector3.up * -speed * Time.deltaTime;

        transform.rotation = Quaternion.Euler(orbitElevation, currentAzimuth, 0);
    }
}
