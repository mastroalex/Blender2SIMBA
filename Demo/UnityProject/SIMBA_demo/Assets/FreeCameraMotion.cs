using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FreeCameraController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 3f;
    public float sprintMultiplier = 3f;
    public float verticalSpeed = 3f;

    [Header("Look")]
    public float mouseSensitivity = 0.15f;
    public float maxPitch = 89f;

    private CharacterController controller;

    private float yaw;
    private float pitch;

    private bool mouseLocked;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        Vector3 e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x;

        LockMouse(true);
    }

    void Update()
    {
        UpdateMouseLock();
        UpdateLook();
        UpdateMovement();
    }

    void UpdateMouseLock()
    {
        if (Mouse.current.rightButton.wasPressedThisFrame)
            LockMouse(true);

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            LockMouse(false);
    }

    void UpdateLook()
    {
        if (!mouseLocked)
            return;

        Vector2 delta = Mouse.current.delta.ReadValue();

        yaw += delta.x * mouseSensitivity;
        pitch -= delta.y * mouseSensitivity;

        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void UpdateMovement()
    {
        Vector3 move = Vector3.zero;

        if (Keyboard.current.wKey.isPressed)
            move += transform.forward;

        if (Keyboard.current.sKey.isPressed)
            move -= transform.forward;

        if (Keyboard.current.aKey.isPressed)
            move -= transform.right;

        if (Keyboard.current.dKey.isPressed)
            move += transform.right;

        if (Keyboard.current.eKey.isPressed)
            move += Vector3.up;

        if (Keyboard.current.qKey.isPressed)
            move += Vector3.down;

        if (move.sqrMagnitude > 1f)
            move.Normalize();

        float speed = moveSpeed;

        if (Keyboard.current.leftShiftKey.isPressed)
            speed *= sprintMultiplier;

        controller.Move(move * speed * Time.deltaTime);
    }

    void LockMouse(bool locked)
    {
        mouseLocked = locked;

        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}