using Tmds.DBus.Protocol;

namespace MoonTTS.Infrastructure.Tests.OS.Linux.Wayland;

/* File Structure:
        - PortalShortcut(string Id, string Description, string? PreferredTrigger)
        - CreateSessionCall(string Sender, string HandleToken, string SessionHandleToken, string SessionHandle)
        - BindShortcutsCall(string Sender, string SessionHandle, IReadOnlyList<PortalShortcut> Shortcuts, string ParentWindow, string HandleToken)
        - SessionHandleFormat
        - DummyDesktopPortal
            - StartAsync(CancellationToken cancellationToken) : Task<DummyDesktopPortal>
            - RejectRegister / CreateSessionResponse / BindShortcutsResponse / SessionHandleFormat /
              AnswerCreateSession / RequestPathOverride / RejectClose : behaviour switches
            - RegisteredAppIds / CreateSessionCalls / BindShortcutsCalls / ClosedSessions / ClientNames : recordings
            - Path : string
            - HandlesChildPaths : bool
            - HandleMethodAsync(MethodContext context) : ValueTask
            - EmitActivated(string sessionHandle, string shortcutId) : void
            - IsConnectedAsync(string uniqueName) : Task<bool>
            - WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken) : Task
            - Dispose() : void
*/

/* A shortcut as received by BindShortcuts */
internal sealed record PortalShortcut(string Id, string Description, string? PreferredTrigger);

/* One GlobalShortcuts.CreateSession call */
internal sealed record CreateSessionCall(string Sender, string HandleToken, string SessionHandleToken, string SessionHandle);

/* One GlobalShortcuts.BindShortcuts call */
internal sealed record BindShortcutsCall(
    string Sender, string SessionHandle, IReadOnlyList<PortalShortcut> Shortcuts, string ParentWindow, string HandleToken);

/* How the CreateSession Response carries the session handle */
internal enum SessionHandleFormat
{
    String,     /* "s", as the portal specification says */
    ObjectPath, /* "o", as some portal backends send it */
    Missing,    /* no session_handle at all */
}

/* A dummy xdg-desktop-portal: owns org.freedesktop.portal.Desktop on the private bus and answers the
   GlobalShortcuts, Session and host Registry interfaces the way the real portal does: method calls return a
   Request object path, and the outcome arrives later as an org.freedesktop.portal.Request.Response signal sent
   to the caller on that path */
internal sealed class DummyDesktopPortal : IPathMethodHandler, IDisposable
{
    public const string BusName = "org.freedesktop.portal.Desktop";
    public const string DesktopPath = "/org/freedesktop/portal/desktop";
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly DBusConnection _connection;
    private readonly Lock _lock = new();
    private readonly List<string> _registeredAppIds = new();
    private readonly List<CreateSessionCall> _createSessionCalls = new();
    private readonly List<BindShortcutsCall> _bindShortcutsCalls = new();
    private readonly List<string> _closedSessions = new();
    private readonly List<string> _clientNames = new();

    /* Registry.Register answers with an error */
    public bool RejectRegister { get; set; }

    /* Response code of the CreateSession request (0 success, 1 cancelled by the user, 2 other error) */
    public uint CreateSessionResponse { get; set; }

    /* Response code of the BindShortcuts request */
    public uint BindShortcutsResponse { get; set; }

    /* How the session handle is sent back */
    public SessionHandleFormat SessionHandleFormat { get; set; } = SessionHandleFormat.String;

    /* When false, CreateSession returns its request path but the Response signal never comes */
    public bool AnswerCreateSession { get; set; } = true;

    /* Request path returned by method calls instead of the standard one (the Response still uses the standard one) */
    public string? RequestPathOverride { get; set; }

    /* Session.Close answers with an error */
    public bool RejectClose { get; set; }

    /* App ids passed to Registry.Register */
    public IReadOnlyList<string> RegisteredAppIds { get { lock (_lock) return _registeredAppIds.ToList(); } }

