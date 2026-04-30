using FishNet.Object;
using UnityEngine;

public class MovementScript : NetworkBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

        if (Input.GetKeyDown(KeyCode.I))
        {
            if (base.IsOwner)
            {
                var pos = new Vector3(0f, 1f, 0f);
                transform.position = pos;
            }

           
        }

        if (Input.GetKeyDown(KeyCode.O))
        {
            if (base.IsOwner)
            {
                var pos = new Vector3(0f, 0f, 0f);
                transform.position = pos;
            }
        }


    }
}
