using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Calabash.Movement
{
    /// <summary>Снимок ввода за один кадр. Его же подаёт самотест вместо живой клавиатуры.</summary>
    public struct MovementInput
    {
        /// <summary>x — стрейф (A/D), y — вперёд/назад (W/S), длина ≤ 1.</summary>
        public Vector2 move;
        public bool jumpPressed;
        public bool jumpHeld;
        public bool dashPressed;
        /// <summary>Слайд на земле / граунд-слэм в воздухе (одна кнопка, как в ULTRAKILL).</summary>
        public bool slidePressed;
        public bool slideHeld;
    }

    /// <summary>
    /// Читает клавиатуру, мышь и геймпад. Все клавиши настраиваются в инспекторе.
    /// Работает и с новым Input System (по умолчанию в Unity 6), и со старым Input Manager.
    /// </summary>
    [DisallowMultipleComponent]
    public class ZhdanInput : MonoBehaviour
    {
#if ENABLE_INPUT_SYSTEM
        [Header("Клавиатура")]
        public Key forward = Key.W;
        public Key back = Key.S;
        public Key left = Key.A;
        public Key right = Key.D;
        public Key jump = Key.Space;
        public Key dash = Key.LeftShift;
        [Tooltip("Слайд на земле / граунд-слэм в воздухе")]
        public Key slide = Key.LeftCtrl;
        [Tooltip("Дублирующая клавиша слайда (Ctrl неудобен на некоторых раскладках)")]
        public Key slideAlt = Key.C;

        [Header("Мышь")]
        [Tooltip("Дэш также на боковую кнопку мыши (Mouse 4)")]
        public bool dashOnMouseButton4 = true;

        [Header("Геймпад")]
        public GamepadButton jumpButton = GamepadButton.South;
        public GamepadButton dashButton = GamepadButton.RightShoulder;
        public GamepadButton slideButton = GamepadButton.East;
        [Tooltip("Скорость поворота камеры правым стиком, град/с")]
        public float gamepadLookSpeed = 220f;
#else
        [Header("Клавиатура (старый Input Manager)")]
        public KeyCode forward = KeyCode.W;
        public KeyCode back = KeyCode.S;
        public KeyCode left = KeyCode.A;
        public KeyCode right = KeyCode.D;
        public KeyCode jump = KeyCode.Space;
        public KeyCode dash = KeyCode.LeftShift;
        public KeyCode slide = KeyCode.LeftControl;
        public KeyCode slideAlt = KeyCode.C;
#endif

        [Header("Обзор")]
        [Tooltip("Градусов поворота на пиксель движения мыши")]
        public float mouseSensitivity = 0.1f;
        public bool invertY = false;

        public MovementInput ReadMovement()
        {
            var i = new MovementInput();
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb[forward].isPressed) move.y += 1f;
                if (kb[back].isPressed) move.y -= 1f;
                if (kb[right].isPressed) move.x += 1f;
                if (kb[left].isPressed) move.x -= 1f;
                i.jumpPressed |= kb[jump].wasPressedThisFrame;
                i.jumpHeld |= kb[jump].isPressed;
                i.dashPressed |= kb[dash].wasPressedThisFrame;
                i.slidePressed |= kb[slide].wasPressedThisFrame || kb[slideAlt].wasPressedThisFrame;
                i.slideHeld |= kb[slide].isPressed || kb[slideAlt].isPressed;
            }
            if (mouse != null && dashOnMouseButton4)
                i.dashPressed |= mouse.backButton.wasPressedThisFrame;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > move.sqrMagnitude) move = stick;
                i.jumpPressed |= pad[jumpButton].wasPressedThisFrame;
                i.jumpHeld |= pad[jumpButton].isPressed;
                i.dashPressed |= pad[dashButton].wasPressedThisFrame;
                i.slidePressed |= pad[slideButton].wasPressedThisFrame;
                i.slideHeld |= pad[slideButton].isPressed;
            }
            i.move = Vector2.ClampMagnitude(move, 1f);
#else
            Vector2 move = Vector2.zero;
            if (Input.GetKey(forward)) move.y += 1f;
            if (Input.GetKey(back)) move.y -= 1f;
            if (Input.GetKey(right)) move.x += 1f;
            if (Input.GetKey(left)) move.x -= 1f;
            i.move = Vector2.ClampMagnitude(move, 1f);
            i.jumpPressed = Input.GetKeyDown(jump);
            i.jumpHeld = Input.GetKey(jump);
            i.dashPressed = Input.GetKeyDown(dash);
            i.slidePressed = Input.GetKeyDown(slide) || Input.GetKeyDown(slideAlt);
            i.slideHeld = Input.GetKey(slide) || Input.GetKey(slideAlt);
#endif
            return i;
        }

        /// <summary>Поворот камеры за кадр в градусах: x — рыскание, y — тангаж (вверх +).</summary>
        public Vector2 ReadLookDegrees(float dt)
        {
            Vector2 d = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) d += Mouse.current.delta.ReadValue() * mouseSensitivity;
            if (Gamepad.current != null) d += Gamepad.current.rightStick.ReadValue() * gamepadLookSpeed * dt;
#else
            d += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * mouseSensitivity * 20f;
#endif
            if (invertY) d.y = -d.y;
            return d;
        }

        public bool UnlockCursorPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        public bool LockCursorPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}
