using UnityEngine;

public class Spinner : MonoBehaviour
{
    public float rotationSpeed = 180f; // degrees per second

    void Update()
    {
        transform.Rotate(0, 0, -rotationSpeed * Time.deltaTime); // counterclockwise
    }
}
