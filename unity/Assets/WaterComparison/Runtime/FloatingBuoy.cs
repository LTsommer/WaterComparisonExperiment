using UnityEngine;

public sealed class FloatingBuoy : MonoBehaviour
{
    [SerializeField] private float waterlineOffset = 0.38f;
    [SerializeField, Range(0.01f, 1f)] private float rotationResponse = 0.09f;
    private Vector2 restPosition;

    private void Awake() => restPosition = new Vector2(transform.position.x, transform.position.z);

    private void LateUpdate()
    {
        var time = Time.time;
        var displaced = new Vector3(restPosition.x, 0f, restPosition.y);
        var derivativeX = Vector3.right;
        var derivativeZ = Vector3.forward;

        AccumulateWave(restPosition, new Vector2(0.9563f, 0.2924f), 0.420f, 0.25438f, 1.579f, 0.72f, 0.37f, time, ref displaced, ref derivativeX, ref derivativeZ);
        AccumulateWave(restPosition, new Vector2(0.8462f, 0.5329f), 0.260f, 0.38547f, 1.944f, 0.62f, 2.13f, time, ref displaced, ref derivativeX, ref derivativeZ);

        var lowBandOffset = new Vector2(displaced.x - restPosition.x, displaced.z - restPosition.y);
        var warpedPosition = restPosition + lowBandOffset * 1.35f;
        AccumulateWave(warpedPosition, new Vector2(0.9959f, -0.0905f), 0.160f, 0.57644f, 2.378f, 0.56f, 4.71f, time, ref displaced, ref derivativeX, ref derivativeZ);
        AccumulateWave(warpedPosition, new Vector2(0.6718f, 0.7407f), 0.095f, 0.88496f, 2.947f, 0.50f, 1.29f, time, ref displaced, ref derivativeX, ref derivativeZ);
        AccumulateWave(warpedPosition, new Vector2(0.9231f, -0.3846f), 0.052f, 1.46121f, 3.785f, 0.42f, 5.53f, time, ref displaced, ref derivativeX, ref derivativeZ);
        AccumulateWave(warpedPosition, new Vector2(0.4307f, 0.9025f), 0.025f, 2.37000f, 4.821f, 0.32f, 3.34f, time, ref displaced, ref derivativeX, ref derivativeZ);

        transform.position = new Vector3(displaced.x, displaced.y + waterlineOffset, displaced.z);
        var surfaceNormal = Vector3.Cross(derivativeZ, derivativeX).normalized;
        var targetRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationResponse);
    }

    private static void AccumulateWave(
        Vector2 surfacePosition,
        Vector2 direction,
        float amplitude,
        float waveNumber,
        float angularFrequency,
        float chop,
        float phaseOffset,
        float time,
        ref Vector3 displaced,
        ref Vector3 derivativeX,
        ref Vector3 derivativeZ)
    {
        var phase = waveNumber * Vector2.Dot(direction, surfacePosition) + angularFrequency * time + phaseOffset;
        var sine = Mathf.Sin(phase);
        var cosine = Mathf.Cos(phase);
        var horizontalAmplitude = amplitude * chop;
        displaced.x += horizontalAmplitude * direction.x * cosine;
        displaced.z += horizontalAmplitude * direction.y * cosine;
        displaced.y += amplitude * sine;

        var horizontalSlope = horizontalAmplitude * waveNumber;
        var verticalSlope = amplitude * waveNumber;
        derivativeX += new Vector3(
            -horizontalSlope * direction.x * direction.x * sine,
            verticalSlope * direction.x * cosine,
            -horizontalSlope * direction.x * direction.y * sine);
        derivativeZ += new Vector3(
            -horizontalSlope * direction.x * direction.y * sine,
            verticalSlope * direction.y * cosine,
            -horizontalSlope * direction.y * direction.y * sine);
    }
}
