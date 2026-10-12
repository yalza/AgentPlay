using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AgentPlay.InputDriving
{
    /// <summary>
    /// A virtual gamepad for game adapters. Actions stage stick and button values during the frame;
    /// Flush() (once per frame, in LateUpdate) queues them for the next input update and resets the stage,
    /// so anything not set again returns to rest. While it exists, input keeps flowing when the Editor is unfocused.
    /// </summary>
    public sealed class VirtualGamepad : IDisposable
    {
        readonly InputSettings.BackgroundBehavior _previousBackground;
        GamepadState _staged;
        bool _disposed;

        public Gamepad Device { get; }

        VirtualGamepad()
        {
            Device = InputSystem.AddDevice<Gamepad>("AgentPlayVirtualGamepad");
            _previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _staged = new GamepadState();
            Debug.Log("[AgentPlay] virtual gamepad created");
        }

        public static VirtualGamepad Create() => new VirtualGamepad();

        public void SetLeftStick(Vector2 value) => _staged.leftStick = Vector2.ClampMagnitude(value, 1f);

        public void SetRightStick(Vector2 value) => _staged.rightStick = Vector2.ClampMagnitude(value, 1f);

        public void SetRightTrigger(bool pressed) => _staged.rightTrigger = pressed ? 1f : 0f;

        public void SetButton(GamepadButton button, bool pressed) => _staged = _staged.WithButton(button, pressed);

        public void Flush()
        {
            if (_disposed || !Device.added) return;
            InputSystem.QueueStateEvent(Device, _staged);
            _staged = new GamepadState();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Device.added) InputSystem.RemoveDevice(Device);
            InputSystem.settings.backgroundBehavior = _previousBackground;
            Debug.Log("[AgentPlay] virtual gamepad removed");
        }
    }
}
