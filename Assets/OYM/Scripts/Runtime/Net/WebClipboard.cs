#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace OYM.Net
{
    /// Copy to the browser clipboard (GUIUtility.systemCopyBuffer stays inside the page on WebGL).
    public static class WebClipboard
    {
        [DllImport("__Internal")] static extern void OYM_CopyText(string text);
        public static void Copy(string text) { if (!string.IsNullOrEmpty(text)) OYM_CopyText(text); }
    }
}
#endif
