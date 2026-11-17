using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpectraMotions.Tools
{
    public class FreeFlyCamera : MonoBehaviour
    {
        public float lookSpeed = 2.0f;
        public float moveSpeed = 5.0f;
        public float fastMoveSpeed = 15.0f;

        float yaw = 0f;
        float pitch = 0f;

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        void Update()
        {
            // Mouse look
            yaw += lookSpeed * Input.GetAxis("Mouse X");
            pitch -= lookSpeed * Input.GetAxis("Mouse Y");
            pitch = Mathf.Clamp(pitch, -90f, 90f);
            transform.eulerAngles = new Vector3(pitch, yaw, 0.0f);

            // Movement
            float speed = Input.GetKey(KeyCode.LeftShift) ? fastMoveSpeed : moveSpeed;

            Vector3 direction = new Vector3(
                Input.GetAxis("Horizontal"),
                0,
                Input.GetAxis("Vertical")
            );

            Vector3 move = transform.TransformDirection(direction) * speed * Time.deltaTime;

            // Vertical movement using Q (up) and E (down)
            if (Input.GetKey(KeyCode.Q)) move.y += speed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) move.y -= speed * Time.deltaTime;

            transform.position += move;

            // Escape: exit play mode
            if (Input.GetKeyDown(KeyCode.Escape))
            {
    #if UNITY_EDITOR
                EditorApplication.isPlaying = false;
    #else
                Application.Quit();
    #endif
            }
        }
    }
}
