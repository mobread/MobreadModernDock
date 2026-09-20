namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;

/// <summary>
/// Hand-written COM interop for the subset of UI Automation (uiautomationcore)
/// the tray gateway needs: create the automation object, get an element from a
/// window handle, search descendants by property, read name/id/rect, and
/// invoke. Interfaces must keep their exact vtable order, so unused members
/// are declared as placeholders. Only the members actually used have real
/// signatures; the rest are `void` slots.
/// </summary>
public static class UiaInterop
{
    public const int UIA_NamePropertyId = 30005;
    public const int UIA_AutomationIdPropertyId = 30011;
    public const int UIA_ClassNamePropertyId = 30012;
    public const int UIA_InvokePatternId = 10000;

    public static readonly Guid CLSID_CUIAutomation8 = new("E22AD333-B25F-460C-83D0-0581107395C9");
    public static readonly Guid CLSID_CUIAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");

    public static IUIAutomation Create()
    {
        // CUIAutomation8 (Win8+) sees XAML-island content; fall back to the
        // classic factory if unavailable.
        foreach (var clsid in new[] { CLSID_CUIAutomation8, CLSID_CUIAutomation })
        {
            var type = Type.GetTypeFromCLSID(clsid);
            if (type == null) continue;
            if (Activator.CreateInstance(type) is IUIAutomation uia)
                return uia;
        }
        throw new InvalidOperationException("UI Automation is not available.");
    }
}

public enum TreeScope
{
    Element = 1,
    Children = 2,
    Descendants = 4,
    Subtree = 7,
}

[StructLayout(LayoutKind.Sequential)]
public struct UiaRect
{
    public int left, top, right, bottom;
}

[ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUIAutomation
{
    void CompareElements_();
    void CompareRuntimeIds_();
    void GetRootElement_();
    [return: MarshalAs(UnmanagedType.Interface)]
    IUIAutomationElement ElementFromHandle(IntPtr hwnd);
    void ElementFromPoint_();
    void GetFocusedElement_();
    void GetRootElementBuildCache_();
    void ElementFromHandleBuildCache_();
    void ElementFromPointBuildCache_();
    void GetFocusedElementBuildCache_();
    void CreateTreeWalker_();
    void get_ControlViewWalker_();
    void get_ContentViewWalker_();
    void get_RawViewWalker_();
    void get_RawViewCondition_();
    void get_ControlViewCondition_();
    void get_ContentViewCondition_();
    void CreateCacheRequest_();
    void CreateTrueCondition_();
    void CreateFalseCondition_();
    [return: MarshalAs(UnmanagedType.Interface)]
    IUIAutomationCondition CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
    // Remaining members are unused; the vtable beyond this point is never called.
}

[ComImport, Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUIAutomationCondition { }

[ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUIAutomationElement
{
    void SetFocus_();
    void GetRuntimeId_();
    [return: MarshalAs(UnmanagedType.Interface)]
    IUIAutomationElement FindFirst(TreeScope scope, IUIAutomationCondition condition);
    [return: MarshalAs(UnmanagedType.Interface)]
    IUIAutomationElementArray FindAll(TreeScope scope, IUIAutomationCondition condition);
    void FindFirstBuildCache_();
    void FindAllBuildCache_();
    void BuildUpdatedCache_();
    [return: MarshalAs(UnmanagedType.Struct)]
    object GetCurrentPropertyValue(int propertyId);
    void GetCurrentPropertyValueEx_();
    void GetCachedPropertyValue_();
    void GetCachedPropertyValueEx_();
    void GetCurrentPatternAs_();
    void GetCachedPatternAs_();
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object GetCurrentPattern(int patternId);
    void GetCachedPattern_();
    void GetCachedParent_();
    void GetCachedChildren_();
    int get_CurrentProcessId();
    int get_CurrentControlType();
    void get_CurrentLocalizedControlType_();
    [return: MarshalAs(UnmanagedType.BStr)]
    string get_CurrentName();
    void get_CurrentAcceleratorKey_();
    void get_CurrentAccessKey_();
    void get_CurrentHasKeyboardFocus_();
    void get_CurrentIsKeyboardFocusable_();
    void get_CurrentIsEnabled_();
    [return: MarshalAs(UnmanagedType.BStr)]
    string get_CurrentAutomationId();
    [return: MarshalAs(UnmanagedType.BStr)]
    string get_CurrentClassName();
    void get_CurrentHelpText_();
    void get_CurrentCulture_();
    void get_CurrentIsControlElement_();
    void get_CurrentIsContentElement_();
    void get_CurrentIsPassword_();
    void get_CurrentNativeWindowHandle_();
    void get_CurrentItemType_();
    void get_CurrentIsOffscreen_();
    void get_CurrentOrientation_();
    void get_CurrentFrameworkId_();
    void get_CurrentIsRequiredForForm_();
    void get_CurrentItemStatus_();
    UiaRect get_CurrentBoundingRectangle();
    // Remaining members unused.
}

[ComImport, Guid("14314595-B4BC-4055-95F2-58F2E42C9855"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUIAutomationElementArray
{
    int get_Length();
    [return: MarshalAs(UnmanagedType.Interface)]
    IUIAutomationElement GetElement(int index);
}

[ComImport, Guid("FB377FBE-8EA6-46D5-9C73-6499642D3059"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IUIAutomationInvokePattern
{
    void Invoke();
}