    /* Every CreateSession call */
    public IReadOnlyList<CreateSessionCall> CreateSessionCalls { get { lock (_lock) return _createSessionCalls.ToList(); } }

    /* Every BindShortcuts call */
    public IReadOnlyList<BindShortcutsCall> BindShortcutsCalls { get { lock (_lock) return _bindShortcutsCalls.ToList(); } }

    /* Object paths of every closed session */
    public IReadOnlyList<string> ClosedSessions { get { lock (_lock) return _closedSessions.ToList(); } }

    /* Unique bus names of the clients that called the portal */
    public IReadOnlyList<string> ClientNames { get { lock (_lock) return _clientNames.Distinct().ToList(); } }

    /* Object path served by this handler */
    public string Path => DesktopPath;

    /* Session objects live below the desktop path */
    public bool HandlesChildPaths => true;

    /* Wraps a connected connection */
    private DummyDesktopPortal(DBusConnection connection)
    {
        _connection = connection;
    }

    /* Connects to the private session bus and takes the portal's well-known name */
    public static async Task<DummyDesktopPortal> StartAsync(CancellationToken cancellationToken)
    {
        var connection = new DBusConnection(PrivateSessionBus.Address);
        await connection.ConnectAsync().AsTask().WaitAsync(WaitTimeout, cancellationToken);
        var portal = new DummyDesktopPortal(connection);
        connection.AddMethodHandler(portal);
        await connection.RequestNameAsync(BusName, RequestNameOptions.Default).WaitAsync(WaitTimeout, cancellationToken);
        return portal;
    }

    /* Dispatches portal method calls */
    public ValueTask HandleMethodAsync(MethodContext context)
    {
        Message request = context.Request;
        lock (_lock)
            _clientNames.Add(request.SenderAsString ?? string.Empty);

        switch ((request.InterfaceAsString, request.MemberAsString))
        {
            case ("org.freedesktop.host.portal.Registry", "Register"):
                HandleRegister(context);
                break;
            case ("org.freedesktop.portal.GlobalShortcuts", "CreateSession"):
                HandleCreateSession(context);
                break;
            case ("org.freedesktop.portal.GlobalShortcuts", "BindShortcuts"):
                HandleBindShortcuts(context);
                break;
            case ("org.freedesktop.portal.Session", "Close"):
                HandleClose(context);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
        return ValueTask.CompletedTask;
    }

    /* Emits GlobalShortcuts.Activated(o session_handle, s shortcut_id, t timestamp, a{sv} options) */
    public void EmitActivated(string sessionHandle, string shortcutId)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(null, DesktopPath, "org.freedesktop.portal.GlobalShortcuts", "Activated", "osta{sv}");
        writer.WriteObjectPath(sessionHandle);
        writer.WriteString(shortcutId);
        writer.WriteUInt64((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        writer.WriteDictionaryEnd(writer.WriteDictionaryStart());
        _connection.TrySendMessage(writer.CreateMessage());
    }

    /* True while a client with that unique name is connected to the bus */
    public async Task<bool> IsConnectedAsync(string uniqueName) =>
        (await _connection.ListServicesAsync()).Contains(uniqueName);

    /* Polls until the condition holds; fails the test after a generous timeout */
    public async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The dummy portal never reached the expected state.");
            await Task.Delay(10, cancellationToken);
        }
    }

    /* Releases the portal name and disconnects */
    public void Dispose() => _connection.Dispose();

    /* Registry.Register(s app_id, a{sv} options) */
    private void HandleRegister(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        string appId = reader.ReadString();
        lock (_lock)
            _registeredAppIds.Add(appId);

        if (RejectRegister)
        {
            context.ReplyError("org.freedesktop.portal.Error.NotAllowed", "An app id has already been set for this connection");
            return;
        }
        ReplyEmpty(context);
    }

