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

    bool  freeCam;
    float currentAzimuth;
    float targetAzimuth;
    float azimuthVelocity;

    void Start()
    {
        targetAzimuth  = 45f;
        currentAzimuth = 45f;
        ApplyOrbit();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            freeCam = !freeCam;
            Debug.Log(freeCam ? "[Camera] Free cam ON" : "[Camera] Locked cam ON");
        }

        HandleRotation();

        if (freeCam) HandleFly();
        else         ApplyOrbit();
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
