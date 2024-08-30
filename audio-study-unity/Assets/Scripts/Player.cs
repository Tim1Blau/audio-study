using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class Player : MonoBehaviour
{
    [SerializeField] float maxVerticalLookAngle = 80.0f;
    [SerializeField] float sensitivity = 2.0f;
    [SerializeField] float movementSpeed = 5.0f;
    [SerializeField] public Camera mainCamera;
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
    bool _escaped;
    Vector2 _rotation;

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _rotation = mainCamera.transform.localRotation.eulerAngles;
    }

    void LateUpdate()
    {
        if (!ShowMouse && Input.GetKeyDown(KeyCode.Escape))
            _escaped = ShowMouse = true;
        else if (Input.GetMouseButtonDown(0) && _escaped)
            _escaped = ShowMouse = false;

        // Rotation.
        if (!ShowMouse)
        {
            var mouseInput = new Vector2(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"));
            _rotation += sensitivity * mouseInput;
            _rotation.y = Mathf.Clamp(_rotation.y, -maxVerticalLookAngle, maxVerticalLookAngle);
            mainCamera.transform.localRotation = Quaternion.Euler(_rotation.x, _rotation.y, 0.0f);
        }

        // Position
        var moveDir = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
        moveDir = mainCamera.transform.localRotation * moveDir;
        moveDir.y = 0.0f;
        _characterController.SimpleMove((canMove ? movementSpeed : 0f) * moveDir);
    }
}