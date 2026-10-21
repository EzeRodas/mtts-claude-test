using MoonTTS.Domain.Interfaces;
using MoonTTS.Domain.ValueObjects;
using Tmds.DBus.Protocol;

namespace MoonTTS.Infrastructure.OS.Linux.Wayland;

/* File Structure:
        - InitializeAsync() : Task
        - RegisterAppIdAsync(DBusConnection connection, bool entryJustWritten) : Task
        - RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string,ShortcutCombo> phraseShortcuts) : Task
        - FormatForWayland(ShortcutCombo combo) : string
        - WaitForResponseAsync(ObjectPath requestPath) : Task<IDictionary<string,object>>
        - CleanupSessionAsync() : Task
        - Dispose() : void
*/

/* This class is in charge of registering the global shortcuts on Wayland though the freedesktop protocol.
   Of course as Wayland does not support this, we're actually talking to the Desktop Environment (as far as I
   understand). So for distributions that use KDE or other DE that supports freedesktop, it should work. if your
   DE doesn't support it, I'm sorry but this is as much as Wayland allows to do. */

/* While yes, I do understand that global shortcuts can be used maliciously by bad actors and blah blah blah,
   I'd love to have a little chat with the Wayland people and know if they ever tried to make an app that's
   not a calculator. Next part of this rant when I'll have to implement the global positioning method. */

public class WaylandShortcutHook : IGlobalShortcutHook
{
    private const string AppId = "com.moontts.app";
    
    private DBusConnection? _connection;
    private IDisposable? _activationWatcher;
    private string? _sessionHandle;
    
    /* Event for triggering shortcut actions */
    public event Action<string>? OnShortcutTriggered;

