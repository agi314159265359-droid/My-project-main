
using UnityEngine;
using UnityEngine.UI;


namespace Mikk.Avatar.Expression
{
    public class CameraController : MonoBehaviour
    {

        public float rotationSpeed = 0.2f;
        public float returnSpeed = 3f;
        public float maxVerticalAngle = 25f; // prevents flipping

        private Vector2 lastTouchPos;
        private bool isDragging = false;

        private float currentX;
        private float currentY;

        private Quaternion originalRotation;

        private Vector3 minePosition;



        void Start()
        {
            

            // Save original rotation (0,90,0 in your case)
            originalRotation = transform.rotation;

            Vector3 angles = transform.eulerAngles;
            currentX = angles.y;
            currentY = angles.x;

            minePosition = transform.position;


           

        }


        void Update()
        {
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);

                if (touch.phase == TouchPhase.Began)
                {
                    isDragging = true;
                    lastTouchPos = touch.position;
                }
                else if (touch.phase == TouchPhase.Moved)
                {
                    Vector2 delta = touch.position - lastTouchPos;

                    currentX += delta.x * rotationSpeed;
                    currentY -= delta.y * rotationSpeed;

                    currentY = Mathf.Clamp(currentY, -maxVerticalAngle, maxVerticalAngle);

                    transform.rotation = Quaternion.Euler(currentY, currentX, 0);

                    lastTouchPos = touch.position;
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    isDragging = false;
                }
            }

            // Smooth return when user stops touching
            if (!isDragging)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    originalRotation,
                    Time.deltaTime * returnSpeed
                );

                Vector3 angles = transform.eulerAngles;
                currentX = angles.y;
                currentY = angles.x;
            }


           


        }



        public void slideSide(string fake)
        {

            if (minePosition == new Vector3(-2f, 1f, 0f))
            {
                var postion = new Vector3(-2f, 1f, 90f);
                gameObject.transform.position = postion;
            }
            else
            {
                var postion = new Vector3(-2f, 1f, 0f);
                gameObject.transform.position = postion;
            }






        }


        public void slideHisSide(string fake)
        {

            if (transform.position == new Vector3(-2f, 1f, 0f))
            {
                var postion = new Vector3(-2f, 1f, 90f);
                gameObject.transform.position = postion;
            }
            else if (transform.position == new Vector3(-2f, 1f, 90f))
            {
                var postion = new Vector3(-2f, 1f, 0f);
                gameObject.transform.position = postion;
            }
            else if (transform.position == new Vector3(-1.5f, 1.15f, 0f))
            {
                var postion = new Vector3(-1.5f, 1.15f, 90f);
                gameObject.transform.position = postion;
            }
            else if (transform.position == new Vector3(-1.5f, 1.15f, 90f))
            {
                var postion = new Vector3(-1.5f, 1.15f, 0f);
                gameObject.transform.position = postion;

            }






        }

      public  void slideMySide(string fake)
        {
            if (transform.position == new Vector3(-2f, 1f, 0f))
            {
                var postion = new Vector3(-2f, 1f, 90f);
                gameObject.transform.position = postion;
            }
            else if (transform.position == new Vector3(-2f, 1f, 90f))
            {
                var postion = new Vector3(-2f, 1f, 0f);
                gameObject.transform.position = postion;
            }
            else if (transform.position == new Vector3(-1.5f, 1.15f, 0f))
            {
                var postion = new Vector3(-1.5f, 1.15f, 90f);
                gameObject.transform.position = postion;
            }
            else if (transform.position == new Vector3(-1.5f, 1.15f, 90f))
            {
                var postion = new Vector3(-1.5f, 1.15f, 0f);
                gameObject.transform.position = postion;

            }







        }


        public void zoomINCamera(string input)
        {
            if (transform.position == new Vector3(-2f, 1f, 0f))
            {
                var postion = new Vector3(-1.4f, 1.16f, 0f);
                gameObject.transform.position = postion;
            }
            else if(transform.position == new Vector3(-2f, 1f, 90f))
            {
                var postion = new Vector3(-1.4f, 1.16f, 90f);
                gameObject.transform.position = postion;
            }



           


        }

        







    }

}