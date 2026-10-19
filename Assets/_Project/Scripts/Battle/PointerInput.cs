using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ZombieSLG.Battle
{
    /// <summary>
    /// Mouse/touch unified through the Input System's Pointer. Knows whether a press started on UI
    /// so a tap on the skill button never also moves the hero.
    /// </summary>
    public sealed class PointerInput
    {
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        private PointerEventData _eventData;
        private EventSystem _eventDataOwner;

        public Vector2 Position { get; private set; }
        public bool PressedThisFrame { get; private set; }
        public bool ReleasedThisFrame { get; private set; }
        public bool IsPressed { get; private set; }
        public bool OverUi { get; private set; }
        public bool PressStartedOverUi { get; private set; }
        public bool CancelPressed { get; private set; }

        public void Update()
        {
            var pointer = Pointer.current;
            if (pointer == null)
            {
                PressedThisFrame = ReleasedThisFrame = IsPressed = false;
                return;
            }

            Position = pointer.position.ReadValue();
            PressedThisFrame = pointer.press.wasPressedThisFrame;
            ReleasedThisFrame = pointer.press.wasReleasedThisFrame;
            IsPressed = pointer.press.isPressed;
            OverUi = IsOverUi(Position);
            if (PressedThisFrame) PressStartedOverUi = OverUi;

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            CancelPressed = (mouse != null && mouse.rightButton.wasPressedThisFrame)
                            || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
        }

        public static bool KeyDown(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        private bool IsOverUi(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            if (_eventData == null || _eventDataOwner != es)
            {
                _eventData = new PointerEventData(es);
                _eventDataOwner = es;
            }
            _eventData.position = screen;
            _uiHits.Clear();
            es.RaycastAll(_eventData, _uiHits);
            return _uiHits.Count > 0;
        }
    }
}
