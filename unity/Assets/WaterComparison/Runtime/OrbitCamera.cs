using UnityEngine;

public sealed class OrbitCamera : MonoBehaviour
{
    [SerializeField] private Vector3 target = Vector3.zero;
    [SerializeField] private float distance = 19.5f;
    [SerializeField] private float yaw = 38f;
    [SerializeField] private float pitch = 20f;

    private void Start() => UpdateTransform();

    private void LateUpdate()
    {
        if (Input.GetMouseButton(0))
        {
            yaw += Input.GetAxis("Mouse X") * 5f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 4f, 8f, 80f);
        }

        distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y, 7f, 34f);
        UpdateTransform();
    }

    private void UpdateTransform()
    {
        transform.position = target + Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -distance);
        transform.LookAt(target);
    }
}
