using System.Reflection;

namespace MoonTTS.Avalonia.Tests.TestSupport;

/* File Structure:
        - InvokeStatic(Type type, string methodName, params object?[] arguments) : object?
        - InvokeStaticAsync(Type type, string methodName, params object?[] arguments) : Task
        - SetField(object target, string fieldName, object? value) : void
        - GetStaticField(Type type, string fieldName) : object?
*/

/* Reaches members App and Program keep private (start-up wiring with no public seam). Reflection is fine here:
   the test assembly is never compiled AOT. A renamed member fails the test with a clear message */
public static class GuiPrivate
{
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /* Calls a static method and returns its result */
    public static object? InvokeStatic(Type type, string methodName, params object?[] arguments)
    {
        var method = type.GetMethod(methodName, AnyStatic);
        Assert.True(method != null, $"{type.Name}.{methodName} was not found");
        return method.Invoke(null, arguments);
    }

    /* Calls a static async method and awaits it */
    public static Task InvokeStaticAsync(Type type, string methodName, params object?[] arguments)
    {
        var task = InvokeStatic(type, methodName, arguments) as Task;
        Assert.NotNull(task);
        return task;
    }

    /* Overwrites an instance field (used to hand a dummy OS hook to GlobalShortcutManager) */
    public static void SetField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field != null, $"{target.GetType().Name}.{fieldName} was not found");
        field.SetValue(target, value);
    }

    /* Reads a static field or constant (e.g. the single-instance pipe name) */
    public static object? GetStaticField(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, AnyStatic);
        Assert.True(field != null, $"{type.Name}.{fieldName} was not found");
        return field.GetValue(null);
    }
}
