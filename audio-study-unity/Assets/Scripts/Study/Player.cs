using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    [SerializeField] public const float maxVerticalLookAngle = 80.0f;
    [SerializeField] public const float sensitivity = 1.5f;
    [SerializeField] public const float movementSpeed = 5.0f;
    [SerializeField] public new Camera camera;
    public bool canMove = true;

    public bool ShowMouse
    {
        get => Cursor.visible;
        set
        {
            Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = value;
        }
    }

    CharacterController _characterController;
    Vector2 _rotation;

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _rotation = camera.transform.localRotation.eulerAngles;
    }


    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            ShowMouse = true;
        else if (Input.GetMouseButtonDown(0) && !Map.Singleton.IsFocused)
            ShowMouse = false;
        // Rotation.
        if (!ShowMouse)
        {
            var mouseInput = new Vector2(Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y"));
            _rotation += sensitivity * mouseInput;
            _rotation.y = Mathf.Clamp(_rotation.y, -maxVerticalLookAngle, maxVerticalLookAngle);
            camera.transform.localRotation = Quaternion.Euler(_rotation.y, _rotation.x, 0.0f);
        }

        // Position
        // var moveDir = new Vector3(
        //     (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), 0,
        //     (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
        var moveDir = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        moveDir = Quaternion.Euler(0, _rotation.x, 0) * moveDir;
        var mag = moveDir.magnitude;
        if (mag > 1f) moveDir /= mag;
        _characterController.SimpleMove((canMove ? movementSpeed : 0f) * moveDir);
    }
}