    /* GlobalShortcuts.CreateSession(a{sv} options) -> o handle */
    private void HandleCreateSession(MethodContext context)
    {
        string sender = context.Request.SenderAsString ?? string.Empty;
        var options = context.Request.GetBodyReader().ReadDictionaryOfStringToVariantValue();
        string handleToken = options["handle_token"].GetString();
        string sessionToken = options["session_handle_token"].GetString();
        string sessionHandle = $"{DesktopPath}/session/{SenderPathPart(sender)}/{sessionToken}";
        lock (_lock)
            _createSessionCalls.Add(new CreateSessionCall(sender, handleToken, sessionToken, sessionHandle));

        string requestPath = RequestPath(sender, handleToken);
        ReplyRequestPath(context, requestPath);
        if (!AnswerCreateSession)
            return;

        bool sendHandle = CreateSessionResponse == 0 && SessionHandleFormat != SessionHandleFormat.Missing;
        EmitResponse(sender, requestPath, CreateSessionResponse, sendHandle ? sessionHandle : null);
    }

    /* GlobalShortcuts.BindShortcuts(o session_handle, a(sa{sv}) shortcuts, s parent_window, a{sv} options) -> o handle */
    private void HandleBindShortcuts(MethodContext context)
    {
        string sender = context.Request.SenderAsString ?? string.Empty;
        var reader = context.Request.GetBodyReader();
        string sessionHandle = reader.ReadObjectPathAsString();

        var shortcuts = new List<PortalShortcut>();
        ArrayEnd end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            string id = reader.ReadString();
            var properties = reader.ReadDictionaryOfStringToVariantValue();
            string? trigger = properties.TryGetValue("preferred_trigger", out var value) ? value.GetString() : null;
            shortcuts.Add(new PortalShortcut(id, properties["description"].GetString(), trigger));
        }

        string parentWindow = reader.ReadString();
        string handleToken = reader.ReadDictionaryOfStringToVariantValue()["handle_token"].GetString();
        lock (_lock)
            _bindShortcutsCalls.Add(new BindShortcutsCall(sender, sessionHandle, shortcuts, parentWindow, handleToken));

        string requestPath = RequestPath(sender, handleToken);
        ReplyRequestPath(context, requestPath);
        EmitResponse(sender, requestPath, BindShortcutsResponse, sessionHandle: null);
    }

    /* Session.Close() on the session object path */
    private void HandleClose(MethodContext context)
    {
        lock (_lock)
            _closedSessions.Add(context.Request.PathAsString ?? string.Empty);

        if (RejectClose)
        {
            context.ReplyError("org.freedesktop.DBus.Error.UnknownObject", "No such session");
            return;
        }
        ReplyEmpty(context);
    }

    /* Replies to a method call that returns nothing */
    private static void ReplyEmpty(MethodContext context)
    {
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }

    /* Replies with the Request object path the outcome will be signalled on */
    private void ReplyRequestPath(MethodContext context, string requestPath)
    {
        using var writer = context.CreateReplyWriter("o");
        writer.WriteObjectPath(RequestPathOverride ?? requestPath);
        context.Reply(writer.CreateMessage());
    }

    /* Sends Request.Response(u response, a{sv} results) to the caller; results hold the session handle when given,
       in the configured SessionHandleFormat */
    private void EmitResponse(string destination, string requestPath, uint response, string? sessionHandle)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(destination, requestPath, "org.freedesktop.portal.Request", "Response", "ua{sv}");
        writer.WriteUInt32(response);
        var results = writer.WriteDictionaryStart();
        if (sessionHandle != null)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString("session_handle");
            if (SessionHandleFormat == SessionHandleFormat.ObjectPath)
                writer.WriteVariantObjectPath(sessionHandle);
            else
                writer.WriteVariantString(sessionHandle);
        }
        writer.WriteDictionaryEnd(results);
        _connection.TrySendMessage(writer.CreateMessage());
    }

    /* Standard request path: /org/freedesktop/portal/desktop/request/<sender without ':' and '.' as '_'>/<token> */
    private static string RequestPath(string sender, string token) => $"{DesktopPath}/request/{SenderPathPart(sender)}/{token}";

    /* ":1.42" -> "1_42" */
    private static string SenderPathPart(string sender) => sender.TrimStart(':').Replace('.', '_');
}
