using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MoonTTS.Infrastructure.OS.Windows;

/* File Structure:
        - ITaskbarList
        - ITaskbarList2
*/

/* Shell taskbar COM interface, see https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-itaskbarlist */
[GeneratedComInterface]
[Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
internal partial interface ITaskbarList
{
    void HrInit();
    void AddTab(nint hwnd);
    void DeleteTab(nint hwnd);
    void ActivateTab(nint hwnd);
    void SetActiveAlt(nint hwnd);
}

/* Shell taskbar COM interface extension that lets a window be flagged as fullscreen so the taskbar stays behind it,
   see https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-itaskbarlist2 */
[GeneratedComInterface]
[Guid("602D4995-B13A-429b-A66E-1935E44F4317")]
internal partial interface ITaskbarList2 : ITaskbarList
{
    void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
}
