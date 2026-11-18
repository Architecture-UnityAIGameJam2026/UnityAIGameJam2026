using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
UnityEditor.EditorWindow.FocusWindowIfItsOpen(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));
InputSystem.QueueStateEvent(Keyboard.current,new KeyboardState(Key.F3));
return "Queued F3 for the next game input update.";