    /* Initializes connection to DBus session and sibscribes to shortcut activation signals */
    public async Task InitializeAsync()
    {
        string? address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address))
        {
            Console.WriteLine("[WaylandShortcutHook] DBusAddress.Session is null or empty.");
            return;
        }

        _connection = new DBusConnection(address);
        await _connection.ConnectAsync();
        Console.WriteLine($"[WaylandShortcutHook] Connected to D-Bus. UniqueName: {_connection.UniqueName}");

        /* Registers the app ID with the portal registry (required when launched outside an IDE/launcher).
           The portal only accepts IDs backed by a matching .desktop entry, so it gets provisioned first */
        var desktopEntry = LinuxDesktopEntry.EnsureInstalled(AppId);
        if (desktopEntry.IsFailure)
            Console.WriteLine($"[WaylandShortcutHook] Desktop entry warning: {desktopEntry.ErrorMessage}");

        await RegisterAppIdAsync(_connection, entryJustWritten: desktopEntry.Value);

        /* Subscribes to GlobalShortcuts activated signal */
        _activationWatcher = await _connection.WatchSignalAsync<string>(
            sender: null,
            path: "/org/freedesktop/portal/desktop",
            @interface: "org.freedesktop.portal.GlobalShortcuts",
            signal: "Activated",
            reader: (Message msg, object? _) =>
            {
                var reader = msg.GetBodyReader();
                _ = reader.ReadObjectPath(); /* session_handle */
                return reader.ReadString();  /* shortcut_id */
            },
            handler: notification =>
            {
                if (notification.Type == NotificationType.Value && !string.IsNullOrEmpty(notification.Value))
                {
                    Console.WriteLine($"[WaylandShortcutHook] Shortcut Activated: {notification.Value}");
                    OnShortcutTriggered?.Invoke(notification.Value);
                }
                else if (notification.Exception != null)
                {
                    Console.WriteLine($"[WaylandShortcutHook] Activation watcher error: {notification.Exception}");
                }
            },
            flags: ObserverFlags.None,
            emitOnCapturedContext: false);

        Console.WriteLine("[WaylandShortcutHook] Listening for Activated signals.");
    }

    /* Registers the app ID with the portal registry. It has to be the first portal call on the connection.
       A freshly written desktop entry reaches the portal through its file monitor, so in that case the
       registration is retried briefly until the portal has indexed the new entry */
    private static async Task RegisterAppIdAsync(DBusConnection connection, bool entryJustWritten)
    {
        const int retryDelayMs = 200;
        int maxAttempts = entryJustWritten ? 5 : 1;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await connection.CallMethodAsync(CreateRegisterMessage(connection));
                Console.WriteLine($"[WaylandShortcutHook] Registered AppId '{AppId}' with portal registry.");
                return;
            }
            catch (DBusErrorReplyException ex) when (attempt < maxAttempts && ex.ErrorName == "org.freedesktop.portal.Error.Failed")
            {
                await Task.Delay(retryDelayMs);
            }
            catch (Exception ex)
            {
                /* Portals older than the Registry interface derive the app ID from the cgroup instead */
                Console.WriteLine($"[WaylandShortcutHook] Registry.Register warning: {ex.Message}");
                return;
            }
        }

        static MessageBuffer CreateRegisterMessage(DBusConnection conn)
        {
            using var regWriter = conn.GetMessageWriter();
            regWriter.WriteMethodCallHeader(
                destination: "org.freedesktop.portal.Desktop",
                path: "/org/freedesktop/portal/desktop",
                @interface: "org.freedesktop.host.portal.Registry",
                member: "Register",
                signature: "sa{sv}");

            regWriter.WriteString(AppId);
            var dictStart = regWriter.WriteDictionaryStart();
            regWriter.WriteDictionaryEnd(dictStart);

            return regWriter.CreateMessage();
        }
    }
    
    /* Creates a portal session and binds shortcuts */
    public async Task RegisterShortcutAsync(ShortcutCombo? toggleShortcut, Dictionary<string, ShortcutCombo> phraseShortcuts)
    {
        if (_connection == null)
            throw new InvalidOperationException("Wayland hook not initialized.");

        await CleanupSessionAsync();

        string sessionToken = $"moontts_sess_{Guid.NewGuid():N}";
        
        /* Creates session */
        Console.WriteLine("[WaylandShortcutHook] Calling CreateSession...");
        var createResult = await SendRequestAsync(
            reqToken => _connection.CallMethodAsync(
                CreateSessionMessage(_connection, sessionToken, reqToken),
                (Message msg, object? _) => msg.GetBodyReader().ReadObjectPath().ToString()));

        if (!createResult.TryGetValue("session_handle", out var sessionHandleVariant))
            throw new InvalidOperationException("Portal CreateSession did not return a session_handle.");

        _sessionHandle = sessionHandleVariant.Type == VariantValueType.ObjectPath
            ? sessionHandleVariant.GetObjectPath().ToString()
            : sessionHandleVariant.GetString();
        
        Console.WriteLine($"[WaylandShortcutHook] Session created: {_sessionHandle}");
        
        /* Builds shortcut bindings */
        var shortcutsToBind = new List<(string Id, string Description, string Trigger)>();

        if (toggleShortcut != null)
        {
            shortcutsToBind.Add(
                ("toggle_app", "Show/Hide Moon-TTS", FormatForWayland(toggleShortcut)));
        }

        foreach (var phrase in phraseShortcuts)
        {
            shortcutsToBind.Add(
                ($"phrase_{phrase.Key}", $"Moon-TTS Phrase: {phrase.Key}", FormatForWayland(phrase.Value)));
        }

        if (shortcutsToBind.Count == 0) return;
        
        Console.WriteLine($"[WaylandShortcutHook] Calling BindShortcuts with {shortcutsToBind.Count} shortcut(s): " +
                          string.Join(", ", shortcutsToBind.Select(s => $"{s.Id}={s.Trigger}")));

        await SendRequestAsync(
            reqToken => _connection.CallMethodAsync(
                CreateBindShortcutsMessage(_connection, _sessionHandle, shortcutsToBind, reqToken),
                (Message msg, object? _) => msg.GetBodyReader().ReadObjectPath().ToString()),
            timeoutMs: 60000);

        Console.WriteLine("[WaylandShortcutHook] BindShortcuts succeeded");
        
        static MessageBuffer CreateSessionMessage(DBusConnection conn, string sessToken, string reqToken)
        {
            using var writer = conn.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.portal.Desktop",
                path: "/org/freedesktop/portal/desktop",
                @interface: "org.freedesktop.portal.GlobalShortcuts",
                member: "CreateSession",
                signature: "a{sv}");

            var start = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("session_handle_token");
            writer.WriteVariantString(sessToken);

            writer.WriteDictionaryEntryStart();
            writer.WriteString("handle_token");
            writer.WriteVariantString(reqToken);
            writer.WriteDictionaryEnd(start);

            return writer.CreateMessage();
        }

        static MessageBuffer CreateBindShortcutsMessage(
            DBusConnection conn,
            string sessionHandle,
            List<(string Id, string Description, string Trigger)> shortcuts,
            string reqToken)
        {
            using var writer = conn.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.portal.Desktop",
                path: "/org/freedesktop/portal/desktop",
                @interface: "org.freedesktop.portal.GlobalShortcuts",
                member: "BindShortcuts",
                signature: "oa(sa{sv})sa{sv}");

            writer.WriteObjectPath(sessionHandle);

            var arrayStart = writer.WriteArrayStart(DBusType.Struct);
            foreach (var item in shortcuts)
            {
                writer.WriteStructureStart();
                writer.WriteString(item.Id);

                var propsStart = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("description");
                writer.WriteVariantString(item.Description);

                if (!string.IsNullOrEmpty(item.Trigger))
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("preferred_trigger");
                    writer.WriteVariantString(item.Trigger);
                }
                writer.WriteDictionaryEnd(propsStart);
            }
            writer.WriteArrayEnd(arrayStart);

            writer.WriteString(""); /* parent_window */

            var optStart = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("handle_token");
            writer.WriteVariantString(reqToken);
            writer.WriteDictionaryEnd(optStart);

            return writer.CreateMessage();
        }
    }
    
    /* Subscribes to Request.Response, executes the action, and waits for the response */
    private async Task<Dictionary<string, VariantValue>> SendRequestAsync(
        Func<string, Task<string>> portalAction,
        int timeoutMs = 10000)
    {
        string reqToken = $"moontts_req_{Guid.NewGuid():N}";
        string sender = (_connection!.UniqueName ?? "").Replace(":", "").Replace(".", "_");
        string expectedReqPath = $"/org/freedesktop/portal/desktop/request/{sender}/{reqToken}";

        var tcs = new TaskCompletionSource<Dictionary<string, VariantValue>>();
        
        using var sub = await _connection
            .WatchSignalAsync<(uint ResponseCode, Dictionary<string, VariantValue> Results)>(
                sender: null,
                path: expectedReqPath,
                @interface: "org.freedesktop.portal.Request",
                signal: "Response",
                reader: (Message msg, object? _) =>
                {
                    var r = msg.GetBodyReader();
                    uint response = r.ReadUInt32();
                    var results = r.ReadDictionaryOfStringToVariantValue();
                    return (response, results);
                },
                handler: notification =>
                {
                    if (notification.Type == NotificationType.Value)
                    {
                        Console.WriteLine($"[WaylandShortcutHook] Portal Response received: code={notification.Value.ResponseCode}");
                        if (notification.Value.ResponseCode == 0)
                            tcs.TrySetResult(notification.Value.Results);
                        else
                            tcs.TrySetException(new InvalidOperationException(
                                $"Portal request failed with code: {notification.Value.ResponseCode}"));
                    }
                    else if (notification.Exception != null)
                    {
                        Console.WriteLine($"[WaylandShortcutHook] Request signal exception: {notification.Exception}");
                        tcs.TrySetException(notification.Exception);
                    }
                },
                flags: ObserverFlags.None,
                emitOnCapturedContext: false);
        
        string actualReqPath = await portalAction(reqToken);
        if (actualReqPath != expectedReqPath)
        {
            Console.WriteLine($"[WaylandShortcutHook] WARNING: Path mismatch! Expected: {expectedReqPath}, Actual: {actualReqPath}");
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        return await tcs.Task.WaitAsync(cts.Token);
    }

    /* Applies the expected formatting for Wayland / GTK */
    private string FormatForWayland(ShortcutCombo combo)
    {
        var parts = new List<string>();

        if (combo.HasCtrl) parts.Add("CTRL");
        if (combo.HasAlt) parts.Add("ALT");
        if (combo.HasShift) parts.Add("SHIFT");
        if (combo.HasWin) parts.Add("LOGO");

        string rawKey = combo.MainKey.Trim();
        string key = rawKey.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => "Return",
            "ESC" or "ESCAPE" => "Escape",
            "SPACE" => "space",
            "TAB" => "Tab",
            "BACKSPACE" => "BackSpace",
            "DELETE" or "DEL" => "Delete",
            "INSERT" or "INS" => "Insert",
            "HOME" => "Home",
            "END" => "End",
            "PAGEUP" or "PAGE_UP" or "PGUP" => "Page_Up",
            "PAGEDOWN" or "PAGE_DOWN" or "PGDN" => "Page_Down",
            "UP" or "ARROWUP" => "Up",
            "DOWN" or "ARROWDOWN" => "Down",
            "LEFT" or "ARROWLEFT" => "Left",
            "RIGHT" or "ARROWRIGHT" => "Right",
            "PLUS" or "ADD" => "plus",
            "MINUS" or "SUBTRACT" => "minus",
            /* Function keys F1-F24 */
            var k when k.Length > 1 && k[0] == 'F' && int.TryParse(k[1..], out _) => k,
            /* Single alphanumeric character */
            var k when k.Length == 1 => k.ToLowerInvariant(),
            /* Preserve exact casing for other valid XKB keysym names */
            _ => rawKey
        };

        parts.Add(key);
        return string.Join("+", parts);
    }

    /* If a session exists, it gets closed */
    private async Task CleanupSessionAsync()
    {
        if (string.IsNullOrEmpty(_sessionHandle) || _connection == null)
            return;
        
        try
        {
            await _connection.CallMethodAsync(CreateCloseSessionMessage(_connection, _sessionHandle));
        }
        catch
        {
             /* Ignore if already closed */
        }
        finally
        {
            _sessionHandle = null;
        }

        static MessageBuffer CreateCloseSessionMessage(DBusConnection conn, string sessionHandle)
        {
            using var writer = conn.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.portal.Desktop",
                path: sessionHandle,
                @interface: "org.freedesktop.portal.Session",
                member: "Close",
                signature: "");

            return writer.CreateMessage();
        }
    }

    /* Disposes the lsitener */
    public void Dispose()
    {
        _activationWatcher?.Dispose();
        _ = CleanupSessionAsync();
        _connection.Dispose();
    }
}


